using System;
using System.Collections.Generic;
using System.Linq;

namespace PlanetTrader.Core
{
    /// <summary>
    /// Шоу «Риелтор года» (цель партии), случаи с выбором и «Лот недели». Свой генератор случайных чисел —
    /// остальные генераторы партии от этого не сдвигаются.
    /// </summary>
    public sealed partial class Game
    {
        ShowSeason _season;
        Rng _showRng;
        LotAuction _lot;
        int _nextLotTurn;
        string _lastLegend;

        Rng ShowRng => _showRng ??= new Rng(Seed ^ 0x5_4077UL);

        Corporation CorpById(int id) => _corps.FirstOrDefault(c => c.Id == id);

        /// <summary>Число для текстов: 12 500 — с пробелом между тысячами при любом языке системы.</summary>
        static string Num(double x) => Math.Round(x).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

        /// <summary>Множитель денег в призах и случаях: с уровнем корпорации суммы растут.</summary>
        public double LevelScale(int level) =>
            Balance.ShowLevelScale.Length == 0 ? 1 : Balance.ShowLevelScale[Math.Min(Math.Max(0, level), Balance.ShowLevelScale.Length - 1)];

        T Pick<T>(IList<T> items, Func<T, double> weight)
        {
            double total = items.Sum(weight);
            double roll = ShowRng.NextDouble() * total;
            foreach (var x in items)
            {
                roll -= weight(x);
                if (roll < 0) return x;
            }
            return items[items.Count - 1];
        }

        double CapitalOf(Corporation c)
        {
            var keep = _current;
            _current = c;
            double capital = Capital;
            _current = keep;
            return capital;
        }

        /// <summary>Важное сразу для нескольких игроков: строка в журнал один раз, окно — каждому.</summary>
        static void ImportantFor(IEnumerable<TurnReport> reports, AlertKind kind, string title, string text, Planet p = null, Magnate m = null)
        {
            bool logged = false;
            foreach (var r in reports)
            {
                if (!logged) r.Events.Add(text);
                logged = true;
                r.Alerts.Add(new Alert { Kind = kind, Title = title, Text = text, PlanetId = p?.Id ?? -1, MagnateId = m?.Id ?? -1 });
            }
        }

        // ------------------------------------------------------------------
        // Шоу «Риелтор года»
        // ------------------------------------------------------------------

        public bool ShowOn => Balance.SeasonTurns > 0;

        /// <summary>Текущий сезон шоу; у партии из старого сохранения начнётся сам.</summary>
        public ShowSeason Season
        {
            get
            {
                if (_season == null && ShowOn) StartSeason();
                return _season;
            }
        }

        /// <summary>Сколько кубков у текущей корпорации.</summary>
        public int Cups => _current.Cups;

        void StartSeason()
        {
            var b = Balance;
            var s = new ShowSeason { Number = (_season?.Number ?? 0) + 1, StartTurn = Turn, EndTurn = Turn + Math.Max(1, b.SeasonTurns) - 1 };
            int level = Players.Select(c => c.Level).DefaultIfEmpty(0).Max();
            var pool = b.ShowTasks.Where(TaskPossible).ToList();
            for (int i = 0; i < b.SeasonTasks && pool.Count > 0; i++)
            {
                var spec = Pick(pool, t => t.Weight);
                pool.Remove(spec);
                double target = Math.Max(1, Math.Round(spec.Target + spec.TargetPerLevel * level));
                if (spec.Kind == ShowTaskKind.SellOnExchange) target = Math.Round(target / 100) * 100;
                s.Tasks.Add(new ShowTask { Spec = spec, Target = target });
            }
            foreach (var c in Players.Where(c => !c.Bankrupt))
                s.Entries.Add(new ShowEntry { CorpId = c.Id, Name = c.Name, StartCapital = CapitalOf(c), Start = c.Stats.Copy() });
            foreach (var m in ShowRivals())
                s.Entries.Add(new ShowEntry { MagnateId = m.Id, Name = m.Name, StartCapital = MagnateWealth(m), Start = m.Corp?.Stats.Copy() ?? new ShowStats() });
            _season = s;
        }

