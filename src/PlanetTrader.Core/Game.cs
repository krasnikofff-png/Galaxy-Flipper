using System;
using System.Collections.Generic;
using System.Linq;

namespace PlanetTrader.Core
{
    public readonly struct ActionResult
    {
        public readonly bool Ok;
        public readonly string Error;

        ActionResult(bool ok, string error)
        {
            Ok = ok;
            Error = error;
        }

        public static readonly ActionResult Success = new ActionResult(true, null);
        public static ActionResult Fail(string error) => new ActionResult(false, error);
    }

    /// <summary>Что произошло за ход. Нужен интерфейсу и симулятору.</summary>
    public sealed class TurnReport
    {
        public int Turn;
        public double Upkeep;
        public double Salaries;
        public double Interest;
        public double Insurance;
        public double InsurancePayout;
        public int Accidents;
        /// <summary>Сколько планет забрали коллекторы.</summary>
        public int Seizures;
        public readonly List<string> Events = new List<string>();

        public double TotalCosts => Upkeep + Salaries + Interest + Insurance;
    }

    /// <summary>
    /// Состояние партии и все правила экономики. Один игрок против ИИ.
    /// Действия (покупка, шахта, продажа планеты, зонд, найм) тратят очки действий,
    /// торговля на бирже, заказы и кредит — бесплатны.
    /// </summary>
    public sealed class Game
    {
        static readonly string[] Customers =
        {
            "Империя Зорг", "Корпорация «Вечный понедельник»", "Синдикат Глорб",
            "Республика Пыльных Лун", "Орден Бухгалтеров", "Кооператив «Тёплая туманность»",
        };

        public readonly Balance Balance;
        public readonly Market Market;
        readonly Rng _rng;
        readonly List<Planet> _planets = new List<Planet>();
        readonly List<Order> _orders = new List<Order>();
        readonly double[] _stock = new double[Resources.Count];
        int _nextPlanetId;
        int _nextOrderId;

        public int Turn { get; private set; } = 1;
        public double Money { get; private set; }
        public double Debt { get; private set; }
        public int Managers { get; private set; }
        public int ActionsLeft { get; private set; }
        public bool Insured { get; private set; }

        // Статистика для симулятора и плейтестов.
        public int TotalSeizures { get; private set; }
        public int TurnsActionsExhausted { get; private set; }
        public readonly List<Planet> SoldPlanets = new List<Planet>();

        public IReadOnlyList<Planet> AllPlanets => _planets;
        public IReadOnlyList<Order> Orders => _orders;
        public IEnumerable<Planet> PlayerPlanets => _planets.Where(p => p.Owner == Owner.Player);
        public int PlayerPlanetCount => _planets.Count(p => p.Owner == Owner.Player);

        /// <summary>Планеты, которые игрок может купить прямо сейчас.</summary>
        public IEnumerable<Planet> PlanetsForSale => _planets.Where(p => p.Owner == Owner.Registry && !p.IsDepleted);

        public double Stock(Resource r) => _stock[(int)r];

        public int ActionsPerTurn => Balance.BaseActionsPerTurn + Managers * Balance.ActionsPerManager;

        Game(Balance balance, ulong seed)
        {
            Balance = balance;
            _rng = new Rng(seed);
            Market = new Market(balance);
        }

        public static Game New(Balance balance, ulong seed)
        {
            var g = new Game(balance, seed);
            g.Money = balance.StartMoney;
            g.Debt = balance.StartDebt;

            for (int i = 0; i < balance.SectorPlanets; i++)
                g._planets.Add(g.GeneratePlanet(g.RandomType()));

            int aiCount = (int)Math.Round(balance.SectorPlanets * balance.AiStartShare);
            foreach (var p in g._planets.OrderBy(_ => g._rng.NextULong()).Take(aiCount))
            {
                p.Owner = Owner.Ai;
                p.Mine = MineState.Working;
            }

            // Стартовая планета: скромный ледяной карлик со средним запасом и работающей шахтой.
            var start = new Planet(g._nextPlanetId++, balance.TypeOf(Resource.Ice), balance.TypeOf(Resource.Ice).AverageReserve)
            {
                Owner = Owner.Player,
                Mine = balance.StartPlanetHasMine ? MineState.Working : MineState.None,
                BoughtOnTurn = 0,
            };
            g._planets.Add(start);

            g.ActionsLeft = g.ActionsPerTurn;
            return g;
        }

