using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;

namespace PlanetTrader.Core
{
    /// <summary>
    /// Сохранение партии: снимок всего её состояния в JSON — планеты, корпорации, магнаты, рынок, события, зонды,
    /// заказы и генераторы случайных чисел (загруженная партия продолжается в точности так же).
    ///
    /// Поля обходятся сами (все поля объектов, кроме помеченных [NonSerialized]), поэтому новое поле в классе
    /// сохраняется без правок здесь. Объекты, на которые ссылаются из нескольких мест (корпорации, магнаты),
    /// пишутся один раз и дальше — по номеру. Вещи из баланса (типы планет, зонды, технологии, события,
    /// персонажи) пишутся по имени и при загрузке берутся из нынешнего баланса: после обновления игры старая партия
    /// играет по новым числам. Чего в старом сохранении нет — новое поле, новый ресурс — получает значение
    /// по умолчанию (пустой список, нулевой склад, базовую цену).
    /// </summary>
    public static class SaveGame
    {
        /// <summary>Версия устройства сохранения; растёт, если старое придётся переводить вручную.</summary>
        public const int Version = 1;

        public static string Save(Game game)
        {
            var w = new Writer(BalanceKeys(game));
            w.Sb.Append("{\"version\":").Append(Version).Append(",\"game\":");
            w.Value(game);
            w.Sb.Append('}');
            return w.Sb.ToString();
        }

        /// <summary>Партия из сохранения; balance — нынешний баланс игры (по нему разрешаются типы планет, зонды и прочее).</summary>
        public static Game Load(string json, Balance balance)
        {
            try
            {
                return LoadUnchecked(json, balance);
            }
            catch (Exception e) when (!(e is FormatException))
            {
                throw new FormatException("Сохранение повреждено или не подходит к этой версии игры: " + e.Message, e);
            }
        }

        static Game LoadUnchecked(string json, Balance balance)
        {
            var root = new Parser(json).Parse() as Dictionary<string, object>
                       ?? throw new FormatException("Сохранение повреждено: нет корня");
            if (!root.TryGetValue("game", out var node) || !(node is Dictionary<string, object> gameNode))
                throw new FormatException("Сохранение повреждено: нет партии");

            // Партия создаётся без конструктора: всё её состояние — из сохранения. Баланс и кэши — сразу,
            // чтобы по ним находились типы планет и прочее из баланса.
            var game = (Game)FormatterServices.GetUninitializedObject(typeof(Game));
            SetField(game, "Balance", balance);
            InitDefaults(game, null);
            var reader = new Reader(Invert(BalanceKeys(game)));
            reader.Fill(game, gameNode);
            PadResourceArrays(game, reader.Objects);
            return game;
        }

        // ------------------------------------------------------------------
        // Что берётся из баланса — по имени
        // ------------------------------------------------------------------

        static Dictionary<object, string> BalanceKeys(Game g)
        {
            var b = g.Balance;
            var keys = new Dictionary<object, string>(Same.Instance) { [b] = "balance" };
            void Add(object o, string key)
            {
                if (o != null && !keys.ContainsKey(o)) keys[o] = key;
            }
            for (int s = 0; s < b.Sectors.Length; s++)
            {
                Add(b.Sectors[s], "sector:" + s);
                foreach (var t in g.SectorTypes(s)) Add(t, $"type:{s}:{t.Resource}");
                if (b.Sectors[s].Magnates != null)
                    for (int i = 0; i < b.Sectors[s].Magnates.Length; i++) Add(b.Sectors[s].Magnates[i], $"sectormagnate:{s}:{i}");
            }
            foreach (var t in b.PlanetTypes) Add(t, $"type:base:{t.Resource}");
            foreach (var p in b.ProbeTypes) Add(p, "probe:" + p.Name);
            foreach (var t in b.Technologies) Add(t, "tech:" + t.Id);
            foreach (var e in b.Events) Add(e, "event:" + e.Name);
            foreach (var c in b.Characters) Add(c, "character:" + c.Name);
            for (int i = 0; i < b.CorporationLevels.Length; i++) Add(b.CorporationLevels[i], "level:" + i);
            for (int i = 0; i < b.Magnates.Length; i++) Add(b.Magnates[i], "magnate:" + i);
            foreach (var t in b.ShowTasks) Add(t, "showtask:" + t.Kind);
            foreach (var x in b.Incidents) Add(x, "incident:" + x.Id);
            return keys;
        }