        bool TaskPossible(ShowTaskSpec t)
        {
            if (t.Kind == ShowTaskKind.WinLot) return Balance.LotGapMax > 0;
            if (t.Kind == ShowTaskKind.Research) return Players.Any(c => Balance.Technologies.Any(x => !c.Researched.Contains(x.Id)));
            return true;
        }

        /// <summary>Соперники в шоу: самые богатые магнаты секторов, где у игроков есть лицензия.</summary>
        List<Magnate> ShowRivals()
        {
            var licensed = new HashSet<int>(Players.SelectMany(c => c.Licenses));
            return _magnates.Where(m => (m.Corp == null || !m.Corp.Bankrupt) && licensed.Contains(m.Sector))
                .OrderByDescending(MagnateWealth).ThenBy(m => m.Id).Take(Balance.ShowRivals).ToList();
        }

        /// <summary>Текст задания с целью: «Купить 2 планеты».</summary>
        public static string TaskText(ShowTask t)
        {
            int n = (int)Math.Round(t.Target);
            string target = t.Spec.Noun != null && t.Spec.Noun.Length == 3 ? Words.Count(n, t.Spec.Noun[0], t.Spec.Noun[1], t.Spec.Noun[2]) : Num(n);
            return string.Format(t.Spec.Text, target);
        }

        /// <summary>Сколько сделано по заданию с начала сезона (у магнатов честно считаются только рост капитала и лоты).</summary>
        public double Progress(ShowEntry e, ShowTask t)
        {
            var season = Season;
            if (season == null) return 0;
            if (e.Done.Contains(season.Tasks.IndexOf(t))) return t.Target;
            var corp = e.IsPlayer ? CorpById(e.CorpId) : GetMagnate(e.MagnateId)?.Corp;
            var now = corp?.Stats ?? new ShowStats();
            var start = e.Start ?? new ShowStats();
            switch (t.Spec.Kind)
            {
                case ShowTaskKind.GrowCapital:
                    var magnate = e.IsPlayer ? null : GetMagnate(e.MagnateId);
                    if (e.IsPlayer ? corp == null : magnate == null) return 0;
                    double capital = e.IsPlayer ? CapitalOf(corp) : MagnateWealth(magnate);
                    return e.StartCapital <= 0 ? 0 : Math.Max(0, (capital / e.StartCapital - 1) * 100);
                case ShowTaskKind.WinLot:
                    return now.LotsWon - start.LotsWon;
            }
            if (!e.IsPlayer) return 0;
            switch (t.Spec.Kind)
            {
                case ShowTaskKind.BuyPlanets: return now.PlanetsBought - start.PlanetsBought;
                case ShowTaskKind.FlipPlanet: return now.ProfitableSales - start.ProfitableSales;
                case ShowTaskKind.SettleColony: return now.Colonies - start.Colonies;
                case ShowTaskKind.Educate: return now.EducationLevels - start.EducationLevels;
                case ShowTaskKind.FindHidden: return now.HiddenFound - start.HiddenFound;
                case ShowTaskKind.ExplorePlanets: return now.PlanetsExplored - start.PlanetsExplored;
                case ShowTaskKind.FulfillOrders: return now.OrdersFulfilled - start.OrdersFulfilled;
                case ShowTaskKind.Research: return now.ResearchDone - start.ResearchDone;
                case ShowTaskKind.Festivals: return now.Festivals - start.Festivals;
                case ShowTaskKind.SellOnExchange: return now.SoldOnExchange - start.SoldOnExchange;
            }
            return 0;
        }

        /// <summary>Места в сезоне: больше очков — выше; при равенстве игрок впереди.</summary>
        public List<ShowEntry> Ranking(ShowSeason s = null) =>
            (s ?? Season)?.Entries.OrderByDescending(e => e.Points).ThenByDescending(e => e.IsPlayer).ThenBy(e => e.Name).ToList()
            ?? new List<ShowEntry>();