        PlanetType RandomType()
        {
            double total = Balance.PlanetTypes.Sum(t => t.Weight);
            double roll = _rng.NextDouble() * total;
            foreach (var t in Balance.PlanetTypes)
            {
                roll -= t.Weight;
                if (roll < 0) return t;
            }
            return Balance.PlanetTypes[Balance.PlanetTypes.Length - 1];
        }

        Planet GeneratePlanet(PlanetType type)
        {
            double spread = Balance.ReserveSpread;
            double reserve = Math.Round(type.AverageReserve * _rng.Range(1 - spread, 1 + spread));
            return new Planet(_nextPlanetId++, type, reserve);
        }

        public Planet GetPlanet(int id) => _planets.FirstOrDefault(p => p.Id == id);

        // ------------------------------------------------------------------
        // Оценки
        // ------------------------------------------------------------------

        public double PlanetsValue => PlayerPlanets.Sum(p => p.MarketValue(Balance));
        /// <summary>Склад по выручке, если продать всё разом, — с учётом падения цены.</summary>
        public double StockValue => Resources.All.Sum(r => Market.Quote(r, _stock[(int)r]));

        /// <summary>Капитал = деньги − долг + стоимость планет + стоимость склада.</summary>
        public double Capital => Money - Debt + PlanetsValue + StockValue;

        public double CreditLimit => Balance.CreditLimitShare * PlanetsValue;
        public double AvailableCredit => Math.Max(0, CreditLimit - Debt);

        /// <summary>Сколько планет учитывается в множителе содержания.</summary>
        public int UpkeepCurvePlanets =>
            PlayerPlanets.Count(p => Balance.DepletedCountsInUpkeepCurve || !p.IsDepleted);

        public double UpkeepPerTurn =>
            PlayerPlanets.Sum(p => p.BaseUpkeep(Balance)) * Balance.UpkeepMultiplier(UpkeepCurvePlanets);

        public double InsurancePerTurn => Insured ? Balance.InsuranceRate * PlanetsValue : 0;

        public double CostsPerTurn =>
            UpkeepPerTurn + Managers * Balance.ManagerSalary + Debt * Balance.InterestPerTurn + InsurancePerTurn;

        // ------------------------------------------------------------------
        // Действия, которые тратят очки действий
        // ------------------------------------------------------------------

        ActionResult SpendAction()
        {
            if (ActionsLeft <= 0) return ActionResult.Fail("Действия на этот ход закончились");
            ActionsLeft--;
            if (ActionsLeft == 0) TurnsActionsExhausted++;
            return ActionResult.Success;
        }

        public ActionResult BuyPlanet(int planetId)
        {
            var p = GetPlanet(planetId);
            if (p == null || p.Owner != Owner.Registry) return ActionResult.Fail("Планета не продаётся");
            if (p.IsDepleted) return ActionResult.Fail("Планета выработана");
            double price = p.ListPrice(Balance);
            if (Money < price) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;

            Money -= price;
            p.Owner = Owner.Player;
            p.BoughtOnTurn = Turn;
            p.Invested += price;
            return ActionResult.Success;
        }

