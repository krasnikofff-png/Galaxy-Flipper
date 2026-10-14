using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using Xunit;

namespace PlanetTrader.Core.Tests;

/// <summary>
/// Жители продают планету только тому, кто её разведал. Тесты сделок с жителями разведывают планету сразу,
/// как будто зонд уже вернулся.
/// </summary>
static class ExploredOffers
{
    public static ActionResult OfferExplored(this Game g, int id, Offer offer, out bool accepted)
    {
        var p = g.GetPlanet(id);
        if (p != null) p.Knowledge = Knowledge.Explored;
        return g.MakeOffer(id, offer, out accepted);
    }
}

public class BalanceTests
{
    [Theory]
    [InlineData(1, 1.00)]
    [InlineData(8, 1.50)]
    [InlineData(12, 1.50)]
    [InlineData(16, 1.50)]
    [InlineData(24, 2.38)]
    [InlineData(30, 3.38)]
    public void UpkeepCurveMatchesSpreadsheet(int planets, double expected)
    {
        // Форма кривой из вкладки «Кривая с полкой» (полка 8–16).
        Assert.Equal(expected, new Balance { UpkeepShelfEnd = 16, UpkeepCurveK = 1.336 }.UpkeepMultiplier(planets), 2);
    }

    [Fact]
    public void ShelfEndsWhereBalanceSays()
    {
        var b = new Balance();
        Assert.Equal(b.UpkeepMultiplier(b.UpkeepShelfStart), b.UpkeepMultiplier(b.UpkeepShelfEnd), 6);
        Assert.True(b.UpkeepMultiplier(b.UpkeepShelfEnd + 1) > b.UpkeepMultiplier(b.UpkeepShelfEnd));
    }
}

public class MarketTests
{
    [Fact]
    public void SellingMarketDepthDropsPriceByImpact()
    {
        var b = new Balance();
        var m = new Market(b);
        double revenue = m.Sell(Resource.Ice, b.Depth(Resource.Ice));

        Assert.Equal(10 * (1 - b.SalesImpact), m.Price(Resource.Ice), 6);
        // Средняя цена между «до» и «после».
        Assert.Equal(150 * 10 * (1 - b.SalesImpact / 2), revenue, 6);
    }

    [Fact]
    public void PriceRecoversTowardBase()
    {
        var b = new Balance { MarketNoise = 0 };
        var m = new Market(b);
        m.Sell(Resource.Quantium, 50);
        double low = m.Price(Resource.Quantium);
        m.Tick(new Rng(1));
        Assert.Equal(low + 0.15 * (110 - low), m.Price(Resource.Quantium), 6);
    }

    [Fact]
    public void PriceNeverFallsBelowFloor()
    {
        var b = new Balance();
        var m = new Market(b);
        m.Sell(Resource.Quantium, 10_000);
        Assert.Equal(110 * b.MinPriceShare, m.Price(Resource.Quantium), 6);
    }
}

public class GameTests
{
    static Balance Calm() => new Balance
    {
        AccidentChance = 0,
        AiBuyChance = 0,
        AiResaleChance = 0,
        OrderChancePerTurn = 0,
        MarketNoise = 0,
        Events = new GalacticEvent[0],
        MeteorChance = 0,
        EpidemicChance = 0,
        DiscoveryChancePerTurn = 0,
        // Доставка мгновенная и бесплатная, чтобы проверять экономику отдельно от карты.
        ShipSpeed = 100,
        ShippingCostPerDistance = 0,
    };

    static ProbeType Far(Balance b) => b.ProbeTypes.Last();

