using System;
using System.Linq;
using PlanetTrader.Core;

namespace PlanetTrader.Sim
{
    /// <summary>Стиль игры бота. Те же три стиля, что в таблице баланса.</summary>
    public sealed class Strategy
    {
        public string Name;
        /// <summary>Продаёт выработанные и почти выработанные планеты.</summary>
        public bool SellsSpentPlanets;
        /// <summary>Берёт кредит, чтобы купить выгодную планету.</summary>
        public bool UsesCredit;

        public static readonly Strategy Passive = new Strategy { Name = "Пассивный" };
        public static readonly Strategy Active = new Strategy { Name = "Активный", SellsSpentPlanets = true };
        public static readonly Strategy ActiveCredit = new Strategy { Name = "Активный + кредит", SellsSpentPlanets = true, UsesCredit = true };

        public static readonly Strategy[] All = { Passive, Active, ActiveCredit };
    }

    /// <summary>
    /// Простой, но не глупый игрок: выполняет заказы, продаёт ресурсы без обвала цены,
    /// покупает планеты с хорошей окупаемостью, запускает зонды, когда в реестре пусто.
    /// </summary>
    public sealed class Bot
    {
        /// <summary>Покупаем, только если планета за оставшуюся жизнь вернёт вложенное с запасом.</summary>
        const double MinPaybackRatio = 1.4;
        /// <summary>Сколько ходов расходов держать на счету.</summary>
        const double CashBufferTurns = 3;
        /// <summary>Продаём на бирже, пока цена не упадёт ниже этой доли базовой.</summary>
        const double MinSellPriceShare = 0.85;
        /// <summary>Если на складе больше двух глубин рынка, продаём и дешевле.</summary>
        const double DumpPriceShare = 0.6;

        readonly Strategy _s;
        bool _ranOutOfActions;

        public Bot(Strategy strategy)
        {
            _s = strategy;
        }

        public void PlayTurn(Game g)
        {
            if (_ranOutOfActions && g.Managers < g.Balance.MaxManagers && IncomePerTurn(g) > 3 * g.Balance.ManagerSalary)
                g.HireManager();
            _ranOutOfActions = false;

            FulfillOrders(g);
            SellResources(g);
            if (_s.SellsSpentPlanets) SellSpentPlanets(g);
            BuildMines(g);
            BuyPlanets(g);
            ManageDebt(g);
        }

        static double Buffer(Game g) => CashBufferTurns * g.CostsPerTurn;

        static double IncomePerTurn(Game g) =>
            g.PlayerPlanets.Where(p => p.Mine == MineState.Working && !p.IsDepleted)
                .Sum(p => p.Type.OutputPerTurn * g.Balance.BasePrice(p.Resource));

        static void FulfillOrders(Game g)
        {
            foreach (var o in g.Orders.ToList())
                if (g.Stock(o.Resource) >= o.Amount)
                    g.FulfillOrder(o.Id);
        }

        static void SellResources(Game g)
        {
            foreach (var r in ResourceTypes.All)
            {
                double stock = g.Stock(r);
                // Придерживаем ресурс под открытый заказ, если сами его добываем.
                bool producing = g.PlayerPlanets.Any(p => p.Resource == r && p.Mine != MineState.None && !p.IsDepleted);
                var order = g.Orders.Where(o => o.Resource == r).OrderBy(o => o.Amount).FirstOrDefault();
                if (order != null && producing) stock = Math.Max(0, stock - order.Amount);
                if (stock <= 0) continue;

                // Сколько можно продать, не уронив цену ниже порога.
                double share = stock > 2 * g.Balance.Depth(r) ? DumpPriceShare : MinSellPriceShare;
                double floor = share * g.Balance.BasePrice(r);
                double price = g.Market.Price(r);
                if (price <= floor) continue;
                double maxDrop = 1 - floor / price;
                double maxAmount = maxDrop / g.Balance.SalesImpact * g.Balance.Depth(r);
                double amount = Math.Min(stock, maxAmount);
                if (amount > 0.5) g.SellResource(r, amount);
            }
        }