        public ActionResult BuildMine(int planetId)
        {
            var p = GetPlanet(planetId);
            if (p == null || p.Owner != Owner.Player) return ActionResult.Fail("Это не ваша планета");
            if (p.Mine != MineState.None) return ActionResult.Fail("Шахта уже есть");
            if (Money < p.Type.MineCost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;

            Money -= p.Type.MineCost;
            p.Invested += p.Type.MineCost;
            p.Mine = MineState.Building;
            p.BuildTurnsLeft = Balance.MineBuildTurns;
            return ActionResult.Success;
        }

        /// <summary>Продать планету по рыночной стоимости минус комиссия. Она уходит в реестр.</summary>
        public ActionResult SellPlanet(int planetId)
        {
            var p = GetPlanet(planetId);
            if (p == null || p.Owner != Owner.Player) return ActionResult.Fail("Это не ваша планета");
            var a = SpendAction();
            if (!a.Ok) return a;

            double proceeds = SalePrice(p);
            Money += proceeds;
            p.Earned += proceeds;
            ReleaseToRegistry(p);
            SoldPlanets.Add(p);
            return ActionResult.Success;
        }

        public double SalePrice(Planet p) => p.MarketValue(Balance) * (1 - Balance.ResaleCommission);

        void ReleaseToRegistry(Planet p)
        {
            p.Owner = Owner.Registry;
            p.FoundByProbe = false;
        }

        /// <summary>Зонд: с шансом находит новую планету, которую видит только игрок.</summary>
        public ActionResult LaunchProbe(out Planet found)
        {
            found = null;
            if (Money < Balance.ProbeCost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;

            Money -= Balance.ProbeCost;
            if (_rng.Chance(Balance.ProbeFindChance))
            {
                found = GeneratePlanet(RandomType());
                found.FoundByProbe = true;
                _planets.Add(found);
            }
            return ActionResult.Success;
        }

        /// <summary>Нанять менеджера. Даёт действие со следующего хода.</summary>
        public ActionResult HireManager()
        {
            if (Managers >= Balance.MaxManagers) return ActionResult.Fail("Больше менеджеров нанять нельзя");
            var a = SpendAction();
            if (!a.Ok) return a;
            Managers++;
            return ActionResult.Success;
        }

        public ActionResult FireManager()
        {
            if (Managers == 0) return ActionResult.Fail("Менеджеров нет");
            Managers--;
            return ActionResult.Success;
        }

        // ------------------------------------------------------------------
        // Бесплатные действия
        // ------------------------------------------------------------------

        public ActionResult SellResource(Resource r, double amount)
        {
            if (amount <= 0) return ActionResult.Fail("Нечего продавать");
            if (_stock[(int)r] + 1e-9 < amount) return ActionResult.Fail("Столько ресурса нет на складе");
            _stock[(int)r] -= amount;
            Money += Market.Sell(r, amount);
            return ActionResult.Success;
        }

        public ActionResult FulfillOrder(int orderId)
        {
            var o = _orders.FirstOrDefault(x => x.Id == orderId);
            if (o == null) return ActionResult.Fail("Заказа нет");
            if (_stock[(int)o.Resource] + 1e-9 < o.Amount) return ActionResult.Fail("Не хватает ресурса");
            _stock[(int)o.Resource] -= o.Amount;
            Money += o.Payout(Market, Balance);
            _orders.Remove(o);
            return ActionResult.Success;
        }

        public ActionResult TakeLoan(double amount)
        {
            if (amount <= 0) return ActionResult.Fail("Сумма должна быть больше нуля");
            if (amount > AvailableCredit + 1e-9) return ActionResult.Fail("Превышен кредитный лимит");
            Debt += amount;
            Money += amount;
            return ActionResult.Success;
        }

        public ActionResult RepayLoan(double amount)
        {
            amount = Math.Min(amount, Debt);
            if (amount <= 0) return ActionResult.Fail("Долга нет");
            if (Money < amount) return ActionResult.Fail("Не хватает денег");
            Debt -= amount;
            Money -= amount;
            return ActionResult.Success;
        }

        public void SetInsurance(bool on) => Insured = on;

        // ------------------------------------------------------------------
        // Конец хода
        // ------------------------------------------------------------------

        public TurnReport EndTurn()
        {
            var report = new TurnReport { Turn = Turn };

            ProducePlayer(report);
            ProduceAi();

            // Расходы считаются по состоянию на конец хода.
            report.Upkeep = UpkeepPerTurn;
            report.Salaries = Managers * Balance.ManagerSalary;
            report.Interest = Debt * Balance.InterestPerTurn;
            report.Insurance = InsurancePerTurn;
            double multiplier = Balance.UpkeepMultiplier(UpkeepCurvePlanets);
            foreach (var p in PlayerPlanets)
                p.Earned -= p.BaseUpkeep(Balance) * multiplier;
            Money -= report.TotalCosts;

            if (Money < 0) CallCollectors(report);

            UpdateOrders(report);
            AiTurn(report);
            Market.Tick(_rng);

            Turn++;
            ActionsLeft = ActionsPerTurn;
            return report;
        }

        void ProducePlayer(TurnReport report)
        {
            foreach (var p in PlayerPlanets)
            {
                if (p.Mine == MineState.Building)
                {
                    if (--p.BuildTurnsLeft <= 0) p.Mine = MineState.Working;
                    continue;
                }
                if (p.Mine != MineState.Working || p.IsDepleted) continue;

                if (p.AccidentTurnsLeft == 0 && _rng.Chance(Balance.AccidentChance))
                {
                    p.AccidentTurnsLeft = Balance.AccidentTurns;
                    report.Accidents++;
                    report.Events.Add($"Авария на планете {p.Type.Name} #{p.Id}");
                }

                double amount = Math.Min(p.Type.OutputPerTurn, p.Reserve);
                if (p.AccidentTurnsLeft > 0)
                {
                    p.AccidentTurnsLeft--;
                    if (Insured)
                    {
                        double payout = amount * Market.Price(p.Resource);
                        Money += payout;
                        report.InsurancePayout += payout;
                    }
                    continue;
                }

                p.Reserve -= amount;
                _stock[(int)p.Resource] += amount;
                p.Earned += amount * Market.Price(p.Resource);
            }
        }

        void ProduceAi()
        {
            foreach (var p in _planets)
                if (p.Owner == Owner.Ai && p.Mine == MineState.Working && !p.IsDepleted)
                    p.Reserve -= Math.Min(p.Type.OutputPerTurn, p.Reserve);
        }

        /// <summary>
        /// Невыплата: недостача уходит в долг. Если долг выше кредитного лимита,
        /// коллекторы забирают самые дорогие планеты, пока долг не войдёт в лимит.
        /// Игра продолжается.
        /// </summary>
        void CallCollectors(TurnReport report)
        {
            Debt += -Money;
            Money = 0;
            while (Debt > CreditLimit + 1e-9)
            {
                var best = PlayerPlanets.OrderByDescending(p => p.MarketValue(Balance)).FirstOrDefault();
                if (best == null || best.MarketValue(Balance) <= 0) break;
                double proceeds = SalePrice(best);
                Debt -= proceeds;
                best.Earned += proceeds;
                ReleaseToRegistry(best);
                SoldPlanets.Add(best);
                report.Seizures++;
                TotalSeizures++;
                report.Events.Add($"Коллекторы забрали планету {best.Type.Name} #{best.Id}");
            }
            if (Debt < 0)
            {
                Money += -Debt;
                Debt = 0;
            }
        }

        void UpdateOrders(TurnReport report)
        {
            _orders.RemoveAll(o => o.DeadlineTurn <= Turn);
            if (!_rng.Chance(Balance.OrderChancePerTurn)) return;

            var r = Resources.All[_rng.Range(0, Resources.Count)];
            var type = Balance.TypeOf(r);
            double amount = type.OutputPerTurn * _rng.Range(Balance.OrderMinTurnsOfOutput, Balance.OrderMaxTurnsOfOutput + 1);
            amount = Math.Max(1, Math.Round(amount / 5) * 5);
            var o = new Order(_nextOrderId++, Customers[_rng.Range(0, Customers.Length)], r, amount, Turn + Balance.OrderDeadlineTurns);
            _orders.Add(o);
            report.Events.Add($"{o.Customer}: нужно {o.Amount} ({r.RuName()}) за {Balance.OrderDeadlineTurns} ходов");
        }

        /// <summary>ИИ выкупает лучшую свободную планету и иногда перепродаёт свою.</summary>
        void AiTurn(TurnReport report)
        {
            if (_rng.Chance(Balance.AiBuyChance))
            {
                var best = PlanetsForSale
                    .Where(p => !p.FoundByProbe)
                    .OrderByDescending(p => p.MarketValue(Balance) / Math.Max(1, p.ListPrice(Balance)))
                    .FirstOrDefault();
                if (best != null && best.MarketValue(Balance) > 0)
                {
                    best.Owner = Owner.Ai;
                    if (best.Mine == MineState.None) best.Mine = MineState.Working;
                    report.Events.Add($"ИИ-магнат купил планету {best.Type.Name} #{best.Id}");
                }
            }

            if (_rng.Chance(Balance.AiResaleChance))
            {
                var aiPlanets = _planets.Where(p => p.Owner == Owner.Ai && !p.IsDepleted).ToList();
                if (aiPlanets.Count > 0)
                {
                    var p = aiPlanets[_rng.Range(0, aiPlanets.Count)];
                    p.Owner = Owner.Registry;
                    report.Events.Add($"ИИ-магнат выставил на продажу планету {p.Type.Name} #{p.Id}");
                }
            }
        }
    }
}
