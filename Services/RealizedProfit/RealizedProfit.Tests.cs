using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Commands.MC;
using AccountTier = Coflnet.Sky.Commands.Shared.AccountTier;
using AccountInfo = Coflnet.Sky.Commands.Shared.AccountInfo;
using TimeProvider = System.TimeProvider;
using Coflnet.Sky.FlipTracker.Client.Model;
using Coflnet.Sky.ModCommands.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

public class RealizedProfitEligibilityTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static RealizedProfitSnapshot Snapshot(AccountTier tier, DateTime tierExpires, DateTime accountExpires = default)
        => new("user", (int)tier, tierExpires, accountExpires, ["a"]);

    [Test]
    public void PaidExpiringInMoreThanADay_IsEligible()
        => RealizedProfitEligibility.IsEligible(Snapshot(AccountTier.PREMIUM, Now.AddDays(1).AddMinutes(1)), Now).Should().BeTrue();

    [Test]
    public void PaidExpiringExactlyInADay_IsEligible()
        => RealizedProfitEligibility.IsEligible(Snapshot(AccountTier.PREMIUM_PLUS, Now.AddDays(1)), Now).Should().BeTrue();

    [Test]
    public void PaidExpiringInLessThanADay_IsNotEligible()
        => RealizedProfitEligibility.IsEligible(Snapshot(AccountTier.PREMIUM, Now.AddHours(23)), Now).Should().BeFalse();

    [Test]
    public void ProxyOptInStarter_With15MinuteExpiry_IsNotEligible()
        => RealizedProfitEligibility.IsEligible(Snapshot(AccountTier.STARTER_PREMIUM, Now.AddMinutes(15)), Now).Should().BeFalse();

    [Test]
    public void FreeThatNeverPaid_IsEligible()
        => RealizedProfitEligibility.IsEligible(Snapshot(AccountTier.NONE, Now.AddMinutes(15)), Now).Should().BeTrue();

    [Test]
    public void FreeWhosePaidTierLapsedMoreThanADayAgo_IsEligible()
        => RealizedProfitEligibility.IsEligible(Snapshot(AccountTier.NONE, Now.AddMinutes(15), Now.AddDays(-2)), Now).Should().BeTrue();

    [Test]
    public void FreeWhosePaidTierLapsedHoursAgo_IsNotEligible()
        => RealizedProfitEligibility.IsEligible(Snapshot(AccountTier.NONE, Now.AddMinutes(15), Now.AddHours(-3)), Now).Should().BeFalse();

    [Test]
    public void FreeSessionDowngradedWhileStillPaying_IsNotEligible()
        => RealizedProfitEligibility.IsEligible(Snapshot(AccountTier.NONE, Now.AddMinutes(5), Now.AddDays(10)), Now).Should().BeFalse();
}

public class RealizedProfitSnapshotTests
{
    [Test]
    public void NotLoggedIn_ReturnsNull()
        => RealizedProfitSnapshot.Create(null, AccountTier.NONE, default, null, new SessionInfo { McUuid = "a" }).Should().BeNull();

    [Test]
    public void NoMinecraftAccount_ReturnsNull()
        => RealizedProfitSnapshot.Create("user", AccountTier.NONE, default, new AccountInfo(), new SessionInfo()).Should().BeNull();

    [Test]
    public void CollectsDistinctUuidsFromSessionAndAccount()
    {
        var info = new AccountInfo { ExpiresAt = new DateTime(2026, 1, 1), McIds = ["b", "c"] };
        var session = new SessionInfo { McUuid = "a", MinecraftUuids = ["a", "b"] };

        var snapshot = RealizedProfitSnapshot.Create("user", AccountTier.PREMIUM, new DateTime(2026, 2, 1), info, session)!;

        snapshot.McUuids.Should().BeEquivalentTo(["a", "b", "c"]);
        snapshot.Tier.Should().Be((int)AccountTier.PREMIUM);
        snapshot.AccountExpiresAt.Should().Be(new DateTime(2026, 1, 1));
    }
}