        void SellSpentPlanets(Game g)
        {
            foreach (var p in g.PlayerPlanets.ToList())
            {
                if (g.ActionsLeft == 0) { _ranOutOfActions = true; return; }
                if (p.Mine == MineState.Working && p.RemainingTurns < 2)
                    g.SellPlanet(p.Id);
            }
        }

        void BuildMines(Game g)
        {
            foreach (var p in g.PlayerPlanets.Where(p => p.Mine == MineState.None && !p.IsDepleted).ToList())
            {
                if (g.ActionsLeft == 0) { _ranOutOfActions = true; return; }
                if (g.Money - p.Type.MineCost < Buffer(g)) return;
                g.BuildMine(p.Id);
            }
        }

        /// <summary>Чистый доход планеты за оставшуюся жизнь с учётом роста общего содержания.</summary>
        static double PaybackRatio(Game g, Planet p)
        {
            var b = g.Balance;
            int n = g.UpkeepCurvePlanets;
            double baseUpkeep = g.PlayerPlanets.Sum(x => x.BaseUpkeep(b));
            double extraUpkeep = (baseUpkeep + p.Type.Upkeep) * b.UpkeepMultiplier(n + 1) - baseUpkeep * b.UpkeepMultiplier(n);
            double net = p.Type.OutputPerTurn * b.BasePrice(p.Resource) - extraUpkeep;
            double cost = p.ListPrice(b) + (p.Mine == MineState.None ? p.Type.MineCost : 0);
            return net * p.RemainingTurns / cost;
        }

        void BuyPlanets(Game g)
        {
            // Пока планеты не окупают расходы, не расширяемся.
            if (IncomePerTurn(g) < g.CostsPerTurn) return;

            while (g.ActionsLeft > 0)
            {
                // Сколько можно потратить: деньги сверх запаса, а с кредитной стратегией — ещё и свободный кредит.
                // Занимаем только в пределах полки содержания: дальше кредит не окупается.
                bool borrow = _s.UsesCredit && g.UpkeepCurvePlanets < g.Balance.UpkeepShelfEnd;
                double budget = g.Money - Buffer(g) + (borrow ? g.AvailableCredit : 0);
                bool anyGood = false;
                Planet pick = null;
                double pickRatio = 0;
                foreach (var p in g.PlanetsForSale)
                {
                    double ratio = PaybackRatio(g, p);
                    if (ratio < MinPaybackRatio) continue;
                    anyGood = true;
                    if (FullCost(g, p) <= budget && ratio > pickRatio)
                    {
                        pick = p;
                        pickRatio = ratio;
                    }
                }

                if (pick == null)
                {
                    // В реестре нет ничего выгодного — ищем зондом. Если выгодное есть, но не по карману, копим.
                    if (!anyGood && g.Money - g.Balance.ProbeCost >= Buffer(g) + 1500)
                    {
                        g.LaunchProbe(out _);
                        continue;
                    }
                    return;
                }

                double shortfall = FullCost(g, pick) + Buffer(g) - g.Money;
                if (shortfall > 0) g.TakeLoan(Math.Min(shortfall, g.AvailableCredit));

                g.BuyPlanet(pick.Id);
                if (g.ActionsLeft == 0) break;
                if (pick.Mine == MineState.None) g.BuildMine(pick.Id);
            }
            _ranOutOfActions = true;
        }

        static double FullCost(Game g, Planet p) =>
            p.ListPrice(g.Balance) + (p.Mine == MineState.None ? p.Type.MineCost : 0);

        void ManageDebt(Game g)
        {
            if (g.Debt <= 0) return;
            // Без кредитной стратегии держим запас на покупку планеты. Заёмщик гасит всё лишнее
            // и при нужде займёт снова.
            double keep = Buffer(g) + (_s.UsesCredit ? 0 : 4000);
            double spare = g.Money - keep;
            if (spare > 0) g.RepayLoan(Math.Min(spare, g.Debt));
        }
    }
}