        /// <summary>Место текущей корпорации в сезоне, с единицы; 0 — она не участвует.</summary>
        public int ShowPlace => Ranking().FindIndex(e => e.CorpId == _current.Id) + 1;

        /// <summary>Конец хода: кто что выполнил; в последний ход сезона — итоги и призы.</summary>
        void UpdateShow(Dictionary<int, TurnReport> reports, TurnReport shared)
        {
            if (!ShowOn) return;
            var s = Season;
            for (int i = 0; i < s.Tasks.Count; i++)
            {
                var t = s.Tasks[i];
                foreach (var e in s.Entries)
                {
                    // Разорившийся магнат ушёл из сектора — его очки замирают.
                    if (e.Done.Contains(i) || (!e.IsPlayer && GetMagnate(e.MagnateId) == null)) continue;
                    bool honest = e.IsPlayer || t.Spec.Kind == ShowTaskKind.GrowCapital || t.Spec.Kind == ShowTaskKind.WinLot;
                    bool done = honest ? Progress(e, t) >= t.Target - 1e-9 : ShowRng.Chance(Balance.MagnateTaskChance);
                    if (!done) continue;
                    e.Done.Add(i);
                    e.Points += t.Spec.Points;
                    if (e.IsPlayer && reports.TryGetValue(e.CorpId, out var r))
                        Important(r, AlertKind.Show, $"Задание выполнено: {t.Spec.Title}",
                            $"Шоу «Риелтор года»: «{TaskText(t)}» — готово, +{t.Spec.Points} очков.");
                    else if (!e.IsPlayer)
                        shared.Events.Add($"Шоу «Риелтор года»: {e.Name} выполняет «{t.Spec.Title}» (+{t.Spec.Points}).");
                }
            }
            if (Turn >= s.EndTurn) FinishSeason(reports, shared);
        }

        void FinishSeason(Dictionary<int, TurnReport> reports, TurnReport shared)
        {
            var b = Balance;
            var s = _season;
            var ranking = Ranking(s);
            if (ranking.Count == 0) return;
            string table = string.Join(", ", ranking.Select((e, i) => $"{i + 1}. {e.Name} — {Math.Round(e.Points)}"));
            for (int place = 0; place < ranking.Count; place++)
            {
                var e = ranking[place];
                double prize = place < b.ShowPrize.Length ? b.ShowPrize[place] : 0;
                if (!e.IsPlayer)
                {
                    var m = GetMagnate(e.MagnateId);
                    if (m?.Corp != null && !m.Corp.Bankrupt) m.Money += Math.Round(prize * LevelScale(m.Corp.Level));
                    continue;
                }
                var c = CorpById(e.CorpId);
                if (c == null || c.Bankrupt) continue;
                double money = Math.Round(prize * LevelScale(c.Level));
                double rep = place < b.ShowPrizeReputation.Length ? b.ShowPrizeReputation[place] : 0;
                c.Money += money;
                c.Reputation = Math.Min(100, Math.Max(0, c.Reputation + rep));
                c.SeasonPlaces.Add(place + 1);
                if (place == 0) c.Cups++;
                if (!reports.TryGetValue(c.Id, out var r)) continue;
                string prizeText = money > 0 ? $" Приз — {Math.Round(money)}" + (rep > 0 ? $" и +{rep} к репутации." : ".") : "";
                string host = HostLine(place == 0 ? b.ShowHostWin : b.ShowHostLose);
                string title = place == 0 ? $"Кубок сезона {s.Number}!" : $"Сезон {s.Number}: {place + 1}-е место";
                if (place == 0)
                    Important(r, AlertKind.Show, title,
                        $"Брэд Квазар: {host} Вы — риелтор сезона с {Math.Round(e.Points)} очками.{prizeText} Кубков: {c.Cups}. Итоги: {table}.");
                else
                    Important(r, AlertKind.Show, title,
                        $"Брэд Квазар: {host} Кубок {Holder(ranking[0])}.{prizeText} Итоги: {table}.");
                r.Season = new SeasonSummary
                {
                    Number = s.Number, Place = place + 1, Points = e.Points, Prize = money, Reputation = rep, Cups = c.Cups,
                    HostLine = host, AlertTitle = title, Ranking = ranking,
                };
            }
            shared.Events.Add($"Сезон {s.Number} шоу «Риелтор года» окончен, кубок {Holder(ranking[0])}.");
        }

