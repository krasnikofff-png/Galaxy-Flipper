using System.Linq;
using PlanetTrader.Core;
using Xunit;

/// <summary>Шоу «Риелтор года», случаи и «Лот недели».</summary>
public class ShowTests
{
    /// <summary>Тихая партия: без событий, аварий и пакостей; шоу, случаи и лоты — как задано в тесте.</summary>
    static Balance Quiet() => new Balance
    {
        AccidentChance = 0, AiBuyChance = 0, AiResaleChance = 0, OrderChancePerTurn = 0, DiscoveryChancePerTurn = 0,
        MarketNoise = 0, Events = new GalacticEvent[0], MeteorChance = 0, EpidemicChance = 0,
        IncidentGapMax = 0, LotGapMax = 0, MagnateTaskChance = 0,
    };

    static ShowTaskSpec Buy1 => new ShowTaskSpec { Kind = ShowTaskKind.BuyPlanets, Title = "Скупщик", Text = "Купить {0}", Target = 1, Points = 15 };

    [Fact]
    public void NewGameStartsFirstSeasonWithTasksAndRivals()
    {
        var g = Game.New(new Balance(), 3);
        var s = g.Season;
        Assert.Equal(1, s.Number);
        Assert.Equal(g.Balance.SeasonTasks, s.Tasks.Count);
        Assert.Equal(s.Tasks.Count, s.Tasks.Select(t => t.Spec.Kind).Distinct().Count());
        Assert.Contains(s.Entries, e => e.IsPlayer);
        Assert.InRange(s.Entries.Count(e => !e.IsPlayer), 1, g.Balance.ShowRivals);
        Assert.All(s.Tasks, t => Assert.False(string.IsNullOrEmpty(Game.TaskText(t))));
    }

    [Fact]
    public void DoneTaskGivesPointsAndPopUp()
    {
        var b = Quiet();
        b.ShowTasks = new[] { Buy1 };
        b.SeasonTasks = 1;
        var g = Game.New(b, 5);
        g.Current.Money += 50000;
        // Планету, найденную своим зондом, реестр продаёт без аукциона.
        var p = g.PlanetsForSale.First(x => !x.Inhabited && !x.IsDepleted);
        p.FoundByProbe = true;
        Assert.True(g.BuyPlanet(p.Id).Ok);
        Assert.Equal(1, g.Current.Stats.PlanetsBought);
        var report = g.EndTurn();
        var me = g.Season.Entries.First(e => e.IsPlayer);
        Assert.Equal(15, me.Points);
        Assert.Contains(report.Alerts, a => a.Kind == AlertKind.Show && a.Title.Contains("Скупщик"));
    }

    [Fact]
    public void SeasonEndsWithCupPrizeAndNextSeason()
    {
        var b = Quiet();
        b.ShowTasks = new[] { new ShowTaskSpec { Kind = ShowTaskKind.SellOnExchange, Title = "Биржевик", Text = "Продать на {0}", Target = 1, Points = 10 } };
        b.SeasonTasks = 1;
        b.SeasonTurns = 3;
        var g = Game.New(b, 7);
        g.Current.Stock[(int)Resource.Ice] = 10;
        Assert.True(g.SellResource(Resource.Ice, 10).Ok);
        double money = g.Money;
        var alerts = Enumerable.Range(0, 3).SelectMany(_ => g.EndTurn().Alerts).ToList();
        Assert.Equal(1, g.Current.Cups);
        Assert.Contains(alerts, a => a.Kind == AlertKind.Show && a.Title.Contains("Кубок"));
        Assert.Contains(alerts, a => a.Kind == AlertKind.Show && a.Title.Contains("Сезон 2"));
        Assert.Equal(2, g.Season.Number);
        Assert.Equal(new[] { 1 }, g.Current.SeasonPlaces);
        Assert.True(g.Money > money - 3 * g.CostsPerTurn + b.ShowPrize[0] - 1);
    }