    [Fact]
    public void SameSeedGivesSameGame()
    {
        var a = Game.New(new Balance(), 42);
        var b = Game.New(new Balance(), 42);
        for (int i = 0; i < 20; i++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(a.Capital, b.Capital);
    }

    [Fact]
    public void StartStateMatchesBalance()
    {
        var b = new Balance();
        var g = Game.New(b, 1);
        Assert.Equal(b.StartMoney, g.Money);
        Assert.Equal(1000, g.Debt);
        Assert.Equal(1, g.PlayerPlanetCount);
        Assert.Equal(3, g.ActionsLeft);
        Assert.Equal(10, g.AllPlanets.Count(p => p.Owner == Owner.Ai));
    }

    [Fact]
    public void WorkingMineProducesAndCostsArePaid()
    {
        var g = Game.New(Calm(), 1);
        var start = g.PlayerPlanets.Single();
        double money = g.Money;

        var report = g.EndTurn();

        Assert.Equal(28, g.Stock(Resource.Ice));
        Assert.Equal(g.Balance.TypeOf(Resource.Ice).AverageReserve - 28, start.Reserve);
        Assert.Equal(40, report.Upkeep);
        Assert.Equal(1000 * 0.02, report.Interest);
        Assert.Equal(money - 60, g.Money, 6);
    }

    [Fact]
    public void ActionsAreLimitedPerTurn()
    {
        var b = Calm();
        b.StartMoney = 100_000;
        var g = Game.New(b, 1);

        var probe = Far(b);
        for (int i = 0; i < 3; i++) Assert.True(g.LaunchScan(probe, 0.5, 0.5).Ok);
        Assert.False(g.LaunchScan(probe, 0.5, 0.5).Ok);

        g.EndTurn();
        Assert.Equal(3, g.ActionsLeft);
    }

    [Fact]
    public void ManagerAddsActionNextTurnAndCostsSalary()
    {
        var g = Game.New(Calm(), 1);
        Assert.Equal(3, g.ActionsLeft);
        Assert.Equal(3, g.Candidates.Count);
        var candidate = g.Candidates[1];
        Assert.True(g.HireManager(1).Ok);
        Assert.Equal(2, g.Candidates.Count);
        Assert.Equal(2, g.ActionsLeft); // новичок работает со следующего хода
        var report = g.EndTurn();
        Assert.Equal(candidate.Salary, report.Salaries);
        Assert.Equal(4, g.ActionsLeft);
        Assert.Contains(candidate, g.Staff);
    }

    [Fact]
    public void BestManagerForTheJobActsAndSkillsMatter()
    {
        var b = Calm();
        b.StartMoney = 100_000;
        var g = Game.New(b, 3);
        var p = g.PlanetsForSale.First(x => !x.Inhabited);
        p.FoundByProbe = true;
        p.RuinsValue = 0;
        double listPrice = p.ListPrice(b);
        Assert.Equal(listPrice, g.PurchasePrice(p)); // директор — средний переговорщик

        // Переговорщик с навыком 5 выбивает скидку 2 × 4%.
        var c = g.Candidates[0];
        c.SkillLevels[(int)Skill.Negotiation] = 5;
        c.SkillLevels[(int)Skill.Geology] = 1;
        Assert.True(g.HireManager(0).Ok);
        g.EndTurn();
        Assert.Same(c, g.WhoActs(Skill.Negotiation));
        Assert.Equal(System.Math.Round(listPrice * (1 - 2 * b.NegotiationPriceStep)), g.PurchasePrice(p));
        // Для шахты он плох — шахту строит директор.
        Assert.True(g.WhoActs(Skill.Geology).IsDirector);

        // Ручной выбор: геологию поручили переговорщику — шахта дороже.
        Assert.True(g.SetPerformer(c.Id).Ok);
        Assert.Same(c, g.WhoActs(Skill.Geology));
        Assert.Equal(System.Math.Round(p.Type.MineCost * (1 + 2 * b.GeologyCostStep)), g.MineCost(p));
        double money = g.Money;
        double price = g.PurchasePrice(p);
        Assert.True(g.BuyPlanet(p.Id).Ok);
        Assert.Equal(money - price, g.Money, 6);
        Assert.Equal(0, c.ActionsLeft);
        // Когда у выбранного действий нет, берётся лучший из свободных.
        Assert.True(g.WhoActs(Skill.Geology).IsDirector);
    }

    [Fact]
    public void FiringFreesSalaryAndCandidatesRefresh()
    {
        var b = Calm();
        b.CandidateRefreshTurns = 2;
        var g = Game.New(b, 3);
        Assert.True(g.HireManager().Ok);
        Assert.Equal(1, g.Managers);
        Assert.True(g.FireManager().Ok);
        Assert.Equal(0, g.Managers);
        Assert.Equal(0, g.SalariesPerTurn);
        var before = g.Candidates.ToList();
        g.EndTurn();
        g.EndTurn();
        Assert.Equal(b.ManagerCandidates, g.Candidates.Count);
        Assert.DoesNotContain(before[0], g.Candidates);
    }

    [Fact]
    public void BoughtPlanetNeedsMineThatTakesTwoTurns()
    {
        var b = Calm();
        b.StartMoney = 100_000;
        var g = Game.New(b, 3);
        var p = g.PlanetsForSale.First();
        p.FoundByProbe = true; // без аукциона
        p.RuinsValue = 0;
        double price = p.ListPrice(b);

        Assert.True(g.BuyPlanet(p.Id).Ok);
        Assert.Equal(100_000 - price, g.Money, 6);
        Assert.True(g.BuildMine(p.Id).Ok);

        double reserve = p.Reserve;
        for (int i = 0; i < b.MineBuildTurns; i++) g.EndTurn();
        Assert.Equal(reserve, p.Reserve);
        g.EndTurn();
        Assert.True(p.Reserve < reserve);
    }

    [Fact]
    public void AuctionPriceHasAiMarkupButProbeFindDoesNot()
    {
        var b = new Balance();
        var auction = new Planet(1, b.PlanetTypes[0], b.PlanetTypes[0].AverageReserve);
        var probed = new Planet(2, b.PlanetTypes[0], b.PlanetTypes[0].AverageReserve) { FoundByProbe = true };
        Assert.Equal(1600 * (1 + b.AuctionMarkup), auction.ListPrice(b), 0);
        Assert.Equal(1600, probed.ListPrice(b), 0);
    }

    [Fact]
    public void SellingPlanetPaysValueMinusCommission()
    {
        var g = Game.New(Calm(), 1);
        var p = g.PlayerPlanets.Single();
        double expected = p.MarketValue(g.Balance) * 0.9;
        double money = g.Money;

        Assert.True(g.SellPlanet(p.Id).Ok);
        Assert.Equal(money + expected, g.Money, 6);
        Assert.Equal(Owner.Registry, p.Owner);
    }

    [Fact]
    public void LoanIsLimitedByPlanetValue()
    {
        var g = Game.New(Calm(), 1);
        Assert.False(g.TakeLoan(g.AvailableCredit + 1).Ok);
        Assert.True(g.TakeLoan(g.AvailableCredit).Ok);
        Assert.Equal(g.CreditLimit, g.Debt, 6);
    }

    [Fact]
    public void CollectorsSeizeBestPlanetWhenPlayerCannotPay()
    {
        var b = Calm();
        b.StartMoney = 0;
        b.StartDebt = 0;
        var g = Game.New(b, 1);
        var p = g.PlayerPlanets.Single();
        // Долг на весь лимит, деньги тратим, ресурсы не продаём — рано или поздно платить нечем.
        g.TakeLoan(g.AvailableCredit);
        var probe = g.Balance.ProbeTypes[0];
        while (g.Money >= probe.Cost && g.LaunchScan(probe, p.MapX, p.MapY).Ok) { }

        var alerts = new List<Alert>();
        for (int i = 0; i < 50 && g.TotalSeizures == 0; i++) alerts.AddRange(g.EndTurn().Alerts);

        Assert.Equal(1, g.TotalSeizures);
        Assert.Contains(alerts, a => a.Kind == AlertKind.PlanetLost && a.PlanetId == p.Id);
        // Банкрот — только если после продажи последней планеты долг остался.
        Assert.Equal(g.Bankrupt, alerts.Any(a => a.Kind == AlertKind.Bankrupt));
        Assert.Equal(Owner.Registry, p.Owner);
        Assert.Equal(0, g.PlayerPlanetCount);
    }

    [Fact]
    public void FulfilledOrderPaysMarkup()
    {
        var b = Calm();
        b.OrderChancePerTurn = 1;
        var g = Game.New(b, 5);
        // Копим лёд, пока не появится заказ на лёд, который можно закрыть.
        for (int i = 0; i < 200; i++)
        {
            var order = g.Orders.FirstOrDefault(o => g.Stock(o.Resource) >= o.Amount && g.OrderReachable(o));
            if (order != null)
            {
                double expected = order.Amount * g.Market.Price(order.Resource) * 1.5;
                double money = g.Money;
                Assert.True(g.FulfillOrder(order.Id).Ok);
                Assert.Equal(money + expected, g.Money, 6);
                return;
            }
            g.EndTurn();
        }
        Assert.Fail("Не дождались выполнимого заказа");
    }
}

public class ListingTests
{
    [Fact]
    public void PlanetWithMineIsListedWithMineCost()
    {
        var b = new Balance();
        var type = b.PlanetTypes[0];
        var bare = new Planet(1, type, type.AverageReserve) { FoundByProbe = true };
        var mined = new Planet(2, type, type.AverageReserve) { FoundByProbe = true, Mine = MineState.Working };
        Assert.Equal(bare.ListPrice(b) + type.MineCost, mined.ListPrice(b), 0);
    }

    [Fact]
    public void DepletedPlanetIsCheaperToKeep()
    {
        var b = new Balance();
        var type = b.PlanetTypes[0];
        var p = new Planet(1, type, 0);
        Assert.Equal(type.Upkeep * b.DepletedUpkeepShare, p.BaseUpkeep(b), 6);
    }
}

public class DiscoveryTests
{
    [Fact]
    public void DiscoveredPlanetsAppearInRegistryAtExpectedRate()
    {
        var b = new Balance { AiBuyChance = 0, AiResaleChance = 0, AiStartShare = 0, AccidentChance = 0, MaxSectorPlanets = 1000 };
        var g = Game.New(b, 9);
        int before = g.AllPlanets.Count;
        for (int i = 0; i < 700; i++) g.EndTurn();
        int found = g.AllPlanets.Count - before;
        // Ожидаем ~100 открытий за 700 ходов.
        Assert.InRange(found, 70, 130);
        Assert.All(g.AllPlanets.Skip(before), p => Assert.False(p.FoundByProbe));
    }

    [Fact]
    public void FullSectorHasNoMoreDiscoveries()
    {
        var b = new Balance { AiBuyChance = 0, AiResaleChance = 0, AiStartShare = 0, AccidentChance = 0 };
        var g = Game.New(b, 9);
        for (int i = 0; i < 1000; i++) g.EndTurn();
        Assert.True(g.IsSectorFull());
        Assert.Equal(b.MaxSectorPlanets, g.AllPlanets.Count);
    }

    [Fact]
    public void NoDiscoveriesWhenChanceIsZero()
    {
        var g = Game.New(new Balance { DiscoveryChancePerTurn = 0 }, 9);
        int before = g.AllPlanets.Count;
        for (int i = 0; i < 100; i++) g.EndTurn();
        Assert.Equal(before, g.AllPlanets.Count);
    }
}

public class MapTests
{
    [Fact]
    public void PlanetsAreSpreadOverTheMap()
    {
        var g = Game.New(new Balance(), 4);
        foreach (var p in g.AllPlanets)
        {
            Assert.InRange(p.MapX, 0.0, 1.0);
            Assert.InRange(p.MapY, 0.0, 1.0);
        }
        var points = g.AllPlanets.ToList();
        int tooClose = 0;
        for (int i = 0; i < points.Count; i++)
        for (int j = i + 1; j < points.Count; j++)
        {
            double dx = points[i].MapX - points[j].MapX, dy = points[i].MapY - points[j].MapY;
            if (Math.Sqrt(dx * dx + dy * dy) < 0.05) tooClose++;
        }
        Assert.Equal(0, tooClose);
    }
}

public class ExplorationTests
{
    static Balance Quiet() => new Balance
    {
        AccidentChance = 0,
        AiBuyChance = 0,
        AiResaleChance = 0,
        OrderChancePerTurn = 0,
        DiscoveryChancePerTurn = 0,
        MarketNoise = 0,
        Events = new GalacticEvent[0],
        MeteorChance = 0,
        EpidemicChance = 0,
        StartMoney = 100_000,
    };

    [Fact]
    public void PlayerStartsKnowingOnlyTheNeighbourhood()
    {
        var b = Quiet();
        var g = Game.New(b, 2);
        var start = g.PlayerPlanets.Single();
        Assert.Equal(Knowledge.Explored, start.Knowledge);
        foreach (var p in g.AllPlanets.Where(p => p != start))
        {
            double d = Geometry.Distance(p.MapX, p.MapY, start.MapX, start.MapY);
            Assert.Equal(d <= b.StartScanRadius ? Knowledge.Scanned : Knowledge.Silhouette, p.Knowledge);
        }
    }

