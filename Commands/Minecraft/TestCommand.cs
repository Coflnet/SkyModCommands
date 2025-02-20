using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public class KeybindRegister
    {
        [Newtonsoft.Json.JsonProperty("name")]
        public string Name { get; set; }
        [Newtonsoft.Json.JsonProperty("defaultKey")]
        public string DefaultKey { get; set; }
    }
    public class TestCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.Send(Response.Create("registerKeybind", new KeybindRegister[] { new() { Name = "/cofl lb", DefaultKey = "F" }, new() { Name = "test2", DefaultKey = "Z" } }));
            socket.Send(Response.Create("proxy", new ProxyRequest[] { new() { uploadTo = "https://sky.coflnet.com/api/data/proxy?test", id = "guploadTest", url = "https://willhaben.at" } }));
            return;
            socket.Send(Response.Create("runSequence", new Sequence
            {
                steps = new(){
                new (){type="execute", data="/sbmenu"},
                new (){type="upload", data=""},
            }
            }));
            socket.Dialog(db => db.Msg("Sent sequence, awaiting response"));

            await Task.Delay(5000);
            socket.ExecuteCommand("/cofl report sequence");
            socket.Dialog(db => db.Msg("Autocreated a report, thanks"));
        }

        private static async Task SendRandomFlip(MinecraftSocket socket)
        {
            var r = new Random();
            var activeAuction = await ItemPrices.Instance.GetActiveAuctions(new ActiveItemSearchQuery()
            {
                name = "JUNGLE_KEY",
            });

            var targetAuction = activeAuction.OrderBy(x => x.Price + r.Next(10000)).FirstOrDefault();
            await socket.ModAdapter.SendFlip(new Shared.FlipInstance()
            {
                Auction = new SaveAuction()
                {
                    StartingBid = 5,
                    Uuid = targetAuction.Uuid,
                    AuctioneerId = "384a029294fc445e863f2c42fe9709cb",
                    Context = new() { { "lore", "Custom lore\nwhatever" } }
                },
                Finder = LowPricedAuction.FinderType.SNIPER,
                MedianPrice = 10000000,
                LowestBin = 10000000,
                Volume = 5,
                Interesting = new() { "cool flip" },
                Context = new()
            });
        }
    }

    public class ProxyRequest
    {
        public string uploadTo { get; set; }
        public string id { get; set; }
        public string url { get; set; }
    }

    public class Sequence
    {
        public List<Step> steps { get; set; }
    }

    public class Step
    {
        public string type { get; set; }
        public string data { get; set; }
    }
}