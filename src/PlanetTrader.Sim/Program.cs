using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PlanetTrader.Core;

namespace PlanetTrader.Sim
{
    /// <summary>Итоги одной партии.</summary>
    sealed class RunResult
    {
        public double StartCapital;
        public double[] Capital;   // по ходам, индекс = номер хода
        public int[] Planets;
        public int SecondPlanetTurn = -1;
        public int FirstActionsLimitTurn = -1;
        public int TurnsActionsExhausted;
        public int Seizures;
        public bool TookCredit;
        /// <summary>Сколько ходов-планет игрок владел обитаемыми и сколько из них они бастовали.</summary>
        public int InhabitedTurns, StrikeTurns;
        public double InhabitedInvested, InhabitedReturned;
        public int AuctionsWon, AuctionsLost, PlanetsLostToBribes;
        public int Techs, Extractors, RegeneratingTurns, PlanetTurns;
        public readonly Dictionary<string, (double invested, double returned)> ByType = new Dictionary<string, (double, double)>();
    }

    static class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly int[] Checkpoints = { 30, 60, 90, 120 };

        static int Main(string[] args)
        {
            int runs = 60, turns = 120;
            ulong seed = 1;
            string outDir = "docs/sim";
            bool brakes = false;
            string trace = null;
            var overrides = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--runs": runs = int.Parse(args[++i]); break;
                    case "--turns": turns = int.Parse(args[++i]); break;
                    case "--seed": seed = ulong.Parse(args[++i]); break;
                    case "--out": outDir = args[++i]; break;
                    case "--set": overrides.Add(args[++i]); break;
                    case "--brakes": brakes = true; break;
                    case "--trace": trace = args[++i]; break;
                    default:
                        Console.Error.WriteLine("Использование: sim [--runs 60] [--turns 120] [--seed 1] [--out docs/sim] [--set Param=value]... [--brakes]");
                        return 1;
                }
            }

            turns = Math.Max(turns, Checkpoints.Max());
            var balance = MakeBalance(overrides);

            if (trace != null)
            {
                Trace(balance, Strategy.All.First(s => s.Name == trace), turns, seed);
                return 0;
            }

            Directory.CreateDirectory(outDir);

            var results = Strategy.All.ToDictionary(s => s, s => RunMany(balance, s, runs, turns, seed));
            var noScouting = RunMany(balance, Strategy.ActiveNoScouting, runs, turns, seed);
            var ignoresPeople = RunMany(balance, Strategy.ActiveIgnoresPeople, runs, turns, seed);
            var calm = MakeBalance(overrides);
            foreach (var m in calm.Magnates) m.TrickChance = 0;
            var noTricks = RunMany(calm, Strategy.Active, runs, turns, seed);
            var report = new StringBuilder();
            WriteReport(report, results, noScouting, ignoresPeople, noTricks, runs, overrides);
            WriteCurvesCsv(Path.Combine(outDir, "curves.csv"), results, turns);

            if (brakes)
            {
                report.AppendLine();
                WriteBrakes(report, overrides, runs, turns, seed);
            }

            File.WriteAllText(Path.Combine(outDir, "report.md"), report.ToString());
            Console.Write(report);
            return 0;
        }

        static Balance MakeBalance(IEnumerable<string> overrides)
        {
            var b = new Balance();
            foreach (var o in overrides) Apply(b, o);
            return b;
        }

        /// <summary>
        /// --set AiBuyChance=0.2 меняет любое числовое поле Balance.
        /// Поля элементов массивов тоже: --set Magnates[0].AuctionInterest=0.6, --set ProbeTypes[1].Cost=300.
        /// </summary>
        static void Apply(Balance b, string assignment)
        {
            var parts = assignment.Split('=');
            if (parts.Length != 2) throw new ArgumentException("Ожидается Param=value: " + assignment);
            object target = b;
            string name = parts[0];
            int dot = name.IndexOf("].", StringComparison.Ordinal);
            if (dot > 0)
            {
                int open = name.IndexOf('[');
                var arrayField = typeof(Balance).GetField(name.Substring(0, open), BindingFlags.Public | BindingFlags.Instance)
                                 ?? throw new ArgumentException("Нет параметра " + name);
                int index = int.Parse(name.Substring(open + 1, dot - open - 1), Inv);
                target = ((Array)arrayField.GetValue(b)).GetValue(index);
                name = name.Substring(dot + 2);
            }
            var f = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new ArgumentException("Нет параметра " + parts[0]);
            object value = f.FieldType == typeof(int) ? int.Parse(parts[1], Inv)
                : f.FieldType == typeof(bool) ? bool.Parse(parts[1])
                : (object)double.Parse(parts[1], Inv);
            f.SetValue(target, value);
        }

        static List<RunResult> RunMany(Balance b, Strategy s, int runs, int turns, ulong seed)
        {
            var list = new List<RunResult>();
            for (int i = 0; i < runs; i++) list.Add(RunOne(b, s, turns, seed + (ulong)i * 7919));
            return list;
        }

        /// <summary>--trace "Активный": одна партия, состояние каждые 5 ходов.</summary>
        static void Trace(Balance b, Strategy s, int turns, ulong seed)
        {
            var g = Game.New(b, seed);
            var bot = new Bot(s, seed);
            Console.WriteLine("ход | капитал | деньги | долг | планет (выраб.) | стоим. планет | склад | расходы/ход | менеджеры");
            for (int t = 1; t <= turns; t++)
            {
                bot.PlayTurn(g);
                g.EndTurn();
                if (t % 5 != 0) continue;
                int depleted = g.PlayerPlanets.Count(p => p.IsDepleted);
                Console.WriteLine($"{t} | {N(g.Capital)} | {N(g.Money)} | {N(g.Debt)} | {g.PlayerPlanetCount} ({depleted}) | {N(g.PlanetsValue)} | {N(g.StockValue)} | {N(g.CostsPerTurn)} | {g.Managers}");
            }
        }

        static RunResult RunOne(Balance b, Strategy s, int turns, ulong seed)
        {
            var g = Game.New(b, seed);
            var bot = new Bot(s, seed);
            var r = new RunResult
            {
                StartCapital = g.Capital,
                Capital = new double[turns + 1],
                Planets = new int[turns + 1],
            };
            r.Capital[0] = g.Capital;
            r.Planets[0] = g.PlayerPlanetCount;
            double startDebt = g.Debt;

            for (int t = 1; t <= turns; t++)
            {
                int exhaustedBefore = g.TurnsActionsExhausted;
                bot.PlayTurn(g);
                if (g.Debt > startDebt + 1) r.TookCredit = true;
                if (g.TurnsActionsExhausted > exhaustedBefore && r.FirstActionsLimitTurn < 0) r.FirstActionsLimitTurn = t;
                g.EndTurn();
                r.Capital[t] = g.Capital;
                r.Planets[t] = g.PlayerPlanetCount;
                if (r.SecondPlanetTurn < 0 && g.PlayerPlanetCount >= 2) r.SecondPlanetTurn = t;
                foreach (var p in g.PlayerPlanets.Where(p => p.Inhabited))
                {
                    r.InhabitedTurns++;
                    if (p.OnStrike) r.StrikeTurns++;
                }
                foreach (var p in g.PlayerPlanets)
                {
                    r.PlanetTurns++;
                    if (p.IsRegenerating) r.RegeneratingTurns++;
                }
            }

            r.TurnsActionsExhausted = g.TurnsActionsExhausted;
            r.AuctionsWon = g.AuctionsWon;
            r.AuctionsLost = g.AuctionsLost;
            r.PlanetsLostToBribes = g.PlanetsLostToBribes;
            r.Techs = b.Technologies.Count(t => g.IsResearched(t.Id));
            r.Extractors = g.PlayerPlanets.Sum(p => p.ExtraDeposits.Count(d => d.HasExtractor));
            r.Seizures = g.TotalSeizures;

            // Доход на вложенное по типам: всё, что планета принесла, + её текущая стоимость.
            foreach (var p in g.AllPlanets.Where(p => p.Invested > 0))
            {
                double returned = p.Earned + (p.Owner == Owner.Player ? p.MarketValue(b) : 0);
                if (p.Inhabited)
                {
                    r.InhabitedInvested += p.Invested;
                    r.InhabitedReturned += returned;
                }
                r.ByType.TryGetValue(p.Type.Name, out var acc);
                r.ByType[p.Type.Name] = (acc.invested + p.Invested, acc.returned + returned);
            }
            return r;
        }

        // ------------------------------------------------------------------
        // Статистика
        // ------------------------------------------------------------------

        static double Percentile(IEnumerable<double> values, double q)
        {
            var v = values.OrderBy(x => x).ToArray();
            if (v.Length == 0) return double.NaN;
            double pos = (v.Length - 1) * q;
            int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
            return v[lo] + (v[hi] - v[lo]) * (pos - lo);
        }

        static double Median(IEnumerable<double> values) => Percentile(values, 0.5);

        static string N(double x) => double.IsNaN(x) ? "—" : x.ToString("#,0", Inv).Replace(",", " ");
        static string F(double x, string fmt = "0.0") => double.IsNaN(x) ? "—" : x.ToString(fmt, Inv);

        static string Check(bool ok) => ok ? "✅" : "❌";

        static void WriteReport(StringBuilder sb, Dictionary<Strategy, List<RunResult>> results, List<RunResult> noScouting,
            List<RunResult> ignoresPeople, List<RunResult> noTricks, int runs, List<string> overrides)
        {
            sb.AppendLine("# Отчёт симулятора экономики");
            sb.AppendLine();
            sb.AppendLine($"Медиана по {runs} партиям на каждый стиль игры. Капитал = деньги − долг + рыночная стоимость планет + склад.");
            if (overrides.Count > 0) sb.AppendLine($"Изменённые параметры: `{string.Join("`, `", overrides)}`.");
            sb.AppendLine();

            sb.AppendLine("## Капитал");
            sb.AppendLine();
            sb.AppendLine("| Стиль | Старт | t30 | t60 | t90 | t120 | Худшие 10% t120 | Планет t120 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var kv in results)
            {
                var rs = kv.Value;
                sb.Append($"| {kv.Key.Name} | {N(Median(rs.Select(r => r.StartCapital)))}");
                foreach (var t in Checkpoints) sb.Append($" | {N(Median(rs.Select(r => r.Capital[t])))}");
                sb.AppendLine($" | {N(Percentile(rs.Select(r => r.Capital[120]), 0.1))} | {F(Median(rs.Select(r => (double)r.Planets[120])), "0")} |");
            }
            sb.AppendLine();

            // Проверка целей из вкладки «План проверки».
            var passive = results[Strategy.Passive];
            var active = results[Strategy.Active];
            var credit = results[Strategy.ActiveCredit];

            double secondPlanet = Median(active.Where(r => r.SecondPlanetTurn > 0).Select(r => (double)r.SecondPlanetTurn));
            double growth30 = Median(active.Select(r => r.Capital[30] / r.StartCapital));
            double lead90 = Median(active.Select(r => r.Capital[90])) / Median(passive.Select(r => r.Capital[90]));
            double lead120 = Median(active.Select(r => r.Capital[120])) / Median(passive.Select(r => r.Capital[120]));
            double g30to60 = Median(active.Select(r => r.Capital[60] - r.Capital[30]));
            double g90to120 = Median(active.Select(r => r.Capital[120] - r.Capital[90]));
            double g60to90 = Median(active.Select(r => r.Capital[90] - r.Capital[60]));
            var borrowers = credit.Where(r => r.TookCredit).ToList();
            double seizedShare = borrowers.Count == 0 ? double.NaN : borrowers.Count(r => r.Seizures > 0) / (double)borrowers.Count;
            double creditWinShare = borrowers.Count == 0 ? double.NaN
                : borrowers.Count(r => r.Capital[120] > Median(active.Select(a => a.Capital[120]))) / (double)borrowers.Count;
            double spread30 = Percentile(active.Select(r => r.Capital[30]), 0.9) / Percentile(active.Select(r => r.Capital[30]), 0.1) - 1;
            var limitTurns = active.Where(r => r.FirstActionsLimitTurn > 0).Select(r => (double)r.FirstActionsLimitTurn).ToList();
            double firstLimit = limitTurns.Count > 0 ? Median(limitTurns) : double.NaN;

            sb.AppendLine("## Цели из «Плана проверки» (стиль «Активный», если не указано иное)");
            sb.AppendLine();
            sb.AppendLine("| Что проверяем | Цель | Результат | |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine($"| Время до второй планеты | 8–12 ходов | {F(secondPlanet, "0")} | {Check(secondPlanet >= 8 && secondPlanet <= 12)} |");
            sb.AppendLine($"| Капитал к 30-му ходу | ×3–4 от старта | ×{F(growth30)} | {Check(growth30 >= 3 && growth30 <= 4)} |");
            sb.AppendLine($"| Развитие окупается: «Активный» (технологии, экстракторы) обгоняет «Пассивного» к 90 | на 20%+ | ×{F(lead90, "0.00")} | {Check(lead90 >= 1.2)} |");
            sb.AppendLine($"| То же к 120 | в 1,5–2 раза | ×{F(lead120, "0.00")} | {Check(lead120 >= 1.5 && lead120 <= 2)} |");
            sb.AppendLine($"| Рост замедляется после 60-го хода | прирост 90→120 < 60→90 | {N(g30to60)} → {N(g60to90)} → {N(g90to120)} | {Check(g90to120 < g60to90)} |");
            sb.AppendLine($"| то же, строже | прирост 90→120 < 30→60 | | {Check(g90to120 < g30to60)} |");
            sb.AppendLine($"| Кредит — осознанный риск («Активный + кредит») | есть и выигрыши, и потери планет | обогнали активного: {F(creditWinShare * 100, "0")}%, коллекторы: {F(seizedShare * 100, "0")}% | {Check(creditWinShare > 0 && seizedShare > 0)} |");
            sb.AppendLine($"| Решения важны уже в первой сессии | разброс капитала t30 ≥30% | {F(spread30 * 100, "0")}% (p90/p10) | {Check(spread30 >= 0.3)} |");
            sb.AppendLine($"| Менеджеры нужны | упор в лимит действий к 40-му ходу | первый раз на ходу {F(firstLimit, "0")} | {Check(firstLimit <= 40)} |");

            // Квантий: доход на вложенное по типам планет.
            var byType = new Dictionary<string, (double inv, double ret)>();
            foreach (var r in active)
            foreach (var kv in r.ByType)
            {
                byType.TryGetValue(kv.Key, out var acc);
                byType[kv.Key] = (acc.inv + kv.Value.invested, acc.ret + kv.Value.returned);
            }
            double Roi(string name) => byType.TryGetValue(name, out var v) && v.inv > 0 ? v.ret / v.inv : double.NaN;
            double iceRoi = Roi("Ледяной карлик"), anomalyRoi = Roi("Аномалия");
            double scout60 = Median(active.Select(r => r.Capital[60])) / Median(noScouting.Select(r => r.Capital[60]));
            double scout120 = Median(active.Select(r => r.Capital[120])) / Median(noScouting.Select(r => r.Capital[120]));
            sb.AppendLine($"| Разведка окупается (этап 3) | «Активный» с разведкой обгоняет того же без неё на 10%+ к 120 | ×{F(scout60, "0.00")} к 60, ×{F(scout120, "0.00")} к 120 | {Check(scout120 >= 1.1)} |");
            double StrikeShare(List<RunResult> rs) => rs.Sum(r => r.StrikeTurns) / (double)Math.Max(1, rs.Sum(r => r.InhabitedTurns));
            double InhabitedRoi(List<RunResult> rs) => rs.Sum(r => r.InhabitedReturned) / Math.Max(1, rs.Sum(r => r.InhabitedInvested));
            double strikeCare = StrikeShare(active), strikeIgnore = StrikeShare(ignoresPeople);
            double roiCare = InhabitedRoi(active), roiIgnore = InhabitedRoi(ignoresPeople);
            double inhabitedShare = active.Sum(r => r.InhabitedTurns) / (double)Math.Max(1, active.Sum(r => r.Planets.Skip(1).Sum()));
            double lostIgnoreAvg = ignoresPeople.Average(r => r.PlanetsLostToBribes);
            sb.AppendLine($"| Жители требуют внимания (этап 4) | у заботливого забастовок ≤10%; кто жителей не замечает, бастует ≥20% времени или теряет планеты из-за подкупа | " +
                          $"{F(strikeCare * 100, "0")}% против {F(strikeIgnore * 100, "0")}% и {F(lostIgnoreAvg, "0.0")} планеты | {Check(strikeCare <= 0.1 && (strikeIgnore >= 0.2 || lostIgnoreAvg >= 1))} |");
            sb.AppendLine($"| Забота окупается (этап 4) | доход на вложенное в обитаемые планеты выше на 20%+ | ×{F(roiCare, "0.00")} против ×{F(roiIgnore, "0.00")} | {Check(roiCare >= roiIgnore * 1.2)} |");
            sb.AppendLine($"| Обитаемых среди планет «Активного» | для справки | {F(inhabitedShare * 100, "0")}% | |");
            double trickCost = Median(active.Select(r => r.Capital[120])) / Median(noTricks.Select(r => r.Capital[120]));
            double winRate = active.Sum(r => r.AuctionsWon) / (double)Math.Max(1, active.Sum(r => r.AuctionsWon + r.AuctionsLost));
            double lostCare = active.Average(r => r.PlanetsLostToBribes), lostIgnore = ignoresPeople.Average(r => r.PlanetsLostToBribes);
            sb.AppendLine($"| ИИ мешает заметно, но не бесит (этап 5) | пакости магнатов снижают капитал к 120 на 8–25% | капитал ×{F(trickCost, "0.00")} от партий без пакостей | {Check(trickCost >= 0.75 && trickCost <= 0.92)} |");
            sb.AppendLine($"| Аукционы (этап 5) | бот выигрывает 40–80% своих ставок | {F(winRate * 100, "0")}% | {Check(winRate >= 0.4 && winRate <= 0.8)} |");
            sb.AppendLine($"| Подкуп жителей (этап 5) | заботливый теряет ≤1 планеты за партию, тот, кто жителей не замечает, — больше | {F(lostCare, "0.0")} против {F(lostIgnore, "0.0")} | {Check(lostCare <= 1 && lostIgnore > lostCare)} |");
            double regenShare = active.Sum(r => r.RegeneratingTurns) / (double)Math.Max(1, active.Sum(r => r.PlanetTurns));
            sb.AppendLine($"| Месторождения восстанавливаются (этап 7) | для справки: доля планет-ходов, когда основное месторождение восстанавливается | {F(regenShare * 100, "0")}% | |");
            sb.AppendLine($"| Развитие (этап 7) | для справки: технологий и экстракторов у «Активного» к 120 | {F(Median(active.Select(r => (double)r.Techs)), "0")} технологий, {F(Median(active.Select(r => (double)r.Extractors)), "0")} экстракторов | |");
            sb.AppendLine($"| Квантий не ломает экономику | Аномалия не сильно выгоднее Льда | Аномалия ×{F(anomalyRoi, "0.00")}, Лёд ×{F(iceRoi, "0.00")} | {Check(anomalyRoi <= iceRoi * 1.3)} |");
            sb.AppendLine();

            sb.AppendLine("## Доход на вложенное по типам планет («Активный»)");
            sb.AppendLine();
            sb.AppendLine("| Тип | Вложено (сумма) | Вернулось | × |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var kv in byType.OrderBy(k => k.Key))
                sb.AppendLine($"| {kv.Key} | {N(kv.Value.inv)} | {N(kv.Value.ret)} | ×{F(kv.Value.ret / kv.Value.inv, "0.00")} |");
        }

        static void WriteCurvesCsv(string path, Dictionary<Strategy, List<RunResult>> results, int turns)
        {
            var sb = new StringBuilder();
            sb.Append("turn");
            foreach (var s in results.Keys) sb.Append($",{s.Name}: капитал,{s.Name}: худшие 10%,{s.Name}: планет");
            sb.AppendLine();
            for (int t = 0; t <= turns; t++)
            {
                sb.Append(t);
                foreach (var rs in results.Values)
                {
                    sb.Append(',').Append(F(Median(rs.Select(r => r.Capital[t])), "0"));
                    sb.Append(',').Append(F(Percentile(rs.Select(r => r.Capital[t]), 0.1), "0"));
                    sb.Append(',').Append(F(Median(rs.Select(r => (double)r.Planets[t])), "0.#"));
                }
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>Как во вкладке «Тормоза»: отключаем по одному тормозу и смотрим, насколько вырастет капитал.</summary>
        static void WriteBrakes(StringBuilder sb, List<string> overrides, int runs, int turns, ulong seed)
        {
            var brakes = new (string name, Action<Balance> off)[]
            {
                ("База", b => { }),
                ("Без стартового долга", b => b.StartDebt = 0),
                ("Без роста содержания с числом планет", b => b.UpkeepCurveK = 1),
                ("Без влияния продаж на рынок", b => b.SalesImpact = 0),
                ("Без ИИ-конкурентов", b => { b.AiBuyChance = 0; b.AiStartShare = 0; b.AuctionMarkup = 0; }),
                ("Без зарплат", b => b.ManagerSalary = 0),
                ("Без процентов", b => b.InterestPerTurn = 0),
                ("Без аварий", b => b.AccidentChance = 0),
            };

            sb.AppendLine("## Тормоза");
            sb.AppendLine();
            sb.AppendLine("Каждая строка отключает один тормоз. Чем больше последний столбец, тем сильнее этот тормоз сдерживает рост.");
            sb.AppendLine();
            sb.AppendLine("| Что отключили | Пассивный t120 | Активный t30 | Активный t120 | Активный + кредит t120 | Активный t120 к базе |");
            sb.AppendLine("|---|---|---|---|---|---|");
            double baseActive = double.NaN;
            foreach (var (name, off) in brakes)
            {
                var b = MakeBalance(overrides);
                off(b);
                var p = RunMany(b, Strategy.Passive, runs, turns, seed);
                var a = RunMany(b, Strategy.Active, runs, turns, seed);
                var c = RunMany(b, Strategy.ActiveCredit, runs, turns, seed);
                double a120 = Median(a.Select(r => r.Capital[120]));
                if (double.IsNaN(baseActive)) baseActive = a120;
                sb.AppendLine($"| {name} | {N(Median(p.Select(r => r.Capital[120])))} | {N(Median(a.Select(r => r.Capital[30])))} | {N(a120)} | {N(Median(c.Select(r => r.Capital[120])))} | ×{F(a120 / baseActive)} |");
            }
        }
    }
}
