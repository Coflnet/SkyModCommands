namespace Coflnet.Sky.ModCommands.Services;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CassandraCluster = global::Cassandra.Cluster;
using SocketOptions = global::Cassandra.SocketOptions;
using SimpleStatement = global::Cassandra.SimpleStatement;
using InvalidQueryException = global::Cassandra.InvalidQueryException;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

class CassandraSmokeTests
{
    [Test]
    public void RunnerPlanPinsLifecycleTimeoutsAndExplicitTest()
    {
        var runner = Path.Combine(FindProjectDirectory(), "scripts", "cassandra-smoke.sh");
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "bash",
            ArgumentList = { runner, "--plan" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        Assert.That(process, Is.Not.Null);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.That(process.WaitForExit(5_000), Is.True, "Runner plan timed out.");
        Assert.Multiple(() =>
        {
            Assert.That(process.ExitCode, Is.Zero, error);
            Assert.That(output, Does.Contain("cassandra:4.1.7@sha256:"));
            Assert.That(output, Does.Contain("READINESS_TIMEOUT_SECONDS=180"));
            Assert.That(output, Does.Contain("TOTAL_TIMEOUT_SECONDS=420"));
            Assert.That(output, Does.Contain("RESOURCES=container,network,volume"));
            Assert.That(output, Does.Contain("CLEANUP=always"));
            Assert.That(output, Does.Contain("docker logs --tail 40"));
            Assert.That(output, Does.Contain("FullyQualifiedName=Coflnet.Sky.ModCommands.Services.CassandraSmokeTests.BlockedReasonsServerRoundTrip"));
        });
    }

    [Test]
    [Explicit("Run through scripts/cassandra-smoke.sh; it requires a disposable Cassandra container.")]
    public async Task BlockedReasonsServerRoundTrip()
    {
        var host = Environment.GetEnvironmentVariable("CASSANDRA_SMOKE_HOST")
            ?? throw new InvalidOperationException("CASSANDRA_SMOKE_HOST is required.");
        var port = int.Parse(Environment.GetEnvironmentVariable("CASSANDRA_SMOKE_PORT")
            ?? throw new InvalidOperationException("CASSANDRA_SMOKE_PORT is required."));
        using var cluster = CassandraCluster.Builder()
            .AddContactPoint(host)
            .WithPort(port)
            .WithSocketOptions(new SocketOptions().SetConnectTimeoutMillis(30_000).SetReadTimeoutMillis(30_000))
            .Build();
        using var bootstrap = cluster.Connect();
        bootstrap.Execute("CREATE KEYSPACE smoke WITH replication = {'class': 'SimpleStrategy', 'replication_factor': 1}");
        using var session = cluster.Connect("smoke");

        // 1. legacy table shape and one legacy row
        session.Execute("CREATE TABLE blocked_reasons (userid text, auctionuuid uuid, finder_type int, blocked_at timestamp, reason text, PRIMARY KEY ((userid), auctionuuid, finder_type))");
        var user = "5";
        var legacyUuid = Guid.NewGuid();
        session.Execute(new SimpleStatement(
            "INSERT INTO blocked_reasons (userid, auctionuuid, finder_type, blocked_at, reason) VALUES (?, ?, ?, ?, ?)",
            user, legacyUuid, (int)LowPricedAuction.FinderType.SNIPER_MEDIAN, DateTime.UtcNow, "legacy minProfit"));

        // 2. constructor adds the columns, second construction swallows the already exists error
        var service = new BlockedService(session, NullLogger<BlockedService>.Instance);
        try
        {
            session.Execute("ALTER TABLE blocked_reasons ADD target_price bigint");
            Assert.Fail("expected the column to exist already");
        }
        catch (InvalidQueryException e)
        {
            TestContext.Progress.WriteLine("Server message for existing column: " + e.Message);
            Assert.That(BlockedService.IsColumnExistsError(e), Is.True, e.Message);
        }
        service = new BlockedService(session, NullLogger<BlockedService>.Instance);

        // 3. archive more than one batch
        var queue = new ConcurrentQueue<MinecraftSocket.BlockedElement>();
        var recent = Enumerable.Range(0, 30).Select(i => Element(Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddMinutes(-5), 10_000_000 + i, 6_000_000, i % 2 == 0 ? "minProfit" : "blacklist x")).ToList();
        var old = Element(Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddHours(-3), 1, 1, "minProfit");
        var invalid = Element("not-a-uuid", DateTime.UtcNow.AddMinutes(-5), 1, 1, "minProfit");
        queue.Enqueue(old);
        queue.Enqueue(invalid);
        foreach (var e in recent)
            queue.Enqueue(e);
        await service.ArchiveBlockedFlipsUntil(queue, user, 0);
        Assert.That(queue, Is.Empty);
        await service.ArchiveBlockedFlipsUntil(new(new[] { Element(Guid.NewGuid().ToString("N"), DateTime.UtcNow, 7, 3, "other user") }), "6", 0);

        // 4. reads
        var all = await service.GetBlockedReasons(user, 1000);
        var legacy = all.Single(r => r.AuctionUuid == legacyUuid);
        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(31));
            Assert.That(all.Select(r => r.AuctionUuid), Does.Not.Contain(Guid.Parse(old.Flip.Auction.Uuid)));
            Assert.That(legacy.TargetPrice, Is.Zero);
            Assert.That(legacy.StartingBid, Is.Zero);
            Assert.That(legacy.FinderType, Is.EqualTo(LowPricedAuction.FinderType.SNIPER_MEDIAN));
            Assert.That(legacy.Reason, Is.EqualTo("legacy minProfit"));
        });
        var first = recent[3];
        var byAuction = (await service.GetBlockedReasons(user, Guid.Parse(first.Flip.Auction.Uuid))).Single();
        Assert.Multiple(() =>
        {
            Assert.That(byAuction.TargetPrice, Is.EqualTo(10_000_003));
            Assert.That(byAuction.StartingBid, Is.EqualTo(6_000_000));
            Assert.That(byAuction.FinderType, Is.EqualTo(LowPricedAuction.FinderType.SNIPER));
            Assert.That(byAuction.Reason, Is.EqualTo("blacklist x"));
        });
        Assert.That(await service.GetBlockedReasons(user, 10), Has.Count.EqualTo(10));
        var other = await service.GetBlockedReasons("6", 1000);
        Assert.That(other, Has.Count.EqualTo(1));
        Assert.That(other[0].UserId, Is.EqualTo("6"));
        var summary = BlockedService.Summarize(all);
        Assert.That(summary.Sum(s => s.Count), Is.EqualTo(31));

        // 5. TTL
        var ttls = session.Execute(new SimpleStatement("SELECT TTL(reason) AS ttl, auctionuuid FROM blocked_reasons WHERE userid = ?", user))
            .Where(r => r.GetValue<Guid>("auctionuuid") != legacyUuid).Select(r => r.GetValue<int>("ttl")).ToList();
        Assert.That(ttls, Has.Count.EqualTo(30));
        Assert.That(ttls, Has.All.InRange(7 * 24 * 3600 - 300, 7 * 24 * 3600));
    }

    private static MinecraftSocket.BlockedElement Element(string uuid, DateTime start, long target, long startingBid, string reason) => new()
    {
        Flip = new LowPricedAuction()
        {
            Auction = new SaveAuction() { Uuid = uuid, Start = start, StartingBid = startingBid },
            TargetPrice = target,
            Finder = LowPricedAuction.FinderType.SNIPER
        },
        Reason = reason
    };

    private static string FindProjectDirectory()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SkyModCommands.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the project source directory.");
    }
}