        /// <summary>«у вас» или «у Гуго Жаднер».</summary>
        static string Holder(ShowEntry e) => e.Name == "Вы" ? "у вас" : $"у {e.Name}";

        string HostLine(string[] lines) => lines == null || lines.Length == 0 ? "" : lines[ShowRng.Range(0, lines.Length)];

        /// <summary>Начало хода: новый сезон, если прошлый кончился.</summary>
        void NextSeason(Dictionary<int, TurnReport> reports, List<Corporation> players)
        {
            if (!ShowOn || Season.EndTurn >= Turn) return;
            StartSeason();
            var s = _season;
            string tasks = string.Join("; ", s.Tasks.Select(t => $"«{t.Spec.Title}» — {TaskText(t).ToLowerInvariant()} (+{t.Spec.Points})"));
            ImportantFor(players.Where(c => reports.ContainsKey(c.Id)).Select(c => reports[c.Id]), AlertKind.Show, $"Сезон {s.Number}: новые задания",
                $"Брэд Квазар: {HostLine(Balance.ShowHostStart)} Сезон {s.Number} до хода {s.EndTurn}. Задания: {tasks}.");
        }

        // ------------------------------------------------------------------
        // Случаи
        // ------------------------------------------------------------------

        /// <summary>Случай текущей корпорации, который ждёт ответа; null — нет.</summary>
        public ActiveIncident Incident => _current.Incident;

        public double IncidentCost(ActiveIncident a, IncidentChoice c) => Math.Round(c.Cost * a.Scale);

        /// <summary>Текст случая с именами планеты, магната и ценой.</summary>
        public string IncidentText(ActiveIncident a, string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var p = GetPlanet(a.PlanetId);
            var m = GetMagnate(a.MagnateId);
            return text.Replace("{planet}", p == null ? "планету" : PlanetName(p))
                .Replace("{magnate}", m?.Name ?? "Магнат")
                .Replace("{resource}", p == null ? "ресурс" : p.Resource.RuName().ToLowerInvariant())
                .Replace("{price}", p == null ? "" : Num(IncidentSalePrice(p, a)));
        }

        /// <summary>Как называть планету в текстах: у легендарных — по имени.</summary>
        public static string PlanetName(Planet p) => p.Legend != null ? $"«{p.Legend}» ({p.Type.Name} #{p.Id})" : $"{p.Type.Name} #{p.Id}";

        double IncidentSalePrice(Planet p, ActiveIncident a)
        {
            double premium = a.Def.Choices.Select(c => c.Success.SalePremium).DefaultIfEmpty(0).Max();
            return Math.Round(p.MarketValue(Balance) * Math.Max(1, premium));
        }

        /// <summary>Раз в несколько ходов — новый случай текущей корпорации.</summary>
        void OfferIncident()
        {
            var b = Balance;
            var c = _current;
            if (b.IncidentGapMax <= 0 || b.Incidents.Length == 0 || c.Incident != null || c.Bankrupt) return;
            if (Turn < Math.Max(c.NextIncidentTurn, b.IncidentFirstTurn)) return;
            c.NextIncidentTurn = Turn + ShowRng.Range(b.IncidentGapMin, b.IncidentGapMax + 1);
            var options = b.Incidents.Where(d => d.MinTurn <= Turn && d.Id != c.LastIncidentId).Select(MakeIncident).Where(x => x != null).ToList();
            if (options.Count == 0) return;
            var chosen = Pick(options, x => x.Def.Weight);
            c.Incident = chosen;
            c.LastIncidentId = chosen.Def.Id;
            c.Stats.IncidentsSeen++;
        }

