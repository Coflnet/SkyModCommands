using System;
using System.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class ConfigsCommandTests
{
    private static readonly DateTime OnboardingCutoff = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public void StaleConfigLosesFiftyRankingVotesAndFallsToSecondPage()
    {
        var ratings = Enumerable.Range(0, 12)
            .Select(index => Rating($"recent-{index}", 40 - index, OnboardingCutoff))
            .Append(Rating("stale", 78, OnboardingCutoff.AddTicks(-1)))
            .ToList();

        var ranked = ConfigsCommand.RankForListing(ratings, OnboardingCutoff);

        Assert.Multiple(() =>
        {
            Assert.That(ranked.Take(12).Select(r => r.ConfigName),
                Does.Not.Contain("stale"));
            Assert.That(ranked[12].ConfigName, Is.EqualTo("stale"));
            Assert.That(ranked[12].Rating, Is.EqualTo(28));
        });
    }

    [Test]
    public void MigrationPenaltyAppliesBeforeCutoffIncludingDefaultTimestamp()
    {
        var ratings = new[]
        {
            Rating("before", 60, OnboardingCutoff.AddTicks(-1)),
            Rating("at", 60, OnboardingCutoff),
            Rating("after", 60, OnboardingCutoff.AddTicks(1)),
            Rating("default", 60, default)
        };

        var ranked = ConfigsCommand.RankForListing(ratings, OnboardingCutoff)
            .ToDictionary(r => r.ConfigName);

        Assert.Multiple(() =>
        {
            Assert.That(ranked["before"].Rating, Is.EqualTo(10));
            Assert.That(ranked["at"].Rating, Is.EqualTo(60));
            Assert.That(ranked["after"].Rating, Is.EqualTo(60));
            Assert.That(ranked["default"].Rating, Is.EqualTo(10));
        });
    }

    [Test]
    public void CompletedCalendarMonthsCostTwoVotesEach()
    {
        var now = new DateTime(2026, 12, 15, 0, 0, 0, DateTimeKind.Utc);
        var ratings = new[]
        {
            Rating("partial", 60, new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc)),
            Rating("exact", 60, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc)),
            Rating("multiple", 60, new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc)),
            Rating("future", 60, now.AddDays(1))
        };

        var ranked = ConfigsCommand.RankForListing(ratings, now)
            .ToDictionary(r => r.ConfigName);

        Assert.Multiple(() =>
        {
            Assert.That(ranked["partial"].Rating, Is.EqualTo(60));
            Assert.That(ranked["exact"].Rating, Is.EqualTo(58));
            Assert.That(ranked["multiple"].Rating, Is.EqualTo(54));
            Assert.That(ranked["future"].Rating, Is.EqualTo(60));
        });
    }

    [Test]
    public void EndOfMonthCountsAsOneCompletedCalendarMonth()
    {
        var updated = new DateTime(2027, 1, 31, 12, 0, 0, DateTimeKind.Utc);
        var beforeAnniversary = new DateTime(2027, 2, 28, 11, 59, 59, DateTimeKind.Utc);
        var atAnniversary = updated.AddMonths(1);
        var rating = Rating("month-end", 60, updated);

        Assert.Multiple(() =>
        {
            Assert.That(ConfigsCommand.RankForListing([rating], beforeAnniversary).Single().Rating,
                Is.EqualTo(60));
            Assert.That(ConfigsCommand.RankForListing([rating], atAnniversary).Single().Rating,
                Is.EqualTo(58));
        });
    }

    [Test]
    public void MonthlyPenaltyStacksWithMigrationPenaltyAndUsesCreatedWhenUpdateMissing()
    {
        var now = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        var old = Rating("old", 100, new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc));
        var createdOnly = Rating("created-only", 100, default);
        createdOnly.Created = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var noDates = Rating("no-dates", 100, default);

        var ranked = ConfigsCommand.RankForListing([old, createdOnly, noDates], now)
            .ToDictionary(r => r.ConfigName);

        Assert.Multiple(() =>
        {
            Assert.That(ranked["old"].Rating, Is.EqualTo(44));
            Assert.That(ranked["created-only"].Rating, Is.EqualTo(46));
            Assert.That(ranked["no-dates"].Rating, Is.EqualTo(50));
        });
    }

    [Test]
    public void RankingLeavesStoredVotesUnchangedAndRecomputesAfterTimestampRefresh()
    {
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var source = Rating("example", 75, OnboardingCutoff.AddDays(-1));
        source.Upvotes.Add("upvoter");
        source.Downvotes.Add("downvoter");

        var first = ConfigsCommand.RankForListing([source], now).Single();
        var second = ConfigsCommand.RankForListing([source], now).Single();
        source.LastUpdated = now;
        var refreshed = ConfigsCommand.RankForListing([source], now).Single();

        Assert.Multiple(() =>
        {
            Assert.That(source.Rating, Is.EqualTo(75));
            Assert.That(source.Upvotes, Is.EquivalentTo(new[] { "upvoter" }));
            Assert.That(source.Downvotes, Is.EquivalentTo(new[] { "downvoter" }));
            Assert.That(first, Is.Not.SameAs(source));
            Assert.That(first.Rating, Is.EqualTo(21));
            Assert.That(second.Rating, Is.EqualTo(21));
            Assert.That(first.Upvotes, Is.EquivalentTo(source.Upvotes));
            Assert.That(first.Downvotes, Is.EquivalentTo(source.Downvotes));
            Assert.That(refreshed.Rating, Is.EqualTo(75));
        });
    }

    private static ConfigsCommand.ConfigRating Rating(string name, int votes, DateTime lastUpdated) => new()
    {
        Type = "config",
        ConfigName = name,
        OwnerId = "creator",
        Rating = votes,
        LastUpdated = lastUpdated,
        Upvotes = [],
        Downvotes = []
    };
}
