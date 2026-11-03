using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Cassandra;
using Coflnet.Sky.Api.Models.Mod;
using Newtonsoft.Json;
using Coflnet.Sky.Commands.MC;
using Microsoft.Extensions.Logging.Abstractions;
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
            "SELECT description, disable_flips, session_id, settings, updated_at FROM mod_instance_settings WHERE session_id = ? LIMIT ?",
            "INSERT INTO mod_instance_settings (disable_flips, session_id, settings, updated_at) VALUES (?, ?, ?, ?) USING TTL ?",
            "DELETE FROM mod_instance_settings WHERE session_id = ?"
        }));
        Assert.That(executed.Select(e => e.Cql), Has.None.Contains("ALLOW FILTERING"));
        Assert.That(executed[0].Values, Is.EqualTo(new object[] { "session", 1 }), "session key bound as string, single row");
        Assert.That(executed[1].Values[1], Is.TypeOf<string>().And.EqualTo("session"));
        Assert.That(executed[1].Values[4], Is.EqualTo(30 * 24 * 3600), "rows expire after 30 days");
        Assert.That(executed[2].Values, Is.EqualTo(new object[] { "session" }));
    }

    [Test]
    public void DescriptionRowRoundTrips()
    {
        var description = DescriptionSetting.Default;
        description.DisableInfoIn = new HashSet<string> { "bazaar" };
        var row = InstanceSettingsService.ToRow("session", ModSessionLifesycle.DefaultSettings, JsonConvert.SerializeObject(description));

        var restored = InstanceSettingsService.DescriptionFromRow(row);

        Assert.That(restored.DisableInfoIn, Is.EquivalentTo(new[] { "bazaar" }));
        Assert.That(InstanceSettingsService.DescriptionFromRow(new InstanceSettingsService.InstanceSettings()), Is.Null);
        Assert.That(InstanceSettingsService.DescriptionFromRow(null), Is.Null);
    }

    [Test]
    public void DescriptionOnlyRowReadsAsNotOptedIn()
    {
        var row = new InstanceSettingsService.InstanceSettings { SessionId = "s", Description = JsonConvert.SerializeObject(DescriptionSetting.Default) };

        Assert.That(InstanceSettingsService.DescriptionFromRow(row), Is.Null, "without a settings column the instance is not opted in");
    }

    [Test]
    public async Task InsertCarriesAllColumnsAndTtl()
    {
        var executed = new List<(string Cql, object[] Values)>();
        var table = InstanceSettingsService.CreateTable(RecordingSession(executed));
        var row = InstanceSettingsService.ToRow("session", ModSessionLifesycle.DefaultSettings, "{}");

        await InstanceSettingsService.InsertQuery(table, row).ExecuteAsync();

        var query = executed.Single();
        Assert.That(query.Cql, Is.EqualTo("INSERT INTO mod_instance_settings (description, disable_flips, session_id, settings, updated_at) VALUES (?, ?, ?, ?, ?) USING TTL ?"));
        Assert.That(query.Values.Last(), Is.EqualTo(30 * 24 * 3600), "all cells get the same fresh ttl");
    }

    [Test]
    public void FlipRowKeepsTheExistingDescription()
    {
        var row = InstanceSettingsService.ToRow("session", ModSessionLifesycle.DefaultSettings, "{\"kept\":true}");

        Assert.That(row.Description, Is.EqualTo("{\"kept\":true}"));
    }

    [Test]
    public async Task SaveDescriptionWithoutRowWritesNothing()
    {
        var executed = new List<(string Cql, object[] Values)>();
        var service = new InstanceSettingsService(InstanceSettingsService.CreateTable(RecordingSession(executed)), NullLogger<InstanceSettingsService>.Instance);

        var saved = await service.SaveDescription("session", DescriptionSetting.Default);

        Assert.That(saved, Is.False, "callers must not report success when nothing was stored");
        Assert.That(executed.Select(e => e.Cql), Has.None.StartsWith("INSERT").And.None.StartsWith("UPDATE"),
            "a missing row means not opted in or disabled, nothing may recreate it");
    }

    [Test]
    public async Task RefreshTtlWithoutRowWritesNothing()
    {
        var executed = new List<(string Cql, object[] Values)>();
        var service = new InstanceSettingsService(InstanceSettingsService.CreateTable(RecordingSession(executed)), NullLogger<InstanceSettingsService>.Instance);

        await service.RefreshTtl("session");

        Assert.That(executed.Select(e => e.Cql), Has.None.StartsWith("INSERT"));
    }

    [TestCase(30, true)]
    [TestCase(1, false)]
    public void OnlyRowsNotWrittenForADayAreRefreshed(int hoursSinceWrite, bool expected)
    {
        var now = DateTime.UtcNow;
        var row = InstanceSettingsService.ToRow("session", ModSessionLifesycle.DefaultSettings);
        row.UpdatedAt = now.AddHours(-hoursSinceWrite);

        Assert.That(InstanceSettingsService.NeedsRefresh(row, now), Is.EqualTo(expected));
    }

    [Test]
    public void RowsWithoutSettingsAreNeverRefreshed()
    {
        var row = new InstanceSettingsService.InstanceSettings { SessionId = "s", UpdatedAt = DateTime.UtcNow.AddDays(-20) };

        Assert.That(InstanceSettingsService.NeedsRefresh(row, DateTime.UtcNow), Is.False, "no ghost rows for ids that never opted in");
        Assert.That(InstanceSettingsService.NeedsRefresh(null, DateTime.UtcNow), Is.False);
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