        ActiveIncident MakeIncident(Incident def)
        {
            var a = new ActiveIncident { Def = def, Turn = Turn, Scale = LevelScale(Level) };
            var mine = PlayerPlanets.ToList();
            Planet p = null;
            Magnate m = null;
            switch (def.Target)
            {
                case IncidentTarget.MyPlanet:
                    p = Any(mine);
                    if (p == null) return null;
                    break;
                case IncidentTarget.MyMine:
                    p = Any(mine.Where(x => x.Mine == MineState.Working && !x.IsDepleted).ToList());
                    if (p == null) return null;
                    break;
                case IncidentTarget.MyInhabited:
                    p = Any(mine.Where(x => x.Inhabited).ToList());
                    if (p == null) return null;
                    break;
                case IncidentTarget.Magnate:
                    m = Any(ActiveMagnates.Where(x => (x.Corp == null || !x.Corp.Bankrupt) && HasLicense(x.Sector)
                                                      && (def.Magnate == null || x.Name.Split(' ')[0] == def.Magnate)).ToList());
                    if (m == null) return null;
                    break;
                case IncidentTarget.MyPlanetAndMagnate:
                    // Последнюю планету магнат не выпрашивает: без планет партия кончится.
                    if (mine.Count < 2) return null;
                    p = Any(mine.Where(x => !x.IsDepleted).ToList());
                    if (p == null) return null;
                    double price = Math.Round(p.MarketValue(Balance) * Math.Max(1, def.Choices.Select(x => x.Success.SalePremium).DefaultIfEmpty(0).Max()));
                    m = Any(ActiveMagnates.Where(x => x.Sector == p.Sector && x.Corp != null && !x.Corp.Bankrupt && x.Money - MagnateReserve(x) >= price).ToList());
                    if (m == null || price <= 0) return null;
                    break;
            }
            a.PlanetId = p?.Id ?? -1;
            a.MagnateId = m?.Id ?? -1;
            return a;
        }

        T Any<T>(IList<T> items) where T : class => items.Count == 0 ? null : items[ShowRng.Range(0, items.Count)];

        /// <summary>Ответить на случай вариантом choice. Действий не тратит. result — чем кончилось.</summary>
        public ActionResult ResolveIncident(int choice, out string result)
        {
            result = null;
            var a = _current.Incident;
            if (a == null) return ActionResult.Fail("Случая нет");
            if (choice < 0 || choice >= a.Def.Choices.Length) return ActionResult.Fail("Нет такого ответа");
            var c = a.Def.Choices[choice];
            double cost = IncidentCost(a, c);
            if (Money < cost) return ActionResult.Fail("Не хватает денег");
            result = ApplyIncident(a, c, cost);
            return ActionResult.Success;
        }