    [Fact]
    public void KnownReserveDependsOnKnowledge()
    {
        var g = Game.New(Quiet(), 2);
        var p = g.PlanetsForSale.First();
        p.Knowledge = Knowledge.Silhouette;
        Assert.Null(g.KnownReserve(p));
        p.Knowledge = Knowledge.Scanned;
        Assert.Equal(p.EstimatedReserve, g.KnownReserve(p));
        p.Knowledge = Knowledge.Explored;
        Assert.Equal(p.Reserve, g.KnownReserve(p));
    }

    [Fact]
    public void RegistryPricesByEstimateNotByTruth()
    {
        var b = new Balance();
        var type = b.PlanetTypes[0];
        var p = new Planet(1, type, type.AverageReserve) { EstimatedInitialReserve = type.AverageReserve * 1.2 };
        Assert.Equal(type.Price * 1.2 * (1 + b.AuctionMarkup), p.ListPrice(b), 0);
    }

    [Fact]
    public void SomeRegistryEstimatesAreDuds()
    {
        var g = Game.New(new Balance(), 3);
        for (int i = 0; i < 300; i++) g.EndTurn();
        var all = g.AllPlanets.Where(p => p.InitialReserve > 0).ToList();
        int duds = all.Count(p => p.EstimatedInitialReserve > 2 * p.InitialReserve);
        Assert.InRange(duds / (double)all.Count, 0.05, 0.30);
    }

    [Fact]
    public void ExploreProbeRevealsPlanetAfterFlight()
    {
        var b = Quiet();
        var g = Game.New(b, 2);
        var target = g.PlanetsForSale.First(p => p.Knowledge != Knowledge.Explored);
        var probe = b.ProbeTypes.Last();
        int turns = g.ProbeTurns(probe, target.MapX, target.MapY, ProbeMission.Explore);

        Assert.True(g.LaunchExplore(probe, target.Id).Ok);
        Assert.False(g.LaunchExplore(probe, target.Id).Ok);
        for (int i = 1; i < turns; i++)
        {
            g.EndTurn();
            Assert.NotEqual(Knowledge.Explored, target.Knowledge);
        }
        g.EndTurn();
        Assert.Equal(Knowledge.Explored, target.Knowledge);
        Assert.Empty(g.Probes);
    }

    [Fact]
    public void CheapProbeCannotReachFarPoints()
    {
        var b = Quiet();
        var g = Game.New(b, 2);
        var cheap = b.ProbeTypes[0];
        // Угол карты дальше дальности «Москита» и от биржи, и от стартовой планеты?
        double far = new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) }
            .Select(c => g.LaunchOrigin(c.Item1, c.Item2).distance).Max();
        Assert.True(far > cheap.Range);
        var corner = new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) }
            .First(c => g.LaunchOrigin(c.Item1, c.Item2).distance > cheap.Range);
        Assert.False(g.LaunchScan(cheap, corner.Item1, corner.Item2).Ok);
        Assert.True(g.LaunchScan(b.ProbeTypes.Last(), corner.Item1, corner.Item2).Ok);
    }

    [Fact]
    public void ScanRevealsSilhouettesInRadius()
    {
        var b = Quiet();
        var g = Game.New(b, 2);
        var probe = b.ProbeTypes.Last();
        var target = g.AllPlanets.First(p => p.Knowledge == Knowledge.Silhouette);
        var inside = g.AllPlanets.Where(p => p.Knowledge == Knowledge.Silhouette
            && Geometry.Distance(p.MapX, p.MapY, target.MapX, target.MapY) <= probe.ScanRadius).ToList();

        Assert.True(g.LaunchScan(probe, target.MapX, target.MapY).Ok);
        for (int i = 0; i < 5; i++) g.EndTurn();
        Assert.All(inside, p => Assert.Equal(Knowledge.Scanned, p.Knowledge));
    }

    [Fact]
    public void BuyingRevealsPlanetAndPaysRuins()
    {
        var b = Quiet();
        var g = Game.New(b, 2);
        var p = g.PlanetsForSale.First();
        p.FoundByProbe = true; // без аукциона
        p.RuinsValue = 500;
        double money = g.Money;
        double price = p.ListPrice(b);

        Assert.True(g.BuyPlanet(p.Id).Ok);
        Assert.Equal(Knowledge.Explored, p.Knowledge);
        Assert.Equal(money - price + 500, g.Money, 6);
        Assert.True(p.RuinsLooted);
        Assert.NotEmpty(g.Notices);
    }

    [Fact]
    public void ResidentsTalkOnlyAfterExploration()
    {
        var g = Game.New(Quiet(), 2);
        var p = g.PlanetsForSale.First(x => x.Knowledge != Knowledge.Explored);
        p.Inhabited = true;
        Assert.False(g.MakeOffer(p.Id, new Offer(1.0), out _).Ok);
        Assert.Equal(g.ActionsPerTurn, g.ActionsLeft);

        p.Knowledge = Knowledge.Explored; // как после зонда
        Assert.True(g.MakeOffer(p.Id, new Offer(1.0), out _).Ok);
    }

    [Fact]
    public void FarPlanetsShipSlowerAndPayForDelivery()
    {
        var b = Quiet();
        b.ShipSpeed = 0.1;
        var g = Game.New(b, 2);
        var start = g.PlayerPlanets.Single();
        int turns = g.ShipTurns(start);
        Assert.True(turns >= 1);

        var report = g.EndTurn();
        Assert.True(report.Shipping > 0);
        Assert.Equal(0, g.Stock(Resource.Ice));
        Assert.Equal(28, g.InTransit(Resource.Ice));
        for (int i = 0; i < turns; i++) g.EndTurn();
        Assert.True(g.Stock(Resource.Ice) >= 28);
    }

    [Fact]
    public void OrdersPayMinusDeliveryAndExpireWhenUnreachable()
    {
        var b = Quiet();
        b.OrderChancePerTurn = 1;
        var g = Game.New(b, 2);
        g.EndTurn();
        var o = g.Orders.First();
        Assert.True(g.DeliveryTurns(o) >= 1);
        Assert.Equal(o.GrossPayout(g.Market, b) - g.DeliveryCost(o), g.OrderNetPayout(o), 6);
        while (g.OrderReachable(o)) g.EndTurn();
        Assert.DoesNotContain(o, g.Orders);
    }
}

public class PeopleTests
{
    static Balance Quiet() => new Balance
    {
        AccidentChance = 0,
        AiBuyChance = 0,
        AiResaleChance = 0,
        OrderChancePerTurn = 0,
        DiscoveryChancePerTurn = 0,
        MarketNoise = 0,
        Events = new GalacticEvent[0],
        MeteorChance = 0,
        EpidemicChance = 0,
        StartMoney = 100_000,
        RequestChancePerPlanet = 0,
        ShipSpeed = 100,
        ShippingCostPerDistance = 0,
    };

    /// <summary>Покупает обитаемую планету с работающей шахтой по честной цене (жители точно согласны).</summary>
    static Planet BuyInhabited(Game g)
    {
        var p = g.PlanetsForSale.First();
        p.Inhabited = true;
        p.Mine = MineState.Working;
        p.RuinsValue = 0;
        g.Balance.OfferChanceAtFullPrice = 1;
        Assert.True(g.OfferExplored(p.Id, new Offer(1.0), out bool accepted).Ok);
        Assert.True(accepted);
        return p;
    }

    [Fact]
    public void InhabitedPlanetIsNotSoldByRegistry()
    {
        var g = Game.New(Quiet(), 4);
        var p = g.PlanetsForSale.First();
        p.Inhabited = true;
        Assert.False(g.BuyPlanet(p.Id).Ok);
        Assert.Equal(Owner.Registry, p.Owner);
    }

    [Fact]
    public void FairDealGivesStartingLoyalty()
    {
        var b = Quiet();
        var g = Game.New(b, 4);
        var p = BuyInhabited(g);
        Assert.Equal(Owner.Player, p.Owner);
        Assert.Equal(b.LoyaltyAfterFairDeal, p.Loyalty);
    }