    [Fact]
    public void IncidentWaitsForAnswerAndSilenceChoosesTheLastOption()
    {
        var b = Quiet();
        b.IncidentFirstTurn = 1;
        b.IncidentGapMin = b.IncidentGapMax = 1;
        b.Incidents = new[]
        {
            new Incident
            {
                Id = "gift", Title = "Подарок", Text = "Вам дарят деньги.", Target = IncidentTarget.None,
                Choices = new[]
                {
                    new IncidentChoice { Label = "Взять", Success = new IncidentOutcome { Money = 100, Text = "Взяли." } },
                    new IncidentChoice { Label = "Отказаться", Success = new IncidentOutcome { Reputation = 1, Text = "Отказались." } },
                },
            },
            new Incident
            {
                Id = "fee", Title = "Взнос", Text = "Взнос в клуб.", Target = IncidentTarget.None,
                Choices = new[] { new IncidentChoice { Label = "Заплатить", Cost = 50, Success = new IncidentOutcome { Text = "Заплатили." } } },
            },
        };
        var g = Game.New(b, 9);
        g.EndTurn();
        var a = g.Incident;
        Assert.NotNull(a);
        if (a.Def.Id == "gift")
        {
            double money = g.Money;
            Assert.True(g.ResolveIncident(0, out var result).Ok);
            Assert.Equal("Взяли.", result);
            Assert.Equal(money + 100, g.Money);
            Assert.Null(g.Incident);
            g.EndTurn();
            a = g.Incident;
        }
        // Следующий случай — другой (подряд один и тот же не приходит). Промолчим: выбран последний вариант.
        Assert.Equal("fee", a.Def.Id);
        var report = g.EndTurn();
        Assert.Contains(report.Events, e => e.Contains("вы промолчали") && e.Contains("Заплатили."));
    }

    static Game LotGame(double interest, out Planet lot)
    {
        var b = Quiet();
        b.LotFirstTurn = 2;
        b.LotGapMin = b.LotGapMax = 50;
        b.LotNoticeTurns = 2;
        b.LotInterestFactor = interest;
        b.LotBudgetShare = 100;
        b.MagnateStartMoney = 1_000_000;
        var g = Game.New(b, 11);
        g.EndTurn();
        Assert.NotNull(g.Lot);
        lot = g.GetPlanet(g.Lot.PlanetId);
        return g;
    }

    /// <summary>Доиграть до торгов; отчёт хода, в конце которого они прошли.</summary>
    static TurnReport UntilSold(Game g)
    {
        TurnReport report = null;
        for (int i = 0; i < 10 && g.Lot != null; i++) report = g.EndTurn();
        Assert.Null(g.Lot);
        return report;
    }

    [Fact]
    public void LotIsSoldOnlyAtItsAuctionToTheHighestBid()
    {
        var g = LotGame(0, out var p);
        Assert.NotNull(p.Legend);
        Assert.True(g.IsLot(p));
        Assert.DoesNotContain(g.PlanetsForSale, x => x.Id == p.Id);
        Assert.False(g.BuyPlanet(p.Id).Ok);
        g.Current.Money += g.LotBidPrice(2.0) + 10000;
        Assert.True(g.PlaceLotBid(1.0).Ok);
        Assert.False(g.PlaceLotBid(1.0).Ok);
        Assert.True(g.PlaceLotBid(1.25).Ok);
        Assert.Equal(g.LotBidPrice(1.25), g.MyLotBid.Amount);
        var report = UntilSold(g);
        Assert.True(g.IsMine(p));
        Assert.Null(g.Lot);
        Assert.Equal(1, g.Current.Stats.LotsWon);
        Assert.Contains(report.Alerts, a => a.Title.Contains("Лот недели ваш"));
    }

    [Fact]
    public void RichMagnatesTakeTheLotIfNobodyOutbids()
    {
        var g = LotGame(10, out var p);
        Assert.NotEmpty(g.Lot.Interested);
        UntilSold(g);
        Assert.Equal(Owner.Ai, p.Owner);
    }

    [Fact]
    public void BidWithoutMoneyBurns()
    {
        var g = LotGame(0, out var p);
        g.Current.Money += g.LotBidPrice(1.0);
        Assert.True(g.PlaceLotBid(1.0).Ok);
        double rep = g.Reputation;
        var report = g.EndTurn();
        g.Current.Money = 0;
        report = UntilSold(g);
        Assert.False(g.IsMine(p));
        Assert.True(g.Reputation < rep);
        Assert.Contains(report.Alerts, a => a.Title == "Ставка сгорела");
    }
}