        string ApplyIncident(ActiveIncident a, IncidentChoice c, double cost)
        {
            _current.Incident = null;
            Money -= cost;
            var p = GetPlanet(a.PlanetId);
            var m = GetMagnate(a.MagnateId);
            bool success = c.Chance >= 1 || ShowRng.Chance(c.Chance);
            var o = success || c.Failure == null ? c.Success : c.Failure;
            bool mine = IsMine(p);
            if (mine) p.Earned -= cost;
            if (o.Money != 0)
            {
                double v = Math.Round(o.Money * a.Scale);
                Money += v;
                if (mine) p.Earned += v;
            }
            if (o.Reputation != 0) ChangeReputation(o.Reputation);
            if (o.Loyalty != 0 && mine && p.Inhabited) p.Loyalty = Clamp(p.Loyalty + o.Loyalty);
            if (o.Stock != 0)
            {
                var r = o.Resource ?? p?.Resource ?? Resource.Ice;
                _stock[(int)r] = Math.Max(0, _stock[(int)r] + o.Stock);
            }
            if (o.ReserveShare > 0 && mine && !p.IsRegenerating)
            {
                p.Reserve += p.Capacity * o.ReserveShare;
                p.Capacity = Math.Max(p.Capacity, p.Reserve);
            }
            if (o.AccidentTurns > 0 && mine && p.Mine == MineState.Working) p.AccidentTurnsLeft = Math.Max(p.AccidentTurnsLeft, o.AccidentTurns);
            if (o.ShowPoints != 0 && ShowOn)
            {
                var e = Season.Entries.FirstOrDefault(x => x.CorpId == _current.Id);
                if (e != null)
                {
                    e.Points += o.ShowPoints;
                    e.BonusPoints += o.ShowPoints;
                }
            }
            if (o.QuietTurns > 0 && m != null) m.QuietUntilTurn = Math.Max(m.QuietUntilTurn, Turn + o.QuietTurns);
            string text = IncidentText(a, o.Text);
            if (o.SalePremium > 0 && mine && m?.Corp != null && !m.Corp.Bankrupt)
            {
                double price = IncidentSalePrice(p, a);
                m.Money -= price;
                Money += price;
                p.Earned += price;
                if (p.Earned > p.Invested) _current.Stats.ProfitableSales++;
                GiveToMagnate(p, m);
                SoldPlanets.Add(p);
            }
            return text;
        }

        /// <summary>Игрок промолчал до конца хода — за него выбран последний вариант («ничего не делать»).</summary>
        void ExpireIncident(TurnReport report)
        {
            var a = _current.Incident;
            if (a == null) return;
            var c = a.Def.Choices[a.Def.Choices.Length - 1];
            string result = ApplyIncident(a, c, IncidentCost(a, c));
            report.Events.Add($"Случай «{a.Def.Title}»: вы промолчали. {result}");
        }

        // ------------------------------------------------------------------
        // Лот недели
        // ------------------------------------------------------------------

        /// <summary>Идущие торги за легендарную планету; null — торгов нет.</summary>
        public LotAuction Lot => _lot;

        public bool IsLot(Planet p) => _lot != null && p != null && _lot.PlanetId == p.Id;

        public bool LotsOn => Balance.LotGapMax > 0 && Balance.Legends.Length > 0;

        /// <summary>Ставка текущей корпорации на лот; null — не ставила.</summary>
        public LotBid MyLotBid => _lot?.Bids.FirstOrDefault(x => x.CorpId == _current.Id);

        public double LotBidPrice(double factor) => _lot == null ? 0 : Math.Round(_lot.StartPrice * factor / 10) * 10;

        /// <summary>Закрытая ставка на лот: тратит действие (переговоры), деньги списываются только при победе.</summary>
        public ActionResult PlaceLotBid(double factor)
        {
            if (_lot == null) return ActionResult.Fail("Торгов нет");
            var p = GetPlanet(_lot.PlanetId);
            if (!HasLicense(p.Sector)) return NoLicense(p);
            double amount = LotBidPrice(factor);
            var mine = MyLotBid;
            if (mine != null && mine.Amount >= amount) return ActionResult.Fail("Ваша ставка уже не меньше");
            if (Money < amount) return ActionResult.Fail("Не хватает денег");
            double strength = amount * (1 + Balance.NegotiationPriceStep * Edge(Skill.Negotiation));
            var a = SpendAction(Skill.Negotiation);
            if (!a.Ok) return a;
            if (mine == null) _lot.Bids.Add(new LotBid { CorpId = _current.Id, Amount = amount, Strength = strength });
            else
            {
                mine.Amount = amount;
                mine.Strength = strength;
            }
            return ActionResult.Success;
        }