        static Dictionary<string, object> Invert(Dictionary<object, string> keys)
        {
            var map = new Dictionary<string, object>();
            foreach (var kv in keys)
                if (!map.ContainsKey(kv.Value)) map[kv.Value] = kv.Key;
            return map;
        }

        // ------------------------------------------------------------------
        // Поля
        // ------------------------------------------------------------------

        static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();

        static FieldInfo[] Fields(Type t)
        {
            lock (FieldCache)
            {
                if (FieldCache.TryGetValue(t, out var fields)) return fields;
                var list = new List<FieldInfo>();
                for (var x = t; x != null && x != typeof(object); x = x.BaseType)
                    foreach (var f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) list.Add(f);
                fields = list.ToArray();
                FieldCache[t] = fields;
                return fields;
            }
        }

        static void SetField(object o, string name, object value)
        {
            foreach (var f in Fields(o.GetType()))
                if (f.Name == name)
                {
                    f.SetValue(o, value);
                    return;
                }
        }

        static bool IsCollection(Type t) =>
            t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(List<>) || t.GetGenericTypeDefinition() == typeof(HashSet<>)
                                || t.GetGenericTypeDefinition() == typeof(Dictionary<,>));

        /// <summary>Пустые списки, множества и словари — тем полям, которых нет в сохранении или которые не сохраняются.</summary>
        static void InitDefaults(object o, ICollection<string> present)
        {
            foreach (var f in Fields(o.GetType()))
            {
                if (present != null && present.Contains(f.Name) && !f.IsNotSerialized) continue;
                if (f.GetValue(o) == null && IsCollection(f.FieldType)) f.SetValue(o, Activator.CreateInstance(f.FieldType));
            }
        }

        /// <summary>Склады и цены по ресурсам: в старом сохранении ресурсов могло быть меньше.</summary>
        static void PadResourceArrays(Game game, IEnumerable<object> objects)
        {
            var b = game.Balance;
            foreach (var o in objects)
            {
                if (o is Corporation c && c.Stock.Length < ResourceTypes.Count) SetField(c, "Stock", Pad(c.Stock, _ => 0));
                if (o is Market m)
                {
                    foreach (var f in Fields(typeof(Market)))
                        if (f.FieldType == typeof(double[]) && f.GetValue(m) is double[] a && a.Length < ResourceTypes.Count)
                            f.SetValue(m, Pad(a, i => f.Name == "_price" ? b.BasePrice((Resource)i) : 1));
                }
            }
        }

        static double[] Pad(double[] a, Func<int, double> fill)
        {
            var r = new double[ResourceTypes.Count];
            for (int i = 0; i < r.Length; i++) r[i] = i < a.Length ? a[i] : fill(i);
            return r;
        }

        sealed class Same : IEqualityComparer<object>
        {
            public static readonly Same Instance = new Same();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }

        // ------------------------------------------------------------------
        // Запись
        // ------------------------------------------------------------------

        sealed class Writer
        {
            public readonly StringBuilder Sb = new StringBuilder(1 << 16);
            readonly Dictionary<object, string> _balance;
            readonly Dictionary<object, int> _ids = new Dictionary<object, int>(Same.Instance);

            public Writer(Dictionary<object, string> balance) => _balance = balance;