public class RealizedProfitCollectorTests
{
    private class FakeTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private class FakeSources : IRealizedProfitSources
    {
        public bool FailAh, FailBazaar, FailTasks;
        public int Calls;
        public DateTime LastFrom;
        public Task<long> GetAhProfit(IReadOnlyList<string> uuids, DateTime from, DateTime to, CancellationToken token)
            => Run(FailAh, 100, from);
        public Task<long> GetBazaarProfit(IReadOnlyList<string> uuids, DateTime from, DateTime to, CancellationToken token)
            => Run(FailBazaar, 20, from);
        public Task<long> GetTaskValue(IReadOnlyList<string> uuids, DateTime from, DateTime to, CancellationToken token)
            => Run(FailTasks, 3, from);
        private Task<long> Run(bool fail, long value, DateTime from)
        {
            Calls++;
            LastFrom = from;
            return fail ? Task.FromException<long>(new HttpRequestException("down")) : Task.FromResult(value);
        }
    }

    private class FakeStore : IRealizedProfitStore
    {
        public List<RealizedProfitRow> Rows = new();
        public Task Upsert(RealizedProfitRow row)
        {
            Rows.Add(row);
            return Task.CompletedTask;
        }
        public Task<List<RealizedProfitRow>> GetTier(int tier) => Task.FromResult(Rows.Where(r => r.Tier == tier).ToList());
    }

    private FakeTime time = null!;
    private FakeSources sources = null!;
    private FakeStore store = null!;
    private RealizedProfitCollector collector = null!;

    [SetUp]
    public void Setup()
    {
        time = new FakeTime();
        sources = new FakeSources();
        store = new FakeStore();
        collector = new RealizedProfitCollector(sources, store, time, NullLogger<RealizedProfitCollector>.Instance);
    }

    private RealizedProfitSnapshot Paid(string user = "u1")
        => new(user, (int)AccountTier.PREMIUM, time.Now.UtcDateTime.AddDays(5), default, ["a"]);

    [Test]
    public async Task EligibleUser_StoresAllSourcesForTheLastDay()
    {
        (await collector.Process(Paid())).Should().BeTrue();

        var row = store.Rows.Should().ContainSingle().Subject;
        (row.Tier, row.UserId, row.AhProfit, row.BazaarProfit, row.TaskValue).Should().Be(((int)AccountTier.PREMIUM, "u1", 100L, 20L, 3L));
        sources.LastFrom.Should().Be(time.Now.UtcDateTime.AddDays(-1));
    }

    [Test]
    public async Task IneligibleUser_IsNotQueried()
    {
        var expiringSoon = Paid() with { TierExpiresAt = time.Now.UtcDateTime.AddHours(2) };

        (await collector.Process(expiringSoon)).Should().BeFalse();

        sources.Calls.Should().Be(0);
        store.Rows.Should().BeEmpty();
    }

    [Test]
    public async Task SameUserWithinAnHour_IsThrottled()
    {
        await collector.Process(Paid());
        time.Now = time.Now.AddMinutes(59);

        (await collector.Process(Paid())).Should().BeFalse();

        store.Rows.Should().HaveCount(1);
        sources.Calls.Should().Be(3);
    }

    [Test]
    public async Task SameUserAfterAnHour_IsProcessedAgain()
    {
        await collector.Process(Paid());
        time.Now = time.Now.AddMinutes(61);

        (await collector.Process(Paid())).Should().BeTrue();

        store.Rows.Should().HaveCount(2);
    }

    [Test]
    public async Task OtherUsers_AreNotThrottledByEachOther()
    {
        await collector.Process(Paid("u1"));
        (await collector.Process(Paid("u2"))).Should().BeTrue();
    }

    [Test]
    public async Task OneFailingSource_KeepsTheOthers()
    {
        sources.FailBazaar = true;

        (await collector.Process(Paid())).Should().BeTrue();

        var row = store.Rows.Single();
        (row.AhProfit, row.BazaarProfit, row.TaskValue).Should().Be((100L, (long?)null, 3L));
    }

    [Test]
    public async Task AllSourcesFailing_StoresNothingAndAllowsRetry()
    {
        sources.FailAh = sources.FailBazaar = sources.FailTasks = true;
        (await collector.Process(Paid())).Should().BeFalse();
        store.Rows.Should().BeEmpty();

        sources.FailAh = sources.FailBazaar = sources.FailTasks = false;
        (await collector.Process(Paid())).Should().BeTrue();
    }
}

public class RealizedProfitSourcesTests
{
    private static PastFlip Flip(long purchaseAuctionId, long profit)
        => new() { PurchaseAuctionId = Guid.Parse($"00000000-0000-0000-0000-{purchaseAuctionId:D12}"), Profit = profit };

    [Test]
    public void AhProfit_CountsEachPurchaseOnceAndSkipsUnsold()
    {
        var flips = new[] { Flip(1, 500), Flip(1, 500), Flip(2, -200), Flip(3, 0) };

        RealizedProfitSources.SumAhProfit(flips).Should().Be(300);
    }

