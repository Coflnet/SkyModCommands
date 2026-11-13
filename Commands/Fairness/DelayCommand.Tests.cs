using System;
using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class DelayCommandTests
{
    private static readonly TimeSpan Delayed = TimeSpan.FromSeconds(1);

    [Test]
    public void NoticeIsHiddenWithoutCountedFlips()
    {
        Assert.That(DelayCommand.GetExemptionNotice(0, 0, Delayed), Is.Null);
    }

    [Test]
    public void NoticeIsHiddenWhenNotDelayed()
    {
        Assert.That(DelayCommand.GetExemptionNotice(3, 12, TimeSpan.Zero), Is.Null);
    }

    [Test]
    public void NoticeShowsColouredCountsAndText()
    {
        var notice = DelayCommand.GetExemptionNotice(3, 12, Delayed);

        Assert.That(notice, Does.Contain($"{McColorCodes.AQUA}3"));
        Assert.That(notice, Does.Contain($"{McColorCodes.AQUA}12"));
        Assert.That(notice, Does.EndWith($"{McColorCodes.AQUA}3{McCommand.DEFAULT_COLOR} of the {McColorCodes.AQUA}12{McCommand.DEFAULT_COLOR} flips had no delay"));
        Assert.That(notice, Does.Not.Contain("0.33"));
    }

    [Test]
    public void HoverExplainsTheCounts()
    {
        Assert.That(DelayCommand.ExemptionNoticeHover, Is.Not.Empty);
        Assert.That(DelayCommand.ExemptionNoticeHover, Does.Contain("sent without delay because they are known to be macroed so there is no point in delaying them."));
    }

    [Test]
    public void NoticeIsShownWhenNoneWereExempt()
    {
        Assert.That(DelayCommand.GetExemptionNotice(0, 5, Delayed), Does.Contain($"{McColorCodes.AQUA}0"));
    }

    [Test]
    public void SessionIsOnlyProxySessionAfterProxySyncRegistered()
    {
        var socket = new Coflnet.Sky.ModCommands.Tests.ModSessionLifesycleTests.TestSocket();

        Assert.That(socket.IsProxySession, Is.False);
        socket.TryRegisterProxySync();
        Assert.That(socket.IsProxySession, Is.True);
    }
}