            public void Value(object v)
            {
                switch (v)
                {
                    case null: Sb.Append("null"); return;
                    case string s: Text(s); return;
                    case bool b: Sb.Append(b ? "true" : "false"); return;
                    case double d: Number(d); return;
                    case float f: Number(f); return;
                    case Enum e: Text(e.ToString()); return;
                }
                var t = v.GetType();
                if (t.IsPrimitive)
                {
                    Sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                    return;
                }
                if (_balance.TryGetValue(v, out var key))
                {
                    Sb.Append("{\"$balance\":");
                    Text(key);
                    Sb.Append('}');
                    return;
                }
                if (v is IEnumerable items && !(v is IDictionary))
                {
                    Sb.Append('[');
                    bool first = true;
                    foreach (var item in items)
                    {
                        if (!first) Sb.Append(',');
                        first = false;
                        Value(item);
                    }
                    Sb.Append(']');
                    return;
                }
                if (_ids.TryGetValue(v, out int id))
                {
                    Sb.Append("{\"$ref\":").Append(id).Append('}');
                    return;
                }
                id = _ids.Count + 1;
                _ids[v] = id;
                Sb.Append("{\"$id\":").Append(id);
                foreach (var f in Fields(t))
                {
                    if (f.IsNotSerialized) continue;
                    Sb.Append(',');
                    Text(f.Name);
                    Sb.Append(':');
                    Value(f.GetValue(v));
                }
                Sb.Append('}');
            }

            void Number(double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) Text(d.ToString(CultureInfo.InvariantCulture));
                else Sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }

