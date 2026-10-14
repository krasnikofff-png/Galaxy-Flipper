using System.Linq;
using PlanetTrader.Core;
using PlanetTrader.Sim;
using Xunit;

/// <summary>Сохранения: загруженная партия — та же самая и продолжается в точности так же.</summary>
public class SaveTests
{
    /// <summary>Партия, которую бот симулятора поиграл: планеты, зонды, исследования, жители, магнаты, секторы.</summary>
    static Game Played(Balance b, int turns, int players = 1, ulong seed = 11)
    {
        var g = Game.New(b, seed, players, b.Characters.First());
        var bots = Enumerable.Range(0, players).Select(i => new Bot(Strategy.Active, seed + (ulong)i)).ToArray();
        PlayOn(g, bots, turns);
        return g;
    }

    static void PlayOn(Game g, Bot[] bots, int turns)
    {
        for (int t = 0; t < turns; t++)
        {
            for (int i = 0; i < bots.Length; i++)
            {
                g.SetCurrent(i);
                bots[i].PlayTurn(g);
            }
            g.SetCurrent(0);
            g.EndTurn();
        }
    }

    [Fact]
    public void LoadedGameSavesTheSame()
    {
        var b = new Balance();
        var g = Played(b, 150);
        string first = g.Save();
        var loaded = Game.Load(first, b);
        Assert.Equal(first, loaded.Save());
        Assert.Equal(g.Turn, loaded.Turn);
        Assert.Equal(g.Money, loaded.Money);
        Assert.Equal(g.AllPlanets.Count, loaded.AllPlanets.Count);
        Assert.Equal(g.Seed, loaded.Seed);
    }

    [Fact]
    public void LoadedGameContinuesExactlyTheSame()
    {
        var b = new Balance();
        var g = Played(b, 120);
        var loaded = Game.Load(g.Save(), b);
        PlayOn(g, new[] { new Bot(Strategy.Active, 5) }, 80);
        PlayOn(loaded, new[] { new Bot(Strategy.Active, 5) }, 80);
        Assert.Equal(g.Save(), loaded.Save());
        Assert.Equal(g.Money, loaded.Money);
    }

    [Fact]
    public void CoopGameSurvivesSaving()
    {
        var b = new Balance { SectorPlanets = 90, MaxSectorPlanets = 150 };
        var g = Played(b, 60, players: 3);
        var loaded = Game.Load(g.Save(), b);
        PlayOn(g, Enumerable.Range(0, 3).Select(i => new Bot(Strategy.Active, 30 + (ulong)i)).ToArray(), 40);
        PlayOn(loaded, Enumerable.Range(0, 3).Select(i => new Bot(Strategy.Active, 30 + (ulong)i)).ToArray(), 40);
        Assert.Equal(g.Save(), loaded.Save());
    }

    [Fact]
    public void SharedObjectsStayShared()
    {
        var b = new Balance();
        var g = Game.Load(Played(b, 100).Save(), b);
        // Корпорация планеты — та же, что в списке корпораций; тип планеты — из баланса; магнат и его корпорация знают друг друга.
        foreach (var p in g.AllPlanets.Where(p => p.OwnerCorp != null))
            Assert.Contains(g.Corporations, c => ReferenceEquals(c, p.OwnerCorp));
        foreach (var p in g.AllPlanets)
            Assert.Contains(g.SectorTypes(p.Sector), t => ReferenceEquals(t, p.Type));
        foreach (var c in g.Corporations.Where(c => c.Magnate != null))
            Assert.Same(c, c.Magnate.Corp);
    }

    [Fact]
    public void OldSaveWithoutNewFieldsStillLoads()
    {
        var b = new Balance();
        var g = Played(b, 40);
        // Как будто сохранение — из старой версии: в нём нет склада корпораций и списка лицензий.
        string json = g.Save().Replace("\"Licenses\":[0],", "");
        var loaded = Game.Load(json, b);
        Assert.NotNull(loaded.Corporations[0].Licenses);
        loaded.EndTurn();
    }

    [Fact]
    public void BrokenSaveGivesClearError()
    {
        var e = Assert.Throws<System.FormatException>(() => Game.Load("{\"version\":1,\"game\":{\"$id\":1,", new Balance()));
        Assert.Contains("Сохранение повреждено", e.Message);
    }
}
