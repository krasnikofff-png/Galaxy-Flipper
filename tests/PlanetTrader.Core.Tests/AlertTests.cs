using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using PlanetTrader.Sim;
using Xunit;

/// <summary>Окно «Важное за ход»: что туда попадает.</summary>
public class AlertTests
{
    [Fact]
    public void OnlyThePlayersOwnTroublesAreAlerts()
    {
        var b = new Balance();
        var g = Game.New(b, 21, 1, b.Characters[0]);
        var bot = new Bot(Strategy.Active, 21);
        var all = new List<Alert>();
        for (int t = 0; t < 300; t++)
        {
            bot.PlayTurn(g);
            var report = g.EndTurn();
            foreach (var a in report.Alerts)
            {
                // Строка окна есть и в журнале.
                Assert.Contains(a.Text, report.Events);
                Assert.False(string.IsNullOrEmpty(a.Title));
                // Беды магнатов игроку окном не показывают: метеорит, бунт и забастовка — только на своих планетах.
                if (a.Kind == AlertKind.Disaster) Assert.True(g.IsMine(g.GetPlanet(a.PlanetId)));
                if (a.MagnateId >= 0) Assert.NotNull(g.GetMagnate(a.MagnateId));
            }
            all.AddRange(report.Alerts);
        }
        // За длинную партию магнаты успевают напакостить, а корпорация — вырасти.
        Assert.Contains(all, a => a.Kind == AlertKind.Trick || a.Kind == AlertKind.Threat);
        Assert.Contains(all, a => a.Kind == AlertKind.Good);
    }
}
