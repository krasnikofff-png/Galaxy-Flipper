using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Снимок экрана собранной игры для проверки графики без человека:
    /// запуск с ключом -autoShot путь.png делает снимок через несколько секунд и закрывает игру.
    /// -autoShotDelay секунды — сколько ждать (по умолчанию 6: планеты карты запекаются в фоне).
    /// -autoShotReveal — полностью разведать все планеты сектора (виден облик каждого типа, руины), выбрать свою планету,
    /// устроить на ней аварию и запустить зонд — чтобы на снимке карты было всё.
    /// -autoShotDrag dx — перед снимком «потянуть мышью» окно планеты на dx пикселей (облёт базы).
    /// -autoShotTab n — открыть вкладку правой панели номер n (0 — «Планета», 1 — «Биржа», …).
    /// -autoShotClicks "x,y;x,y" — перед снимком кликнуть мышью в эти точки интерфейса (экран 1600×900), по одной в полсекунды.
    /// -autoShotWheel "x,y,шаги" — покрутить колесо мыши в точке (меньше нуля — к себе, приблизить),
    /// -autoShotPan "x,y,dx,dy" — потянуть мышью из точки на dx, dy.
    /// -autoShotBuy — купить свободную планету без шахты, выбрать её и выписать в журнал кнопки правой панели.
    /// -autoShotSelectFree — выбрать свободную планету, которую ещё не разведали (видно кнопку «Разведать зондом»).
    /// -autoShotProbe — включить режим зонда. -autoShotRivals — нажать «Конец хода» перед снимком (пауза «ходят соперники»).
    /// Без ключа ничего не делает.
    /// </summary>
    public sealed class AutoScreenshot : MonoBehaviour
    {
        string _path;
        float _delay = 6;
        bool _reveal;
        float _drag;
        int _tab = -1;
        bool _probe, _rivals, _buy, _selectFree;
        Vector2[] _clicks = new Vector2[0];
        float[] _wheel, _pan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = System.Environment.GetCommandLineArgs();
            string path = null;
            float delay = 6;
            bool reveal = System.Array.IndexOf(args, "-autoShotReveal") >= 0;
            float drag = 0;
            int tab = -1;
            string clicks = null;
            float[] wheel = null, pan = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-autoShot") path = args[i + 1];
                if (args[i] == "-autoShotDrag") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out drag);
                if (args[i] == "-autoShotClicks") clicks = args[i + 1];
                if (args[i] == "-autoShotWheel") wheel = Numbers(args[i + 1]);
                if (args[i] == "-autoShotPan") pan = Numbers(args[i + 1]);
                if (args[i] == "-autoShotTab") int.TryParse(args[i + 1], out tab);
                if (args[i] == "-autoShotDelay") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out delay);
            }
            if (path == null) return;
            var go = new GameObject("Автоснимок");
            DontDestroyOnLoad(go);
            var shot = go.AddComponent<AutoScreenshot>();
            shot._path = path;
            shot._delay = delay;
            shot._reveal = reveal;
            shot._drag = drag;
            shot._tab = tab;
            shot._wheel = wheel;
            shot._pan = pan;
            if (clicks != null)
                shot._clicks = clicks.Split(';').Select(c => c.Split(','))
                    .Select(c => new Vector2(float.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture)))
                    .ToArray();
            shot._probe = System.Array.IndexOf(args, "-autoShotProbe") >= 0;
            shot._rivals = System.Array.IndexOf(args, "-autoShotRivals") >= 0;
            shot._buy = System.Array.IndexOf(args, "-autoShotBuy") >= 0;
            shot._selectFree = System.Array.IndexOf(args, "-autoShotSelectFree") >= 0;
        }

        IEnumerator Start()
        {
            yield return null;
            var app = FindAnyObjectByType<PlanetTraderApp>();
            if (_reveal && app != null)
            {
                foreach (var p in app.CurrentGame.AllPlanets)
                    p.Knowledge = Core.Knowledge.Explored;
                foreach (var p in app.CurrentGame.PlayerPlanets)
                {
                    app.Select(p.Id);
                    // Для снимка карты: авария (дым над планетой) и зонд в полёте.
                    p.AccidentTurnsLeft = 2;
                    break;
                }
                var g = app.CurrentGame;
                g.LaunchScan(g.Balance.ProbeTypes[2], 0.85, 0.2);
            }
            if (app != null && _buy)
            {
                var g = app.CurrentGame;
                g.Current.Money += 100000;
                foreach (var target in g.AllPlanets.Where(p => p.Owner == Core.Owner.Registry && p.Mine == Core.MineState.None && !p.Inhabited && !p.IsDepleted).ToList())
                {
                    bool won = false;
                    // По правилам покупают только разведанное — для снимка «разведываем» сразу.
                    target.Knowledge = Core.Knowledge.Explored;
                    var r = target.SoldAtAuction ? g.PlaceBid(target.Id, g.Balance.BidFactors.Max(), out won) : g.BuyPlanet(target.Id);
                    Debug.Log($"Автопокупка #{target.Id}: {(r.Ok ? (target.SoldAtAuction && !won ? "ставка проиграна" : "куплена") : r.Error)}, шахта {target.Mine}, владелец {target.Owner}");
                    if (target.Owner != Core.Owner.Player) continue;
                    app.Select(target.Id);
                    break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
                var side = FindAnyObjectByType<UIDocument>().rootVisualElement.Q<ScrollView>("sideScroll");
                foreach (var b in side.Query<Button>().ToList())
                    Debug.Log($"Кнопка панели: «{b.text}» {(b.enabledSelf ? "" : "(неактивна)")} {(b.resolvedStyle.display == DisplayStyle.None ? "(скрыта)" : "")}");
            }
            if (app != null && _selectFree)
            {
                var free = app.CurrentGame.PlanetsForSale.FirstOrDefault(p => p.Knowledge == Core.Knowledge.Scanned && !p.Inhabited);
                if (free != null) app.Select(free.Id);
            }
            if (app != null && _tab >= 0) app.ShowTab(_tab);
            if (app != null && _probe) app.SetProbeMode(true);
            if (_drag != 0 && app != null)
            {
                yield return new WaitForSecondsRealtime(_delay * 0.6f);
                var portrait = app.GetComponent<Visuals.PlanetPortrait>();
                for (int i = 0; i < 20; i++)
                {
                    portrait.Drag(new Vector2(_drag / 20, -1));
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(_delay * 0.4f);
            }
            else
                yield return new WaitForSecondsRealtime(_delay);
            foreach (var c in _clicks)
            {
                Click(c);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (_wheel != null) Wheel(new Vector2(_wheel[0], _wheel[1]), _wheel[2]);
            if (_pan != null)
            {
                var from = new Vector2(_pan[0], _pan[1]);
                var by = new Vector2(_pan[2], _pan[3]);
                Send(PointerAt(EventType.MouseDown, from, Vector2.zero), from);
                for (int i = 1; i <= 10; i++)
                {
                    Send(PointerAt(EventType.MouseDrag, from + by * (i / 10f), by / 10), from);
                    yield return null;
                }
                Send(PointerAt(EventType.MouseUp, from + by, Vector2.zero), from);
            }
            yield return new WaitForSecondsRealtime(0.3f);
            if (app != null && _rivals)
            {
                app.StartRivalsTurn();
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(_path);
            yield return null;
            yield return null;
            Debug.Log($"Автоснимок: {_path}");
            Application.Quit();
        }
    
        static float[] Numbers(string s) =>
            s.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        static EventBase PointerAt(EventType type, Vector2 at, Vector2 delta)
        {
            var e = new Event { type = type, mousePosition = at, delta = delta, button = 0, clickCount = 1 };
            return type == EventType.MouseDown ? PointerDownEvent.GetPooled(e)
                : type == EventType.MouseUp ? (EventBase)PointerUpEvent.GetPooled(e) : PointerMoveEvent.GetPooled(e);
        }

        /// <summary>Отправить событие элементу под точкой pickAt (или тому, кто захватил мышь).</summary>
        static void Send(EventBase e, Vector2 pickAt)
        {
            var panel = FindAnyObjectByType<UIDocument>()?.rootVisualElement?.panel;
            var target = panel?.GetCapturingElement(PointerId.mousePointerId) as VisualElement ?? panel?.Pick(pickAt);
            if (target == null) return;
            using (e)
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        static void Wheel(Vector2 at, float steps)
        {
            var e = new Event { type = EventType.ScrollWheel, mousePosition = at, delta = new Vector2(0, steps) };
            Send(WheelEvent.GetPooled(e), at);
        }

        /// <summary>Клик мышью в точке интерфейса: нажатие и отпускание идут через UI Toolkit, как от настоящей мыши.</summary>
        static void Click(Vector2 at)
        {
            var document = FindAnyObjectByType<UIDocument>();
            var panel = document?.rootVisualElement?.panel;
            if (panel == null) return;
            var target = panel.Pick(at);
            Debug.Log($"Автоклик {at}: {(target == null ? "мимо" : target.name + " " + string.Join(" ", target.GetClasses()))}");
            if (target == null) return;
            foreach (var type in new[] { EventType.MouseDown, EventType.MouseUp })
            {
                var e = new Event { type = type, mousePosition = at, button = 0, clickCount = 1 };
                using (var pointer = type == EventType.MouseDown ? (EventBase)PointerDownEvent.GetPooled(e) : PointerUpEvent.GetPooled(e))
                {
                    pointer.target = target;
                    target.SendEvent(pointer);
                }
            }
        }
    }
}