        void AnnounceLot(Dictionary<int, TurnReport> reports, List<Corporation> players)
        {
            var b = Balance;
            if (!LotsOn || _lot != null) return;
            if (_nextLotTurn == 0) _nextLotTurn = b.LotFirstTurn;
            if (Turn < _nextLotTurn || players.Count == 0) return;

            var sectors = players.SelectMany(c => c.Licenses).Distinct().OrderBy(x => x).ToList();
            int sector = sectors[ShowRng.Range(0, sectors.Count)];
            var resources = SectorResources(sector).ToList();
            var legends = b.Legends.Where(l => (l.OnlySector < 0 || l.OnlySector == sector) && l.Id != _lastLegend
                                               && (l.Resource == null || resources.Contains(l.Resource.Value))).ToList();
            if (legends.Count == 0)
            {
                _nextLotTurn = Turn + Math.Max(1, b.LotGapMin);
                return;
            }
            var legend = Pick(legends, _ => 1.0);
            _lastLegend = legend.Id;
            var type = TypeIn(sector, legend.Resource ?? resources[ShowRng.Range(0, resources.Count)]);

            // Лот по карману самому богатому игроку: слишком дорогую легенду реестр «урезает» до его возможностей.
            double budget = b.LotBudgetShare * players.Max(CapitalOf);
            double factor = legend.ReserveFactor;
            Planet p = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                p = MakeLegend(legend, type, sector, factor);
                if (LotStartPrice(p) <= budget || factor <= 1) break;
                factor = Math.Max(1, factor * 0.75);
            }
            PlaceOnMap(p);
            _planets.Add(p);
            _nextPlanetId++;

            var lot = new LotAuction
            {
                PlanetId = p.Id, Legend = legend.Name, AnnounceTurn = Turn, ResolveTurn = Turn + Math.Max(1, b.LotNoticeTurns) - 1,
                StartPrice = LotStartPrice(p),
            };
            foreach (var m in ActiveMagnates.Where(x => x.Sector == sector && (x.Corp == null || !x.Corp.Bankrupt)))
                if (ShowRng.Chance(Math.Min(0.95, m.AuctionInterest * b.LotInterestFactor))) lot.Interested.Add(m.Id);
            // В большом секторе на торги приходят не все: зал вмещает несколько магнатов.
            while (lot.Interested.Count > b.LotMaxRivals) lot.Interested.RemoveAt(ShowRng.Range(0, lot.Interested.Count));
            _lot = lot;

            string who = lot.Interested.Count == 0 ? "Магнаты пока молчат."
                : "Интерес проявили: " + string.Join(", ", lot.Interested.Select(id => GetMagnate(id)?.Name).Where(n => n != null)) + ".";
            var licensed = players.Where(c => c.Licenses.Contains(sector) && reports.ContainsKey(c.Id)).Select(c => reports[c.Id]).ToList();
            ImportantFor(licensed, AlertKind.Show, $"Лот недели: «{legend.Name}»",
                $"{legend.Description} {p.Type.Name} в секторе «{b.Sectors[sector].Name}». Торги в конце хода {lot.ResolveTurn}, ставки закрытые, " +
                $"стартовая цена {Math.Round(lot.StartPrice)}. {who}", p);
        }

        Planet MakeLegend(LegendaryPlanet legend, PlanetType type, int sector, double factor)
        {
            var b = Balance;
            var p = new Planet(_nextPlanetId, type, Math.Round(type.AverageReserve * factor))
            {
                Sector = sector, Legend = legend.Name, Knowledge = Knowledge.Explored,
                Inhabited = legend.Inhabited, Loyalty = legend.Loyalty, Education = legend.Education, Specialty = legend.Specialty,
                HasSchool = legend.HasSchool, RuinsValue = legend.Ruins,
            };
            if (legend.MineBuilt) p.Mine = MineState.Working;
            var others = SectorResources(sector).Where(r => r != type.Resource).OrderBy(r => r).ToList();
            for (int i = 0; i < legend.ExtraDeposits && others.Count > 0; i++)
            {
                var r = others[(sector + i * 3 + legend.Id.Length) % others.Count];
                others.Remove(r);
                double output = Math.Round(TypeIn(sector, r).OutputPerTurn * b.ExtraOutputMax, 1);
                double capacity = Math.Round(output * b.ExtraCapacityTurnsMax);
                p.ExtraDeposits.Add(new Deposit { Resource = r, OutputPerTurn = Math.Max(1, output), Capacity = capacity, InitialCapacity = capacity, Reserve = capacity });
            }
            return p;
        }