    [Fact]
    public void OfferChanceGrowsWithPrice()
    {
        var b = new Balance();
        var g = Game.New(b, 4);
        var p = g.PlanetsForSale.First();
        Assert.Equal(0.2, g.OfferChance(p, new Offer(0.8)), 6);
        Assert.Equal(0.6, g.OfferChance(p, new Offer(1.0)), 6);
        Assert.Equal(0.8, g.OfferChance(p, new Offer(1.1)), 6);
        Assert.Equal(b.SchoolDealChance, g.OfferChance(p, new Offer(1, withSchool: true)), 6);
    }

    [Fact]
    public void RefusalBlocksTalksForAWhile()
    {
        for (ulong seed = 1; seed < 50; seed++)
        {
            var g = Game.New(Quiet(), seed);
            var p = g.PlanetsForSale.First();
            p.Inhabited = true;
            Assert.True(g.OfferExplored(p.Id, new Offer(0.8), out bool accepted).Ok);
            if (accepted) continue;
            Assert.Equal(Owner.Registry, p.Owner);
            Assert.False(g.OfferExplored(p.Id, new Offer(1.1), out _).Ok);
            for (int i = 0; i < g.Balance.RefusalCooldownTurns; i++) g.EndTurn();
            Assert.True(g.OfferExplored(p.Id, new Offer(1.1), out _).Ok);
            return;
        }
        Assert.Fail("Жители ни разу не отказали при 20% шансе согласия");
    }

    [Fact]
    public void MiningAndTaxesLowerLoyaltyAndTaxPays()
    {
        var b = Quiet();
        var g = Game.New(b, 4);
        var p = BuyInhabited(g);
        g.SetTax(p.Id, TaxLevel.High);
        double money = g.Money;
        var report = g.EndTurn();
        Assert.Equal(60 - b.LoyaltyMiningDrain - 3, p.Loyalty, 6);
        Assert.Equal(b.TaxIncome[(int)TaxLevel.High], report.Taxes, 6);
    }

    [Fact]
    public void LowLoyaltyStartsStrikeThatStopsMiningAndTaxes()
    {
        var b = Quiet();
        var g = Game.New(b, 4);
        var p = BuyInhabited(g);
        p.Loyalty = b.StrikeBelow + 1;
        g.SetTax(p.Id, TaxLevel.High);
        g.EndTurn();
        Assert.True(p.OnStrike);

        double reserve = p.Reserve;
        var report = g.EndTurn();
        Assert.Equal(reserve, p.Reserve);
        Assert.Equal(0, report.Taxes);

        // Праздники поднимают лояльность, и забастовка кончается.
        while (p.OnStrike)
        {
            Assert.True(g.HoldFestival(p.Id).Ok);
            g.EndTurn();
        }
        Assert.True(p.Loyalty >= b.StrikeEndsAt);
    }

    [Fact]
    public void SchoolAddsLoyaltyEveryTurn()
    {
        var b = Quiet();
        var g = Game.New(b, 4);
        var p = BuyInhabited(g);
        Assert.True(g.BuildSchool(p.Id).Ok);
        Assert.False(g.BuildSchool(p.Id).Ok);
        g.EndTurn();
        Assert.Equal(60 - b.LoyaltyMiningDrain + b.SchoolLoyalty, p.Loyalty, 6);
    }

    [Fact]
    public void SchoolDealBuildsSchoolAndCostsMore()
    {
        var b = Quiet();
        b.SchoolDealChance = 1;
        var g = Game.New(b, 4);
        var p = g.PlanetsForSale.First();
        p.Inhabited = true;
        p.RuinsValue = 0;
        var offer = new Offer(1, withSchool: true);
        double cost = g.OfferCost(p, offer);
        double money = g.Money;
        Assert.True(g.OfferExplored(p.Id, offer, out bool accepted).Ok);
        Assert.True(accepted);
        Assert.True(p.HasSchool);
        Assert.Equal(b.LoyaltyAfterSchoolDeal, p.Loyalty);
        Assert.Equal(money - cost, g.Money, 6);
    }

    [Fact]
    public void RequestsRewardOrPunish()
    {
        var b = Quiet();
        var g = Game.New(b, 4);
        var p = BuyInhabited(g);
        p.HasSchool = true; // чтобы просили ресурс, а не школу
        b.RequestChancePerPlanet = 1;
        g.EndTurn();
        var r = g.Requests.Single();
        Assert.Equal(RequestKind.Resource, r.Kind);

        // Не выполнили — обида.
        double before = p.Loyalty;
        b.RequestChancePerPlanet = 0;
        for (int i = 0; i < b.RequestTurns; i++) g.EndTurn();
        Assert.Empty(g.Requests);
        Assert.True(p.Loyalty < before - b.RequestLoyaltyPenalty + b.RequestTurns * b.SchoolLoyalty + 1e-6);
    }
}

public class MagnateTests
{
    static Balance Quiet() => new Balance
    {
        AccidentChance = 0,
        AiBuyChance = 0,
        AiResaleChance = 0,
        OrderChancePerTurn = 0,
        DiscoveryChancePerTurn = 0,
        MarketNoise = 0,
        Events = new GalacticEvent[0],
        MeteorChance = 0,
        EpidemicChance = 0,
        StartMoney = 100_000,
        RequestChancePerPlanet = 0,
        ShipSpeed = 100,
        ShippingCostPerDistance = 0,
    };

    static Balance NoTricks()
    {
        var b = Quiet();
        foreach (var m in b.Magnates) m.TrickChance = 0;
        return b;
    }

    static Planet AuctionPlanet(Game g)
    {
        var p = g.PlanetsForSale.First(x => x.SoldAtAuction);
        p.RuinsValue = 0;
        return p;
    }

    [Fact]
    public void AuctionPlanetCannotBeBoughtWithoutBid()
    {
        var g = Game.New(NoTricks(), 6);
        var p = AuctionPlanet(g);
        Assert.False(g.BuyPlanet(p.Id).Ok);
    }

    [Fact]
    public void BidWinsWhenNoMagnateIsInterested()
    {
        var b = NoTricks();
        foreach (var m in b.Magnates) m.AuctionInterest = 0;
        var g = Game.New(b, 6);
        var p = AuctionPlanet(g);
        Assert.Equal(1, g.AuctionWinChance(p, 1.0), 6);
        double money = g.Money;
        Assert.True(g.PlaceBid(p.Id, 1.1, out bool won).Ok);
        Assert.True(won);
        Assert.Equal(Owner.Player, p.Owner);
        Assert.Equal(money - g.BidPrice(p, 1.1), g.Money, 6);
        Assert.Equal(1, g.AuctionsWon);
    }

    [Fact]
    public void LostAuctionGivesPlanetToMagnate()
    {
        var b = NoTricks();
        foreach (var m in b.Magnates)
        {
            m.AuctionInterest = 1;
            m.BidMin = 2.0;
            m.BidMax = 2.1;
        }
        var g = Game.New(b, 6);
        var p = AuctionPlanet(g);
        Assert.Equal(0, g.AuctionWinChance(p, 1.3), 6);
        double money = g.Money;
        Assert.True(g.PlaceBid(p.Id, 1.3, out bool won).Ok);
        Assert.False(won);
        Assert.Equal(Owner.Ai, p.Owner);
        Assert.True(p.MagnateId >= 0);
        Assert.Equal(money, g.Money, 6);
        Assert.Equal(2, g.ActionsLeft);
    }

    [Fact]
    public void MagnatesAreCopiedPerGame()
    {
        var b = NoTricks();
        var g = Game.New(b, 6);
        Assert.True(g.SpreadRumor(g.Magnates[0].Id).Ok);
        Assert.True(g.Magnates[0].QuietUntilTurn > 1);
        Assert.Equal(0, b.Magnates[0].QuietUntilTurn);
    }

    /// <summary>Игра, где первый магнат каждый ход подкупает, а второй молчит; замечают сразу.</summary>
    static Game BriberyGame(out Planet p)
    {
        var b = NoTricks();
        b.MagnateAttentionPlanets = 1;
        b.Magnates[0].TrickChance = 1;
        b.Magnates[0].TrickWeights = new[] { 1.0, 0, 0, 0 };
        b.OfferChanceAtFullPrice = 1;
        var g = Game.New(b, 6);
        p = g.PlanetsForSale.First(x => !x.FoundByProbe);
        p.Inhabited = true;
        p.Mine = MineState.Working;
        p.RuinsValue = 0;
        Assert.True(g.OfferExplored(p.Id, new Offer(1.0), out bool accepted).Ok);
        Assert.True(accepted);
        p.Loyalty = 35;
        return g;
    }

