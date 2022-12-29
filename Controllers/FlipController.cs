using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;
using System;
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class FlipController : ControllerBase
    {
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
                item.Auction.Context = new() { { "pre-api", "" } };
            }
            //Console.WriteLine(JsonConvert.SerializeObject(flips, Formatting.Indented));
            await FlipperService.Instance.DeliverLowPricedAuctions(flips);
        }
    }
}