            void Text(string s)
            {
                Sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': Sb.Append("\\\""); break;
                        case '\\': Sb.Append("\\\\"); break;
                        case '\n': Sb.Append("\\n"); break;
                        case '\r': Sb.Append("\\r"); break;
                        case '\t': Sb.Append("\\t"); break;
                        default:
                            if (c < ' ') Sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else Sb.Append(c);
                            break;
                    }
                }
                Sb.Append('"');
            }
        }

        // ------------------------------------------------------------------
        // Чтение
        // ------------------------------------------------------------------

        /// <summary>Число из JSON как текст: тип поля решает, во что его превратить (ulong не теряет точности).</summary>
        sealed class Num
        {
            public readonly string Text;
            public Num(string text) => Text = text;
        }

        sealed class Reader
        {
            readonly Dictionary<string, object> _balance;
            readonly Dictionary<long, object> _objects = new Dictionary<long, object>();

            public Reader(Dictionary<string, object> balance) => _balance = balance;

            public IEnumerable<object> Objects => _objects.Values;

            /// <summary>Заполнить готовый объект полями из узла.</summary>
            public void Fill(object o, Dictionary<string, object> node)
            {
                if (node.TryGetValue("$id", out var id)) _objects[long.Parse(((Num)id).Text, CultureInfo.InvariantCulture)] = o;
                foreach (var f in Fields(o.GetType()))
                {
                    if (f.IsNotSerialized || !node.TryGetValue(f.Name, out var value)) continue;
                    if (f.Name == "Balance" && o is Game) continue;
                    f.SetValue(o, Read(value, f.FieldType));
                }
                InitDefaults(o, node.Keys);
            }

            object Read(object node, Type type)
            {
                if (node == null) return type.IsValueType ? Activator.CreateInstance(type) : null;
                var underlying = Nullable.GetUnderlyingType(type);
                if (underlying != null) return Read(node, underlying);
                if (type == typeof(string)) return node is Num n ? n.Text : node as string;
                if (type == typeof(bool)) return node is bool b && b;
                if (type.IsEnum) return ParseEnum(type, node as string);
                if (type.IsPrimitive) return ParseNumber(type, node is Num num ? num.Text : node as string);
                if (type.IsArray)
                {
                    var list = (List<object>)node;
                    var elem = type.GetElementType();
                    var array = Array.CreateInstance(elem, list.Count);
                    for (int i = 0; i < list.Count; i++) array.SetValue(Read(list[i], elem), i);
                    return array;
                }
                if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(HashSet<>)))
                {
                    var elem = type.GetGenericArguments()[0];
                    var collection = Activator.CreateInstance(type);
                    var add = type.GetMethod("Add");
                    foreach (var item in (List<object>)node) add.Invoke(collection, new[] { Read(item, elem) });
                    return collection;
                }
                var obj = (Dictionary<string, object>)node;
                if (obj.TryGetValue("$balance", out var key))
                    return _balance.TryGetValue((string)key, out var found) ? found : null;
                if (obj.TryGetValue("$ref", out var refId))
                    return _objects[long.Parse(((Num)refId).Text, CultureInfo.InvariantCulture)];
                var instance = Create(type);
                Fill(instance, obj);
                return instance;
            }

            static object Create(Type type)
            {
                // С конструктором без параметров — через него (сработают значения полей по умолчанию), иначе — пустой объект.
                var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                return ctor != null ? ctor.Invoke(null) : FormatterServices.GetUninitializedObject(type);
            }

            static object ParseEnum(Type type, string name)
            {
                try
                {
                    return Enum.Parse(type, name);
                }
                catch (Exception)
                {
                    return Activator.CreateInstance(type);
                }
            }

            static object ParseNumber(Type type, string s)
            {
                var c = CultureInfo.InvariantCulture;
                if (type == typeof(double)) return double.Parse(s, NumberStyles.Float, c);
                if (type == typeof(float)) return float.Parse(s, NumberStyles.Float, c);
                if (type == typeof(int)) return int.Parse(s, c);
                if (type == typeof(long)) return long.Parse(s, c);
                if (type == typeof(ulong)) return ulong.Parse(s, c);
                if (type == typeof(uint)) return uint.Parse(s, c);
                if (type == typeof(short)) return short.Parse(s, c);
                if (type == typeof(byte)) return byte.Parse(s, c);
                if (type == typeof(char)) return s.Length > 0 ? s[0] : '\0';
                return Convert.ChangeType(s, type, c);
            }
        }

        /// <summary>Разбор JSON в словари, списки, строки, числа-текст, true/false и null.</summary>
        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) => _s = s;

            public object Parse()
            {
                var v = Value();
                Skip();
                if (_i != _s.Length) throw Error("лишние символы в конце");
                return v;
            }

            FormatException Error(string what) => new FormatException($"Сохранение повреждено: {what} (позиция {_i})");

            void Skip()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            object Value()
            {
                Skip();
                if (_i >= _s.Length) throw Error("неожиданный конец");
                char c = _s[_i];
                if (c == '{') return Object();
                if (c == '[') return Array();
                if (c == '"') return Text();
                if (c == 't' && Take("true")) return true;
                if (c == 'f' && Take("false")) return false;
                if (c == 'n' && Take("null")) return null;
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                if (start == _i) throw Error("непонятный символ");
                return new Num(_s.Substring(start, _i - start));
            }

            bool Take(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) return false;
                _i += word.Length;
                return true;
            }

            Dictionary<string, object> Object()
            {
                var d = new Dictionary<string, object>();
                _i++;
                Skip();
                if (_s[_i] == '}')
                {
                    _i++;
                    return d;
                }
                while (true)
                {
                    Skip();
                    string key = Text();
                    Skip();
                    if (_s[_i++] != ':') throw Error("нет двоеточия");
                    d[key] = Value();
                    Skip();
                    char c = _s[_i++];
                    if (c == '}') return d;
                    if (c != ',') throw Error("нет запятой");
                }
            }

            List<object> Array()
            {
                var list = new List<object>();
                _i++;
                Skip();
                if (_s[_i] == ']')
                {
                    _i++;
                    return list;
                }
                while (true)
                {
                    list.Add(Value());
                    Skip();
                    char c = _s[_i++];
                    if (c == ']') return list;
                    if (c != ',') throw Error("нет запятой");
                }
            }

            string Text()
            {
                if (_s[_i] != '"') throw Error("нет кавычки");
                _i++;
                var sb = new StringBuilder();
                while (true)
                {
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    char e = _s[_i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }
        }
    }
}