    [Fact]
    public void BribeTakesPlanetAfterWarning()
    {
        var g = BriberyGame(out var p);
        var warning = g.EndTurn();
        var attempt = g.Bribes.Single();
        Assert.Equal(p.Id, attempt.PlanetId);
        // Предупреждение — в окно «Важное за ход», с планетой и магнатом.
        var threat = warning.Alerts.Single(a => a.Kind == AlertKind.Threat);
        Assert.Equal(p.Id, threat.PlanetId);
        Assert.Equal(attempt.MagnateId, threat.MagnateId);
        var alerts = new List<Alert>();
        while (g.Turn <= attempt.ResolveTurn) alerts.AddRange(g.EndTurn().Alerts);
        Assert.Equal(Owner.Ai, p.Owner);
        Assert.Equal(1, g.PlanetsLostToBribes);
        Assert.Contains(alerts, a => a.Kind == AlertKind.PlanetLost && a.PlanetId == p.Id);
    }

    [Fact]
    public void CounterBribeKeepsPlanet()
    {
        var g = BriberyGame(out var p);
        g.EndTurn();
        var attempt = g.Bribes.Single();
        Assert.True(g.CounterBribe(attempt.Id).Ok);
        Assert.True(p.Loyalty >= g.Balance.BribeSuccessLoyalty);
        for (int i = 0; i < 3; i++) g.EndTurn();
        Assert.Equal(Owner.Player, p.Owner);
    }

    [Fact]
    public void RumorCrashesPlayersResourcePrice()
    {
        var b = NoTricks();
        b.MagnateAttentionPlanets = 1;
        b.Magnates[1].TrickChance = 1;
        b.Magnates[1].TrickWeights = new[] { 0, 1.0, 0, 0 };
        var g = Game.New(b, 6);
        double before = g.Market.Price(Resource.Ice);
        g.EndTurn();
        // Цена льда после слуха и одного шага восстановления — заметно ниже базовой.
        Assert.True(g.Market.Price(Resource.Ice) < before * 0.85);
    }

    [Fact]
    public void MagnatesEarnAndPayForPlanets()
    {
        var b = NoTricks();
        b.AiResaleChance = 0;
        var g = Game.New(b, 6);
        var m = g.Magnates[0];
        Assert.Equal(b.MagnateStartMoney, m.Money);
        Assert.True(g.MagnatePlanetCount(m) > 0);
        g.EndTurn();
        Assert.True(m.LastIncome > 0);

        // Нищий магнат не покупает, сколько бы ходов ни прошло.
        var poor = NoTricks();
        poor.MagnateStartMoney = -1_000_000;
        poor.AiBuyChance = 1;
        var g2 = Game.New(poor, 6);
        int before = g2.Magnates.Sum(g2.MagnatePlanetCount);
        for (int i = 0; i < 5; i++) g2.EndTurn();
        Assert.True(g2.Magnates.Sum(g2.MagnatePlanetCount) <= before);
    }

    [Fact]
    public void LosingLastPlanetToCollectorsIsBankruptcy()
    {
        var b = NoTricks();
        b.StartMoney = 0;
        b.StartDebt = 1_000_000;
        var g = Game.New(b, 6);
        g.EndTurn();
        Assert.Equal(0, g.PlayerPlanetCount);
        Assert.True(g.Bankrupt);
        double debt = g.Debt;
        int turn = g.Turn;
        g.EndTurn();
        Assert.Equal(debt, g.Debt);
        Assert.Equal(turn, g.Turn);
    }

    [Fact]
    public void CoopPlayersHaveSeparateCorporations()
    {
        var b = NoTricks();
        var g = Game.New(b, 6, players: 2);
        var players = g.Players.ToList();
        Assert.Equal(2, players.Count);
        Assert.Single(g.PlayerPlanets);
        var mine = g.PlayerPlanets.Single();

        g.SetCurrent(players[1].Id);
        Assert.Single(g.PlayerPlanets);
        var theirs = g.PlayerPlanets.Single();
        Assert.NotEqual(mine.Id, theirs.Id);
        Assert.False(g.BuildMine(mine.Id).Ok); // чужая планета
        Assert.True(g.HireManager().Ok);
        Assert.Equal(1, g.Managers);

        g.SetCurrent(players[0].Id);
        Assert.Equal(0, g.Managers);
        double money0 = g.Money;
        g.EndTurn();
        // Ход посчитан обоим: у второго добыча легла на его склад и зарплата менеджеру списана.
        Assert.Equal(2, g.Turn);
        Assert.True(g.LastReports.ContainsKey(players[1].Id));
        Assert.True(g.LastReports[players[1].Id].Salaries > 0);
        Assert.Equal(0, g.LastReports[players[0].Id].Salaries);
        g.SetCurrent(players[1].Id);
        Assert.True(g.Stock(Resource.Ice) + g.InTransit(Resource.Ice) > 0);
        Assert.Equal(g.ActionsPerTurn, g.ActionsLeft);
    }

    [Fact]
    public void MagnatesSellWithoutMovingPrices()
    {
        var b = NoTricks();
        b.MarketNoise = 0;
        b.AiResaleChance = 0;
        b.AiBuyChance = 0;
        b.StartPlanetHasMine = false; // игрок ничего не продаёт
        var g = Game.New(b, 6);
        for (int i = 0; i < 5; i++) g.EndTurn();
        foreach (var r in ResourceTypes.All) Assert.Equal(b.BasePrice(r), g.Market.Price(r), 6);
        Assert.Contains(g.Magnates, m => m.LastIncome > 0);
        Assert.True(g.Magnates.Sum(m => m.Corp.Stock.Sum()) < 1e-9);
    }

    [Fact]
    public void CartelCutsLeaderSales()
    {
        var b = NoTricks();
        b.CartelRivalry = 0;
        b.CartelChance = 1;
        b.MagnateAttentionPlanets = 1;
        b.MarketNoise = 0;
        var g = Game.New(b, 6);
        g.EndTurn();
        Assert.True(g.CartelActive);
        Assert.Equal(b.CartelPriceCut, g.CartelCut, 6);
        double quote = g.Quote(Resource.Ice, 10);
        Assert.Equal(g.Market.Quote(Resource.Ice, 10) * (1 - b.CartelPriceCut), quote, 6);
    }

    static Game ClaimGame(out Planet target)
    {
        var b = NoTricks();
        b.MagnateAttentionPlanets = 1;
        b.MagnateStartMoney = 1_000_000;
        b.Magnates[0].TrickChance = 1;
        b.Magnates[0].TrickWeights = new[] { 0, 0, 0, 0, 0, 1.0 };
        b.AiResaleChance = 0;
        var g = Game.New(b, 6);
        target = g.PlayerPlanets.Single();
        g.EndTurn();
        return g;
    }

    [Fact]
    public void HostileClaimTakesUndefendedPlanetForHalfPrice()
    {
        var g = ClaimGame(out var p);
        var claim = g.Claims.Single();
        Assert.Equal(p.Id, claim.PlanetId);
        double money = g.Money;
        var alerts = new List<Alert>();
        while (g.Turn <= claim.ResolveTurn) alerts.AddRange(g.EndTurn().Alerts);
        Assert.Equal(Owner.Ai, p.Owner);
        Assert.True(g.Money > money);
        Assert.Contains(alerts, a => a.Kind == AlertKind.PlanetLost && a.PlanetId == p.Id && a.MagnateId == claim.MagnateId);
    }

    [Fact]
    public void DefendedClaimKeepsPlanet()
    {
        var g = ClaimGame(out var p);
        var claim = g.Claims.Single();
        double cost = g.ClaimDefenseCost(p);
        double money = g.Money;
        Assert.True(g.DefendClaim(claim.Id).Ok);
        Assert.Equal(money - cost, g.Money, 6);
        Assert.Empty(g.Claims);
    }

    [Fact]
    public void RichMagnateAbsorbsPoorOneAndNewcomerArrives()
    {
        var b = NoTricks();
        b.MergerChance = 1;
        b.AiResaleChance = 0;
        b.AiBuyChance = 0;
        var g = Game.New(b, 6);
        g.Magnates[0].Corp.Money = 10_000_000;
        var poor = g.Magnates[2];
        int poorPlanets = g.MagnatePlanetCount(poor);
        int richPlanets = g.MagnatePlanetCount(g.Magnates[0]);
        g.EndTurn();
        Assert.DoesNotContain(poor, g.Magnates);
        Assert.Equal(3, g.Magnates.Count);
        Assert.True(g.MagnatePlanetCount(g.Magnates[0]) >= richPlanets + poorPlanets - 1);
        Assert.Equal(0, g.MagnatePlanetCount(g.Magnates[2]));
    }

