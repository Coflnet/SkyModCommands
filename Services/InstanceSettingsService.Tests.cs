using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Coflnet.Sky.Commands.MC;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class InstanceSettingsServiceTests
{
    [Test]
    public void RowRoundTripKeepsDisableFlips()
    {
        var settings = ModSessionLifesycle.DefaultSettings;
        settings.DisableFlips = true;
        settings.MinProfit = 4242;

        var restored = InstanceSettingsService.FromRow(InstanceSettingsService.ToRow("session", settings));

        Assert.That(restored.DisableFlips, Is.True, "DisableFlips is outside the settings data contract and has to survive storage");
        Assert.That(restored.MinProfit, Is.EqualTo(4242));
    }

    [Test]
    public async Task QueriesLookUpBySessionKeyWithoutFiltering()
    {
        var executed = new List<(string Cql, object[] Values)>();
        var table = InstanceSettingsService.CreateTable(RecordingSession(executed));
        var row = InstanceSettingsService.ToRow("session", ModSessionLifesycle.DefaultSettings);

        await InstanceSettingsService.SelectQuery(table, "session").ExecuteAsync();
        await InstanceSettingsService.InsertQuery(table, row).ExecuteAsync();
        await InstanceSettingsService.DeleteQuery(table, "session").ExecuteAsync();

        Assert.That(executed.Select(e => e.Cql), Is.EqualTo(new[]
        {
            "SELECT disable_flips, session_id, settings, updated_at FROM mod_instance_settings WHERE session_id = ? LIMIT ?",
            "INSERT INTO mod_instance_settings (disable_flips, session_id, settings, updated_at) VALUES (?, ?, ?, ?) USING TTL ?",
            "DELETE FROM mod_instance_settings WHERE session_id = ?"
        }));
        Assert.That(executed.Select(e => e.Cql), Has.None.Contains("ALLOW FILTERING"));
        Assert.That(executed[0].Values, Is.EqualTo(new object[] { "session", 1 }), "session key bound as string, single row");
        Assert.That(executed[1].Values[1], Is.TypeOf<string>().And.EqualTo("session"));
        Assert.That(executed[1].Values[4], Is.EqualTo(365 * 24 * 3600), "rows expire after a year");
        Assert.That(executed[2].Values, Is.EqualTo(new object[] { "session" }));
    }

    /// <summary>
    /// Session that records every prepared query with the values bound to it, returning no rows
    /// </summary>
    private static ISession RecordingSession(List<(string Cql, object[] Values)> executed)
    {
        var cluster = new Mock<ICluster>();
        cluster.Setup(c => c.Configuration).Returns(Cluster.Builder().AddContactPoint("127.0.0.1").GetConfiguration());
        var session = new Mock<ISession>();
        session.Setup(s => s.Cluster).Returns(cluster.Object);
        session.Setup(s => s.PrepareAsync(It.IsAny<string>())).ReturnsAsync((string cql) => Prepared(cql, executed));
        session.Setup(s => s.ExecuteAsync(It.IsAny<IStatement>())).ReturnsAsync(new RowSet());
        session.Setup(s => s.ExecuteAsync(It.IsAny<IStatement>(), It.IsAny<string>())).ReturnsAsync(new RowSet());
        return session.Object;
    }

    private static PreparedStatement Prepared(string cql, List<(string Cql, object[] Values)> executed)
    {
        var prepared = new Mock<PreparedStatement>();
        prepared.Setup(p => p.Bind(It.IsAny<object[]>())).Returns((object[] values) =>
        {
            executed.Add((cql, values));
            return new BoundStatement();
        });
        return prepared.Object;
    }
}
