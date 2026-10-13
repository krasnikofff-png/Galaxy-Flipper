using PlanetTrader.Core;
using Xunit;

namespace PlanetTrader.Core.Tests;

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
        Assert.Equal(expected, new Balance { UpkeepShelfEnd = 16 }.UpkeepMultiplier(planets), 2);
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

        Assert.Equal(10 * 0.9, m.Price(Resource.Ice), 6);
        // Средняя цена между «до» и «после».
        Assert.Equal(150 * 10 * 0.95, revenue, 6);
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
    };

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
        Assert.Equal(840 - 28, start.Reserve);
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

        for (int i = 0; i < 3; i++) Assert.True(g.LaunchProbe(out _).Ok);
        Assert.False(g.LaunchProbe(out _).Ok);

        g.EndTurn();
        Assert.Equal(3, g.ActionsLeft);
    }

    [Fact]
    public void ManagerAddsActionNextTurnAndCostsSalary()
    {
        var g = Game.New(Calm(), 1);
        Assert.True(g.HireManager().Ok);
        var report = g.EndTurn();
        Assert.Equal(60, report.Salaries);
        Assert.Equal(4, g.ActionsLeft);
    }

    [Fact]
    public void BoughtPlanetNeedsMineThatTakesTwoTurns()
    {
        var b = Calm();
        b.StartMoney = 100_000;
        var g = Game.New(b, 3);
        var p = g.PlanetsForSale.First();
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
        while (g.Money >= g.Balance.ProbeCost && g.LaunchProbe(out _).Ok) { }

        for (int i = 0; i < 50 && g.TotalSeizures == 0; i++) g.EndTurn();

        Assert.Equal(1, g.TotalSeizures);
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
            var order = g.Orders.FirstOrDefault(o => g.Stock(o.Resource) >= o.Amount);
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
        var b = new Balance { AiBuyChance = 0, AiResaleChance = 0, AccidentChance = 0 };
        var g = Game.New(b, 9);
        int before = g.AllPlanets.Count;
        for (int i = 0; i < 700; i++) g.EndTurn();
        int found = g.AllPlanets.Count - before;
        // Ожидаем ~100 открытий за 700 ходов.
        Assert.InRange(found, 70, 130);
        Assert.All(g.AllPlanets.Skip(before), p => Assert.False(p.FoundByProbe));
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
