using System;
using AwesomeAssertions;
using Coflnet.Sky.Commands.Shared;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;

namespace Coflnet.Sky.ModCommands.Services;

public class RedisFlipMatchTrackerTests
{
    private Mock<IConnectionMultiplexer> redis;
    private Mock<ISubscriber> subscriber;
    private Action<RedisChannel, RedisValue> handler;

    [SetUp]
    public void Setup()
    {
        subscriber = new Mock<ISubscriber>();
        subscriber.Setup(s => s.Subscribe(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((_, h, _) => handler = h);
        redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(subscriber.Object);
    }

    private RedisFlipMatchTracker Create(IConnectionMultiplexer connection) =>
        new(connection, NullLogger<RedisFlipMatchTracker>.Instance, "me");

    [Test]
    public void NullRedisRecordsLocally()
    {
        var tracker = Create(null);
        tracker.ReportMatch(5, AccountTier.PREMIUM_PLUS);
        tracker.HighestMatchedTier(5).Should().Be(AccountTier.PREMIUM_PLUS);
    }

    [Test]
    public void PublishesPaidTierMatch()
    {
        var tracker = Create(redis.Object);
        tracker.ReportMatch(7, AccountTier.PREMIUM_PLUS);

        subscriber.Verify(s => s.Publish(
            It.Is<RedisChannel>(c => c == RedisChannel.Literal("flip_matched")),
            It.Is<RedisValue>(v => IsMatch(v, 7, AccountTier.PREMIUM_PLUS, "me")),
            CommandFlags.FireAndForget), Times.Once);
        tracker.HighestMatchedTier(7).Should().Be(AccountTier.PREMIUM_PLUS);
    }

    [Test]
    public void RepeatedTierIsNotPublishedAgainButHigherIs()
    {
        var tracker = Create(redis.Object);
        tracker.ReportMatch(7, AccountTier.PREMIUM).Should().BeTrue();
        tracker.ReportMatch(7, AccountTier.PREMIUM).Should().BeFalse();
        subscriber.Verify(s => s.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), CommandFlags.FireAndForget), Times.Once);

        tracker.ReportMatch(7, AccountTier.PREMIUM_PLUS).Should().BeTrue();
        subscriber.Verify(s => s.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), CommandFlags.FireAndForget), Times.Exactly(2));
    }

    [Test]
    public void DoesNotPublishFreeTier()
    {
        var tracker = Create(redis.Object);
        tracker.ReportMatch(7, AccountTier.NONE);

        subscriber.Verify(s => s.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()), Times.Never);
        tracker.HighestMatchedTier(7).Should().Be(AccountTier.NONE);
    }

    [Test]
    public void RemoteMessageFromOtherServerIsRecorded()
    {
        var tracker = Create(redis.Object);
        handler(RedisChannel.Literal("flip_matched"), Serialize(9, AccountTier.PREMIUM_PLUS, "other"));
        tracker.HighestMatchedTier(9).Should().Be(AccountTier.PREMIUM_PLUS);
    }

    [Test]
    public void RemoteMessageFromOwnServerIsIgnored()
    {
        var tracker = Create(redis.Object);
        handler(RedisChannel.Literal("flip_matched"), Serialize(9, AccountTier.PREMIUM_PLUS, "me"));
        tracker.HighestMatchedTier(9).Should().Be(AccountTier.NONE);
    }

    [Test]
    public void GarbageMessageIsSwallowed()
    {
        var tracker = Create(redis.Object);
        Action act = () => handler(RedisChannel.Literal("flip_matched"), (byte[])[1, 2, 3]);
        act.Should().NotThrow();
    }

    [Test]
    public void SameSubscriberIsOnlySubscribedOnce()
    {
        var tracker = Create(redis.Object);
        tracker.SubscribeRemote(subscriber.Object);
        subscriber.Verify(s => s.Subscribe(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public void SentAtRoundtrips()
    {
        var bytes = MessagePackSerializer.Serialize(new RedisFlipMatchTracker.FlipMatch { UId = 1, Tier = AccountTier.PREMIUM, Server = "x", SentAtUnixMs = 1234567890123 });
        MessagePackSerializer.Deserialize<RedisFlipMatchTracker.FlipMatch>(bytes).SentAtUnixMs.Should().Be(1234567890123);
    }

    [Test]
    public void PublishSetsSentAt()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Create(redis.Object).ReportMatch(7, AccountTier.PREMIUM);
        subscriber.Verify(s => s.Publish(It.IsAny<RedisChannel>(),
            It.Is<RedisValue>(v => MessagePackSerializer.Deserialize<RedisFlipMatchTracker.FlipMatch>((byte[])v).SentAtUnixMs >= before),
            CommandFlags.FireAndForget), Times.Once);
    }

    [Test]
    public void LegacyThreeFieldPayloadDeserializesWithoutSentAt()
    {
        var legacy = MessagePackSerializer.Serialize(new object[] { 9L, (int)AccountTier.PREMIUM_PLUS, "other" });
        var match = MessagePackSerializer.Deserialize<RedisFlipMatchTracker.FlipMatch>(legacy);
        match.UId.Should().Be(9);
        match.SentAtUnixMs.Should().Be(0);
        RedisFlipMatchTracker.LagSeconds(match, 1_000_000).Should().BeNull();

        var tracker = Create(redis.Object);
        handler(RedisChannel.Literal("flip_matched"), legacy);
        tracker.HighestMatchedTier(9).Should().Be(AccountTier.PREMIUM_PLUS);
    }

    [Test]
    public void LagIsNowMinusSentAndClampedAtZero()
    {
        var match = new RedisFlipMatchTracker.FlipMatch { SentAtUnixMs = 1000 };
        RedisFlipMatchTracker.LagSeconds(match, 1250).Should().BeApproximately(0.25, 1e-9);
        RedisFlipMatchTracker.LagSeconds(match, 900).Should().Be(0);
    }

    [Test]
    public void ReachedAtDelegatesToLocalAndRemote()
    {
        var tracker = Create(redis.Object);
        tracker.ReachedAt(9, AccountTier.PREMIUM).Should().BeNull();
        handler(RedisChannel.Literal("flip_matched"), Serialize(9, AccountTier.PREMIUM_PLUS, "other"));
        tracker.ReachedAt(9, AccountTier.PREMIUM).Should().NotBeNull();
    }

    private static byte[] Serialize(long uid, AccountTier tier, string server) =>
        MessagePackSerializer.Serialize(new RedisFlipMatchTracker.FlipMatch { UId = uid, Tier = tier, Server = server });

    private static bool IsMatch(RedisValue v, long uid, AccountTier tier, string server)
    {
        var m = MessagePackSerializer.Deserialize<RedisFlipMatchTracker.FlipMatch>((byte[])v);
        return m.UId == uid && m.Tier == tier && m.Server == server;
    }
}