    [Fact]
    public void BigSectorGetsGeneratedMagnates()
    {
        var b = NoTricks();
        b.SectorPlanets = 120;
        b.MaxSectorPlanets = 200;
        var g = Game.New(b, 6);
        Assert.Equal(12, g.Magnates.Count);
        Assert.Equal(g.Magnates.Count, g.Magnates.Select(m => m.Id).Distinct().Count());
        Assert.All(g.Magnates, m => Assert.Equal(b.MagnateStartMoney, m.Money));
        for (int i = 0; i < 20; i++) g.EndTurn();
    }

    [Fact]
    public void ScientistsArePoachedOnlyFromUnhappyPlanets()
    {
        var b = NoTricks();
        b.MagnateAttentionPlanets = 1;
        b.Magnates[0].TrickChance = 1;
        b.Magnates[0].TrickWeights = new[] { 0, 0, 0, 0, 1.0 };
        var g = Game.New(b, 6);
        var p = g.PlayerPlanets.Single();
        p.Inhabited = true;
        p.HasSchool = true;
        p.Education = 2;
        p.Specialty = Specialty.Engineers;
        p.Loyalty = 90;
        g.EndTurn();
        Assert.Equal(2, p.Education);
        p.Loyalty = 40;
        g.EndTurn();
        Assert.True(p.Education < 2);
    }

    [Fact]
    public void DirtyTricksCostReputationAndReputationMovesOfferChance()
    {
        var b = NoTricks();
        b.FineChance = 0;
        var g = Game.New(b, 6);
        var p = g.PlanetsForSale.First();
        double chance = g.OfferChance(p, new Offer(1.0));
        Assert.True(g.SpreadRumor(g.Magnates[0].Id).Ok);
        Assert.Equal(b.StartReputation + b.PlayerRumorReputation, g.Reputation, 6);
        Assert.True(g.OfferChance(p, new Offer(1.0)) < chance);
    }
}

public class EventTests
{
    static Balance Quiet() => new Balance
    {
        AccidentChance = 0,
        AiBuyChance = 0,
        AiResaleChance = 0,
        OrderChancePerTurn = 0,
        DiscoveryChancePerTurn = 0,
        MarketNoise = 0,
        StartMoney = 100_000,
        RequestChancePerPlanet = 0,
        ShipSpeed = 100,
        ShippingCostPerDistance = 0,
        MeteorChance = 0,
        EpidemicChance = 0,
        FirstEventMinTurn = 2,
        FirstEventMaxTurn = 2,
    };

    static Balance WithOnly(GalacticEvent ev)
    {
        var b = Quiet();
        b.Events = new[] { ev };
        // Магнаты торгуют на той же бирже — здесь проверяем только событие.
        b.AiStartShare = 0;
        foreach (var m in b.Magnates) m.TrickChance = 0;
        return b;
    }

    [Fact]
    public void EventStartsLastsAndEnds()
    {
        var ev = new GalacticEvent("Тест", "", 3) { PriceFactor = GalacticEvent.Only((Resource.Ice, 2.0)) };
        var g = Game.New(WithOnly(ev), 1);
        Assert.Null(g.CurrentEvent);
        g.EndTurn();
        Assert.NotNull(g.CurrentEvent);
        Assert.Equal(2, g.CurrentEvent.StartTurn);
        Assert.Equal(20, g.Market.Price(Resource.Ice), 6);
        for (int i = 0; i < 3; i++) g.EndTurn();
        Assert.Null(g.CurrentEvent);
        // После события цена плавно возвращается к базовой.
        Assert.True(g.Market.Price(Resource.Ice) > 10);
    }

    [Fact]
    public void PlanetSalePeaksDuringHype()
    {
        var ev = new GalacticEvent("Ажиотаж", "", 3) { PlanetSaleFactor = GalacticEvent.Only((Resource.Ice, 1.5)) };
        var g = Game.New(WithOnly(ev), 1);
        var p = g.PlayerPlanets.Single();
        double before = g.SalePrice(p);
        g.EndTurn();
        Assert.Equal(before * 1.5, g.SalePrice(p), 0);
    }

    [Fact]
    public void LicenseFeeHitsOnlyBigCorporations()
    {
        var ev = new GalacticEvent("Сбор", "", 3) { UpkeepFactor = 2, UpkeepFactorFromPlanets = 2 };
        var g = Game.New(WithOnly(ev), 1);
        g.EndTurn();
        Assert.Equal(1, g.EventUpkeepFactor);
        var p = g.PlanetsForSale.First(x => !x.Inhabited);
        p.FoundByProbe = true;
        Assert.True(g.BuyPlanet(p.Id).Ok);
        Assert.Equal(2, g.EventUpkeepFactor);
    }

    [Fact]
    public void MeteorDestroysMineAndInsurancePays()
    {
        var b = Quiet();
        b.Events = new GalacticEvent[0];
        b.MeteorChance = 1;
        var g = Game.New(b, 1);
        var p = g.PlayerPlanets.Single();
        g.SetInsurance(true);
        var report = g.EndTurn();
        Assert.Equal(MineState.None, p.Mine);
        Assert.Equal(p.Type.MineCost, report.InsurancePayout, 6);
    }

    [Fact]
    public void EpidemicHalvesOutput()
    {
        var b = Quiet();
        b.Events = new GalacticEvent[0];
        b.EpidemicChance = 1;
        var g = Game.New(b, 1);
        var p = g.PlayerPlanets.Single();
        p.Inhabited = true;
        double reserve = p.Reserve;
        g.EndTurn();
        Assert.Equal(reserve - p.Type.OutputPerTurn * b.EpidemicOutputShare, p.Reserve, 6);
        Assert.Equal(b.EpidemicTurns - 1, p.EpidemicTurnsLeft);
    }
}

public class DevelopmentTests
{
    static Balance Quiet()
    {
        var b = new Balance
        {
            AccidentChance = 0,
            AiBuyChance = 0,
            AiResaleChance = 0,
            OrderChancePerTurn = 0,
            DiscoveryChancePerTurn = 0,
            MarketNoise = 0,
            StartMoney = 100_000,
            RequestChancePerPlanet = 0,
            ShipSpeed = 100,
            ShippingCostPerDistance = 0,
            MeteorChance = 0,
            EpidemicChance = 0,
            Events = new GalacticEvent[0],
            RemarkChance = 0,
        };
        foreach (var m in b.Magnates) m.TrickChance = 0;
        return b;
    }

    [Fact]
    public void DepletedDepositRegeneratesAfterItsTime()
    {
        var b = Quiet();
        var g = Game.New(b, 1);
        var p = g.PlayerPlanets.Single();
        p.Reserve = p.Type.OutputPerTurn; // ровно на один ход
        g.EndTurn();
        Assert.True(p.IsRegenerating);
        int regen = b.RegenTurns[(int)Resource.Ice];
        Assert.Equal(regen, p.RegenTotal);
        for (int i = 0; i < regen - 1; i++) g.EndTurn();
        Assert.False(p.IsRegenerating);
        // Месторождение восстановилось, но беднее: 90% прежней ёмкости.
        Assert.Equal(p.InitialReserve * b.RegenCapacityShare, p.Reserve, 6);
        Assert.Equal(p.Capacity, p.Reserve, 6);
    }

