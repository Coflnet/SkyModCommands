using System;
using System.Threading;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public interface ISpamController
    {
        void Reset();
        bool ShouldBeSent(FlipInstance auction);
    }
    public class SpamController : ISpamController
    {
        private int SentSinceReset = 0;
        private long HighestValue = 0;

        public void Reset()
        {
            SentSinceReset = 0;
            HighestValue = 0;
        }

        public virtual bool ShouldBeSent(FlipInstance auction)
        {
            if (SentSinceReset > 3)
            {
                if (HighestValue > auction.Profit && auction.Finder != Core.LowPricedAuction.FinderType.USER)
                    return false;
            }
            Interlocked.Increment(ref SentSinceReset);
            HighestValue = Math.Max(auction.Profit, HighestValue);
            return true;
        }
    }
}
