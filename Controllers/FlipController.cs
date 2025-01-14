using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;
using System;
using Coflnet.Sky.ModCommands.Services;
using System.Collections.Generic;
using static Coflnet.Sky.ModCommands.Services.BlockedService;

namespace Coflnet.Sky.ModCommands.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class FlipController : ControllerBase
    {
        private FlipperService flipperService;
        private IBlockedService blockedService;

        public FlipController(FlipperService flipperService, IBlockedService blockedService)
        {
            this.flipperService = flipperService;
            this.blockedService = blockedService;
        }

        [HttpGet]
        [Route("blocked")]
        public async Task<IEnumerable<BlockedReason>> GetBlockedReasons(string userId, string auctionUuid)
        {
            return await blockedService.GetBlockedReasons(userId, Guid.Parse(auctionUuid));
        }

        /// <summary>
        /// Indicates status of service, should be 200 (OK)
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("")]
        public async Task TrackFlip([FromBody] LowPricedAuction[] flips)
        {
            foreach (var item in flips)
            {
                item.Auction.Uuid = Guid.NewGuid().ToString().Replace("-", "");
                item.Auction.Context = new() { { "pre-api", "" }, { "cname", item.Auction.ItemName } };
            }
            //Console.WriteLine(JsonConvert.SerializeObject(flips, Formatting.Indented));
            await flipperService.DeliverLowPricedAuctions(flips);
        }
    }
}