    [Fact]
    public void EducatedResidentsEarnMoreWhenMineIsIdle()
    {
        var b = Quiet();
        var g = Game.New(b, 1);
        var p = g.PlayerPlanets.Single();
        Assert.False(g.Educate(p.Id, Specialty.Engineers).Ok); // необитаема

        Assert.True(g.SettleColony(p.Id).Ok);
        Assert.True(p.Inhabited);
        Assert.Equal(b.ColonyLoyalty, p.Loyalty);
        Assert.False(g.Educate(p.Id, Specialty.Engineers).Ok); // нет школы
        Assert.True(g.BuildSchool(p.Id).Ok);
        g.EndTurn();
        Assert.False(g.Educate(p.Id).Ok); // на первом уровне нужна специальность
        Assert.True(g.Educate(p.Id, Specialty.Engineers).Ok);
        Assert.True(p.IsStudying);
        for (int i = 0; i < b.EducationTurns[1]; i++) g.EndTurn();
        Assert.Equal(1, p.Education);
        Assert.True(p.NaturalSpecialty); // лёд → технари

        double mining = g.IntellectIncome(p);
        double expected = b.IntellectIncome[1] * (1 + b.NaturalSpecialtyBonus) * b.IntellectWhileMining * (1 + (p.Loyalty - 50) * b.IntellectPerLoyalty);
        Assert.Equal(expected, mining, 6);
        // Месторождение выработано — умом заняты все жители.
        p.Reserve = 0;
        Assert.Equal(mining / b.IntellectWhileMining, g.IntellectIncome(p), 6);
        // Бонус технарей: аварии реже.
        Assert.Equal(b.SpecialtyBonusPerLevel[(int)Specialty.Engineers], g.SpecialtyBonus(Specialty.Engineers), 6);

        double money = g.Money;
        var report = g.EndTurn();
        Assert.True(report.Intellect > 0);
        Assert.Contains(g.Deeds, d => d.Kind == DeedKind.Education);
        Assert.Contains(g.Deeds, d => d.Kind == DeedKind.Colony);
    }

    [Fact]
    public void PhysicistsMakeResearchCheaper()
    {
        var b = Quiet();
        var g = Game.New(b, 1);
        var p = g.PlayerPlanets.Single();
        p.Inhabited = true;
        p.Loyalty = 80;
        p.HasSchool = true;
        p.Education = 2;
        p.Specialty = Specialty.Physicists;
        var tech = b.Technologies.First(t => t.Id == "metal");
        Assert.Equal(System.Math.Round(tech.Cost * (1 - 2 * b.SpecialtyBonusPerLevel[(int)Specialty.Physicists])), g.ResearchCost(tech));
        double before = g.Money;
        Assert.True(g.StartResearch("metal").Ok);
        Assert.Equal(g.ResearchCost(tech), before - g.Money, 6);
    }

    [Fact]
    public void ExtraDepositNeedsTechAndExtractor()
    {
        var b = Quiet();
        var g = Game.New(b, 1);
        var p = g.PlayerPlanets.Single();
        p.ExtraDeposits.Add(new Deposit { Resource = Resource.Metal, OutputPerTurn = 5, Capacity = 50, Reserve = 50 });

        Assert.False(g.BuildExtractor(p.Id, 0).Ok);
        Assert.True(g.StartResearch("metal").Ok);
        Assert.False(g.StartResearch("gas").Ok); // одно исследование за раз
        for (int i = 0; i < 3; i++) g.EndTurn();
        Assert.True(g.IsResearched("metal"));
        Assert.True(g.BuildExtractor(p.Id, 0).Ok);
        double before = g.Stock(Resource.Metal);
        g.EndTurn();
        Assert.Equal(before + 5, g.Stock(Resource.Metal), 6);
    }

    [Fact]
    public void TechTreeRespectsRequirements()
    {
        var g = Game.New(Quiet(), 1);
        Assert.False(g.StartResearch("biomass").Ok);
        Assert.True(g.StartResearch("ice").Ok);
    }

    [Fact]
    public void ExtraDepositsAddToPlanetValue()
    {
        var b = Quiet();
        var g = Game.New(b, 1);
        var p = g.PlayerPlanets.Single();
        double before = p.MarketValue(b);
        p.ExtraDeposits.Add(new Deposit { Resource = Resource.Gas, OutputPerTurn = 8, Capacity = 80, Reserve = 80, HasExtractor = true });
        Assert.True(p.MarketValue(b) > before);
    }

    [Fact]
    public void MagnatesRememberDeedsInCharacter()
    {
        var b = Quiet();
        b.RemarkChance = 1;
        var g = Game.New(b, 1);
        var p = g.PlayerPlanets.Single();
        p.Inhabited = true;
        Assert.True(g.SetTax(p.Id, TaxLevel.High).Ok);
        Assert.Equal(DeedKind.HighTax, g.Deeds.Single().Kind);
        Assert.True(g.Kindness < 0);
        var report = g.EndTurn();
        var greedy = g.Magnates.First(m => m.Personality == Personality.Greedy);
        var line = report.Events.First(e => e.StartsWith(greedy.Name));
        Assert.Contains("музыка для моих ушей", line);
        Assert.Contains(p.Type.Name, line);
    }

    [Fact]
    public void CautiousMagnateSparesKindPlayer()
    {
        var b = Quiet();
        b.MagnateAttentionPlanets = 1;
        var g = Game.New(b, 1);
        var p = g.PlayerPlanets.Single();
        p.Inhabited = true;
        // Два хода заботы: праздник, школа, праздник, праздник — доброта 5.
        Assert.True(g.HoldFestival(p.Id).Ok);
        Assert.True(g.BuildSchool(p.Id).Ok);
        g.EndTurn();
        Assert.True(g.HoldFestival(p.Id).Ok);
        Assert.True(g.HoldFestival(p.Id).Ok);
        Assert.True(g.Kindness >= b.CautiousSparesKindness);

        var cautious = g.Magnates.First(m => m.Personality == Personality.Cautious);
        cautious.TrickChance = 1;
        cautious.TrickWeights = new[] { 1.0, 0, 0, 0 };
        // Ниже порога подкупа (50), но выше забастовки (30): забастовка сама испортила бы доброту.
        p.Loyalty = 35;
        g.EndTurn();
        Assert.Empty(g.Bribes);
    }
}

public class ProgressionTests
{
    static Balance Quiet() => new Balance
    {
        AccidentChance = 0,
        AiBuyChance = 0,
        AiResaleChance = 0,
        OrderChancePerTurn = 0,
        DiscoveryChancePerTurn = 0,
        MarketNoise = 0,
        Events = new GalacticEvent[0],
        MeteorChance = 0,
        EpidemicChance = 0,
    };

    [Fact]
    public void StartupHasOneManagerAndNoLongRangeProbe()
    {
        var b = Quiet();
        var g = Game.New(b, 3);
        Assert.Equal(0, g.Level);
        Assert.Equal(1, g.MaxManagers);
        Assert.True(g.HireManager(0).Ok);
        var second = g.HireManager(0);
        Assert.False(second.Ok);
        Assert.Contains("Контора", second.Error);

        var far = b.ProbeTypes.Last();
        Assert.False(g.ProbeUnlocked(far));
        var launch = g.LaunchScan(far, 0.5, 0.5);
        Assert.False(launch.Ok);
        Assert.Contains("Контора", launch.Error);
        Assert.DoesNotContain(far, g.UnlockedProbes);
    }

    [Fact]
    public void LevelGrowsWithCapitalAndNeverDrops()
    {
        var b = Quiet();
        var g = Game.New(b, 3);
        // Порог второго уровня — чуть ниже нынешнего капитала: к концу хода корпорация дорастёт.
        b.CorporationLevels[1].Capital = g.Capital - 1;
        var report = g.EndTurn();
        Assert.Equal(1, g.Level);
        Assert.Equal(2, g.MaxManagers);
        Assert.True(g.ProbeUnlocked(b.ProbeTypes.Last()));
        Assert.Contains(report.Events, e => e.Contains("«Контора»") && e.Contains("Дальнобой") && e.Contains("до 2 менеджеров"));

        // Капитал упал — уровень остаётся.
        b.CorporationLevels[1].Capital = g.Capital * 10;
        g.EndTurn();
        Assert.Equal(1, g.Level);
    }

    [Fact]
    public void RichStartBeginsHigher()
    {
        var b = Quiet();
        b.StartMoney = 40_000;
        var g = Game.New(b, 3);
        Assert.Equal(2, g.Level);
        Assert.Equal("Компания", g.LevelInfo.Name);
        Assert.Equal("Корпорация", g.NextLevel.Name);
    }