    private class StubHandler : HttpMessageHandler
    {
        public List<Uri> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"total\":1500,\"perTask\":{\"Diana\":1500}}")
            });
        }
    }

    [Test]
    public async Task TaskValue_SumsTheRealizedEndpointOfEveryAccount()
    {
        var handler = new StubHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(RealizedProfitSources.HttpClientName)).Returns(new HttpClient(handler));
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PLAYERSTATE_BASE_URL"] = "http://player-state:8000/" }).Build();
        var sources = new RealizedProfitSources(null!, null!, factory.Object, config);

        var total = await sources.GetTaskValue(["00000000000000000000000000000aa1", "00000000000000000000000000000bb2"], new DateTime(2026, 11, 1), new DateTime(2026, 11, 2), default);

        total.Should().Be(3000);
        handler.Requests.Select(r => r.AbsolutePath).Should().BeEquivalentTo(["/Task/00000000000000000000000000000aa1/realized", "/Task/00000000000000000000000000000bb2/realized"]);
        handler.Requests[0].Query.Should().Contain("from=").And.Contain("to=");
    }
}

public class RealizedProfitControllerTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static RealizedProfitRow Row(int tier, long? ah, long? bz, long? task, DateTime updated)
        => new() { Tier = tier, UserId = Guid.NewGuid().ToString(), AhProfit = ah, BazaarProfit = bz, TaskValue = task, UpdatedAt = updated };

    [Test]
    public void Aggregate_SumsPerSourceAndAveragesPerUser()
    {
        var rows = new[]
        {
            Row(2, 1000, 200, 30, Now.AddHours(-1)),
            Row(2, 3000, null, 70, Now.AddHours(-5)),
        };

        var result = RealizedProfitAggregator.Aggregate(2, "PREMIUM", rows, Now);

        (result.Users, result.AhProfit, result.BazaarProfit, result.TaskValue, result.Total)
            .Should().Be((2, 4000L, 200L, 100L, 4300L));
        result.AveragePerUser.Should().Be(2150);
        result.UsersWithMissingSources.Should().Be(1);
    }

    [Test]
    public void Aggregate_IgnoresRowsOlderThanADay()
    {
        var rows = new[] { Row(2, 1000, 0, 0, Now.AddHours(-1)), Row(2, 9999, 0, 0, Now.AddHours(-30)) };

        var result = RealizedProfitAggregator.Aggregate(2, "PREMIUM", rows, Now);

        result.Users.Should().Be(1);
        result.Total.Should().Be(1000);
    }

    [Test]
    public void Aggregate_EmptyTier_HasZeroAverage()
        => RealizedProfitAggregator.Aggregate(0, "NONE", [], Now).AveragePerUser.Should().Be(0);

    [Test]
    public async Task GetTiers_ReturnsEveryTierWithItsOwnRows()
    {
        var store = new Mock<IRealizedProfitStore>();
        store.Setup(s => s.GetTier(It.IsAny<int>())).ReturnsAsync(new List<RealizedProfitRow>());
        store.Setup(s => s.GetTier((int)AccountTier.PREMIUM))
            .ReturnsAsync(new List<RealizedProfitRow> { Row((int)AccountTier.PREMIUM, 500, 5, 1, Now) });
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(Now));
        var controller = new RealizedProfitController(new RealizedProfitTierService(store.Object, time.Object));

        var tiers = await controller.GetTiers();

        tiers.Select(t => t.TierName).Should().BeEquivalentTo(Enum.GetNames<AccountTier>());
        tiers.Single(t => t.Tier == (int)AccountTier.PREMIUM).Total.Should().Be(506);
        tiers.Single(t => t.Tier == (int)AccountTier.NONE).Users.Should().Be(0);
    }

    [Test]
    public async Task GetTiers_IsCachedForAMinute()
    {
        var store = new Mock<IRealizedProfitStore>();
        store.Setup(s => s.GetTier(It.IsAny<int>())).ReturnsAsync(new List<RealizedProfitRow>());
        var now = Now;
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(() => new DateTimeOffset(now));
        var service = new RealizedProfitTierService(store.Object, time.Object);
        var tierCount = Enum.GetValues<AccountTier>().Length;

        await service.GetTiers();
        now = Now.AddSeconds(30);
        await service.GetTiers();
        store.Verify(s => s.GetTier(It.IsAny<int>()), Times.Exactly(tierCount));

        now = Now.AddSeconds(61);
        await service.GetTiers();
        store.Verify(s => s.GetTier(It.IsAny<int>()), Times.Exactly(tierCount * 2));
    }
}