        double LotStartPrice(Planet p) =>
            Math.Max(p.Type.Price, Math.Round((p.MarketValue(Balance) + p.RuinsValue) * Balance.LotPriceFactor / 100) * 100);

        void ResolveLot(Dictionary<int, TurnReport> reports, TurnReport shared)
        {
            var b = Balance;
            var lot = _lot;
            if (lot == null || Turn < lot.ResolveTurn) return;
            _lot = null;
            _nextLotTurn = Turn + 1 + ShowRng.Range(b.LotGapMin, b.LotGapMax + 1);
            var p = GetPlanet(lot.PlanetId);
            if (p == null) return;

            var bids = new List<(double Strength, double Amount, Corporation Corp, Magnate Magnate)>();
            foreach (var bid in lot.Bids)
            {
                var c = CorpById(bid.CorpId);
                if (c == null || c.Bankrupt) continue;
                if (c.Money < bid.Amount)
                {
                    c.Reputation = Math.Min(100, Math.Max(0, c.Reputation + b.LotVoidReputation));
                    if (reports.TryGetValue(c.Id, out var r))
                        Important(r, AlertKind.Trick, "Ставка сгорела",
                            $"На торгах за «{lot.Legend}» у вас не оказалось {Math.Round(bid.Amount)}: аукционист вычеркнул ставку, репутация {b.LotVoidReputation}.", p);
                    continue;
                }
                bids.Add((bid.Strength, bid.Amount, c, null));
            }
            foreach (int id in lot.Interested)
            {
                var m = GetMagnate(id);
                if (m == null || (m.Corp != null && m.Corp.Bankrupt)) continue;
                double amount = lot.StartPrice * ShowRng.Range(b.LotRivalBidMin, b.LotRivalBidMax) * (m.Personality == Personality.Heir ? b.HeirLotMarkup : 1);
                amount = Math.Round(Math.Min(amount, m.Money - MagnateReserve(m)) / 10) * 10;
                if (amount < lot.StartPrice) continue;
                bids.Add((amount, amount, null, m));
            }
            if (bids.Count == 0)
            {
                shared.Events.Add($"Лот «{lot.Legend}» никто не купил: планета осталась у реестра.");
                return;
            }

            var win = bids.OrderByDescending(x => x.Strength).ThenBy(x => x.Corp == null).First();
            string winner;
            if (win.Corp != null)
            {
                var caller = _current;
                _current = win.Corp;
                Acquire(p, win.Amount);
                _current = caller;
                win.Corp.Stats.LotsWon++;
                win.Corp.AuctionsWon++;
                winner = win.Corp.Name;
                if (reports.TryGetValue(win.Corp.Id, out var r))
                    Important(r, AlertKind.Good, "Лот недели ваш!",
                        $"«{lot.Legend}» ({p.Type.Name} #{p.Id}) — ваша за {Math.Round(win.Amount)}. Брэд Квазар: {HostLine(b.ShowHostLot)}", p);
            }
            else
            {
                win.Magnate.Money -= win.Amount;
                GiveToMagnate(p, win.Magnate);
                if (win.Magnate.Corp != null) win.Magnate.Corp.Stats.LotsWon++;
                winner = win.Magnate.Name;
                shared.Events.Add($"Лот «{lot.Legend}» ушёл к {winner} за {Math.Round(win.Amount)}.");
            }
            foreach (var lost in bids.Where(x => x.Corp != null && x.Corp != win.Corp))
                if (reports.TryGetValue(lost.Corp.Id, out var r))
                    Important(r, AlertKind.Trick, "Лот ушёл",
                        $"«{lot.Legend}» досталась {winner} за {Math.Round(win.Amount)}; ваша ставка была {Math.Round(lost.Amount)}.", p, win.Magnate);
        }
    }
}
