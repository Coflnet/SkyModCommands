using System.Threading.Tasks;
using Coflnet.Sky.Base.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Base.Services
{
    public class BaseService
    {
        private BaseDbContext db;

        public BaseService(BaseDbContext db)
        {
            this.db = db;
        }

        public async Task<Flip> AddFlip(Flip flip)
        {
            if (flip.Timestamp == default)
            {
                flip.Timestamp = DateTime.Now;
            }
            var flipAlreadyExists = await db.Flips.Where(f => f.AuctionId == flip.AuctionId && f.FinderType == flip.FinderType).AnyAsync();
            if (flipAlreadyExists)
            {
                return flip;
            }
            db.Flips.Add(flip);
            await db.SaveChangesAsync();
            return flip;
        }

        public async Task<FlipEvent> AddEvent(FlipEvent flipEvent)
        {
            if (flipEvent.Timestamp == default)
            {
                flipEvent.Timestamp = DateTime.Now;
            }
            var flipEventAlreadyExists = await db.FlipEvents.Where(f => f.AuctionId == flipEvent.AuctionId && f.Type == flipEvent.Type && f.PlayerId == flipEvent.PlayerId)
                    .AnyAsync();
            if (flipEventAlreadyExists)
            {
                return flipEvent;
            }
            db.FlipEvents.Add(flipEvent);
            await db.SaveChangesAsync();
            return flipEvent;
        }
    }
}