    [Fact]
    public void GeologistStartsWithSkillsAndSecondDeposit()
    {
        var b = Quiet();
        var geologist = b.Characters.Single(c => c.Name == "Геолог");
        var g = Game.New(b, 3, 1, geologist);
        var director = g.Staff.Single();
        Assert.Equal("Вы (геолог)", director.Name);
        Assert.Equal(5, director.Level(Skill.Geology));
        Assert.Equal(2, director.Level(Skill.Negotiation));
        Assert.Equal(b.StartMoney + geologist.ExtraMoney, g.Money);
        var deposit = Assert.Single(g.PlayerPlanets.Single().ExtraDeposits);
        Assert.Contains(deposit.Resource, new[] { Resource.Metal, Resource.Gas });
        // Навыки персонажа — копия: партия не меняет образец в Balance.
        director.SkillLevels[0] = 1;
        Assert.Equal(2, geologist.Skills[0]);
    }

    [Fact]
    public void SwindlerStartsRicherWithBadReputation()
    {
        var b = Quiet();
        var g = Game.New(b, 3, 1, b.Characters.Single(c => c.Name == "Аферист"));
        Assert.Equal(b.StartMoney + 800, g.Money);
        Assert.Equal(35, g.Reputation);
        Assert.Equal(5, g.Staff.Single().Level(Skill.Shady));
        Assert.False(g.PlayerPlanets.Single().Inhabited);
    }

    [Fact]
    public void PhilanthropistStartsWithResidents()
    {
        var b = Quiet();
        var g = Game.New(b, 3, 1, b.Characters.Single(c => c.Name == "Филантроп"));
        var start = g.PlayerPlanets.Single();
        Assert.True(start.Inhabited);
        Assert.Equal(b.ColonyLoyalty, start.Loyalty);
        Assert.Equal(65, g.Reputation);
        Assert.Equal(b.StartMoney - 300, g.Money);
    }

    [Fact]
    public void WithoutCharacterDirectorIsAverage()
    {
        var b = Quiet();
        var g = Game.New(b, 3);
        Assert.Null(g.Current.Character);
        Assert.All(Skills.All, s => Assert.Equal(Manager.AverageSkill, g.Staff.Single().Level(s)));
        Assert.Equal(b.StartReputation, g.Reputation);
        Assert.Empty(g.PlayerPlanets.Single().ExtraDeposits);
    }

    [Fact]
    public void LevelUnlocksAreListed()
    {
        var g = Game.New(Quiet(), 3);
        Assert.Equal(new[] { "1 менеджер", "зонд «Москит»", "зонд «Стандарт»" }, g.LevelUnlocks(0));
        Assert.Equal(new[] { "до 5 менеджеров", "лицензия на сектор «Лазурная бездна»" }, g.LevelUnlocks(4));
    }

    [Fact]
    public void LicenseNeedsLevelAndMoney()
    {
        var b = Quiet();
        var g = Game.New(b, 3);
        Assert.Contains("Компания", g.LicenseBlocker(1));
        Assert.False(g.BuyLicense(1).Ok);
        Assert.False(g.SectorOpen(1));
        Assert.DoesNotContain(g.AllPlanets, p => p.Sector != 0);

        var rich = Quiet();
        rich.StartMoney = 100_000;
        var r = Game.New(rich, 3);
        Assert.Null(r.LicenseBlocker(1));
        Assert.Equal("Лицензия уже есть", r.LicenseBlocker(0));
    }

    [Fact]
    public void LicenseOpensSectorWithItsOwnPlanetsAndMagnates()
    {
        var b = Quiet();
        b.StartMoney = 100_000;
        var g = Game.New(b, 3);
        int magnatesBefore = g.Magnates.Count;
        double money = g.Money;
        Assert.True(g.BuyLicense(1).Ok);
        Assert.True(g.HasLicense(1));
        Assert.True(g.SectorOpen(1));
        Assert.Equal(money - b.Sectors[1].LicenseCost, g.Money);

        var fire = g.AllPlanets.Where(p => p.Sector == 1).ToList();
        Assert.Equal(b.Sectors[1].Planets, fire.Count);
        // Вулканические планеты — только в «Огненном поясе», океаны — только в «Лазурной бездне».
        Assert.DoesNotContain(g.AllPlanets, p => p.Sector == 0 && (p.Resource == Resource.Plasma || p.Resource == Resource.Deuterium));
        Assert.DoesNotContain(fire, p => p.Resource == Resource.Deuterium);
        // Богаче: добыча и цена ×1,6.
        var ice = fire.FirstOrDefault(p => p.Resource == Resource.Ice);
        if (ice != null) Assert.Equal(b.TypeOf(Resource.Ice).OutputPerTurn * b.Sectors[1].Richness, ice.Type.OutputPerTurn, 6);

        var local = g.Magnates.Skip(magnatesBefore).ToList();
        Assert.NotEmpty(local);
        Assert.All(local, m => Assert.Equal(1, m.Sector));
        Assert.All(g.AllPlanets.Where(p => p.Owner == Owner.Ai && p.Sector == 1), p => Assert.Contains(p.OwnerCorp.Magnate, local));
        Assert.Contains(g.PlanetsForSale, p => p.Sector == 1);
    }

    [Fact]
    public void WithoutLicenseSectorPlanetsAreClosed()
    {
        var b = Quiet();
        b.StartMoney = 100_000;
        var g = Game.New(b, 3, 2);
        g.SetCurrent(1);
        Assert.True(g.BuyLicense(1).Ok);
        var target = g.AllPlanets.First(p => p.Sector == 1 && p.Owner == Owner.Registry && !p.Inhabited);
        g.SetCurrent(0);
        Assert.False(g.HasLicense(1));
        Assert.DoesNotContain(g.PlanetsForSale, p => p.Sector == 1);
        Assert.Contains("Огненный пояс", g.BuyPlanet(target.Id).Error);
        Assert.Contains("Огненный пояс", g.LaunchExplore(b.ProbeTypes[0], target.Id).Error);
        Assert.False(g.LaunchScan(b.ProbeTypes[0], 0.5, 0.5, 1).Ok);
    }

    [Fact]
    public void FireBeltHasTheAuthorsMagnates()
    {
        var b = Quiet();
        b.StartMoney = 100_000;
        var g = Game.New(b, 3);
        Assert.True(g.BuyLicense(1).Ok);
        var local = g.Magnates.Where(m => m.Sector == 1).ToList();
        Assert.Equal(new[] { "Нова Рики", "Орфей Младший", "Ксенон" }, local.Select(m => m.Name));
        Assert.Equal(new[] { Personality.Upstart, Personality.Heir, Personality.Sheikh }, local.Select(m => m.Personality));
        // Казна: наследство ×2, шейх ×3; планеты на старте им достаются бесплатно.
        double money = b.Sectors[1].MagnateMoney;
        Assert.Equal(money, local[0].Money);
        Assert.Equal(money * 2, local[1].Money);
        Assert.Equal(money * 3, local[2].Money);
        // Персонажи в Balance не меняются от партии.
        Assert.Equal(10, b.Sectors[1].Magnates[0].Id);
        // Шейх о чужих делах молчит.
        Assert.Null(Remarks.For(Personality.Sheikh, DeedKind.Festival));
        Assert.NotNull(Remarks.For(Personality.Upstart, DeedKind.Festival));
    }

    [Fact]
    public void ProbesFlyFromTheSectorExchange()
    {
        var b = Quiet();
        b.StartMoney = 100_000;
        var g = Game.New(b, 3);
        Assert.True(g.BuyLicense(1).Ok);
        var home = g.PlayerPlanets.Single();
        // Своих планет в «Огненном поясе» нет: зонд летит от биржи сектора, а не от стартовой планеты.
        var origin = g.LaunchOrigin(home.MapX, home.MapY, 1);
        Assert.Equal((b.ExchangeX, b.ExchangeY), (origin.x, origin.y));
        Assert.Equal(0, g.LaunchOrigin(home.MapX, home.MapY, 0).distance, 6);
        Assert.True(g.LaunchScan(b.ProbeTypes.Last(), 0.5, 0.6, 1).Ok);
        g.EndTurn();
        Assert.Contains(g.AllPlanets, p => p.Sector == 1 && p.Knowledge == Knowledge.Scanned);
    }

    [Fact]
    public void SectorTechnologyNeedsLicense()
    {
        var b = Quiet();
        b.StartMoney = 100_000;
        var g = Game.New(b, 3);
        g.Current.Researched.Add("gas");
        var plasma = b.Technologies.Single(t => t.Id == "plasma");
        Assert.False(g.CanResearch(plasma));
        Assert.Contains("Огненный пояс", g.StartResearch("plasma").Error);
        Assert.True(g.BuyLicense(1).Ok);
        Assert.True(g.CanResearch(plasma));
    }
}