public class RealizedProfitStoreTests
{
    [Test]
    public void Construction_DoesNotResolveTheScyllaSession()
    {
        var services = new Mock<IServiceProvider>(MockBehavior.Strict);

        var store = new RealizedProfitStore(services.Object, TimeProvider.System, NullLogger<RealizedProfitStore>.Instance);
        var collector = new RealizedProfitCollector(Mock.Of<IRealizedProfitSources>(), store, TimeProvider.System, NullLogger<RealizedProfitCollector>.Instance);
        _ = new RealizedProfitBackgroundService(new RealizedProfitQueue(), collector, NullLogger<RealizedProfitBackgroundService>.Instance);

        services.Invocations.Should().BeEmpty();
    }

    [Test]
    public async Task UnavailableSession_IsLoggedAndSkipped()
    {
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(global::Cassandra.ISession))).Throws(new InvalidOperationException("scylla down"));
        var store = new RealizedProfitStore(services.Object, TimeProvider.System, NullLogger<RealizedProfitStore>.Instance);

        await store.Upsert(new RealizedProfitRow { UserId = "u", UpdatedAt = DateTime.UtcNow });
        var rows = await store.GetTier(1);

        rows.Should().BeEmpty();
        services.Verify(s => s.GetService(typeof(global::Cassandra.ISession)), Times.Once, "retry is delayed after a failure");
    }

    [Test]
    public void DbRow_StoresMissingSourcesAsZeroWithAFlag()
    {
        var updated = new DateTime(2026, 11, 2, 23, 30, 0, DateTimeKind.Utc);
        var db = RealizedProfitDbRow.From(new RealizedProfitRow { Tier = 2, UserId = "u", AhProfit = 5, BazaarProfit = null, TaskValue = 7, UpdatedAt = updated });

        db.Day.Should().Be(20261102);
        (db.AhProfit, db.BazaarProfit, db.TaskValue).Should().Be((5L, 0L, 7L));
        db.Missing.Should().Be(RealizedProfitDbRow.BazaarMissing);
        var back = db.ToRow();
        (back.AhProfit, back.BazaarProfit, back.TaskValue).Should().Be((5L, (long?)null, 7L));
    }

    [Test]
    public void Latest_KeepsNewestRowPerUserAndDropsOlderThanADay()
    {
        var now = new DateTime(2026, 11, 2, 1, 0, 0, DateTimeKind.Utc);
        RealizedProfitDbRow Db(string user, long ah, DateTime at) => RealizedProfitDbRow.From(new RealizedProfitRow { UserId = user, AhProfit = ah, BazaarProfit = 0, TaskValue = 0, UpdatedAt = at });
        var rows = new[]
        {
            Db("a", 1, now.AddHours(-20)), Db("a", 2, now.AddHours(-1)), // yesterday and today
            Db("b", 3, now.AddHours(-25)),
        };

        var result = RealizedProfitStore.Latest(rows, now - TimeSpan.FromDays(1));

        result.Should().ContainSingle().Which.AhProfit.Should().Be(2);
    }
}

public class RealizedProfitSourcesLimitTests
{
    [Test]
    public async Task AhProfit_CapsAccountsSkipsMalformedAndLimitsConcurrency()
    {
        var inFlight = 0;
        var peak = 0;
        var calls = 0;
        var tracker = new Mock<Coflnet.Sky.FlipTracker.Client.Api.ITrackerApi>();
        tracker.Setup(t => t.GetFlipsOfPlayerAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var now = Interlocked.Increment(ref inFlight);
                Interlocked.Increment(ref calls);
                int seen;
                while (now > (seen = peak))
                    Interlocked.CompareExchange(ref peak, now, seen);
                await Task.Delay(20);
                Interlocked.Decrement(ref inFlight);
                return new List<PastFlip>();
            });
        var sources = new RealizedProfitSources(tracker.Object, null!, null!, new ConfigurationBuilder().Build());
        var ids = Enumerable.Range(0, 60).Select(i => Guid.NewGuid().ToString()).Append("not-a-guid").Prepend("also bad").ToList();

        var total = await sources.GetAhProfit(ids, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, default);

        total.Should().Be(0);
        calls.Should().Be(RealizedProfitSources.MaxAccountsPerUser);
        peak.Should().BeLessThanOrEqualTo(RealizedProfitSources.MaxConcurrentCalls);
    }
}
