using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using RestSharp;

namespace Coflnet.Sky.Commands.MC
{
    public class ImportTfmCommand : McCommand
    {
        IRestClient client = new RestClient("https://tfm.thom.club/");
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var parts = arguments.Trim('"').Split(' ');
            if (parts.Length != 2)
            {
                socket.SendMessage($"{COFLNET}This command lets you import blacklists from tfm.\n"
                + $"{McColorCodes.GREEN}Usage: /cofl importtfm {McColorCodes.YELLOW}<identifier> <userName>\n"
                + $"{McColorCodes.GREEN}Where <identifier> is one of user, enchant or item");
                return;
            }
            var type = parts[0].ToLower();
            var userName = parts[1];
            var request = new RestRequest($"get_blacklist?blacklist_id={userName}&type={type}", Method.GET);
            var response = await client.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                socket.SendMessage($"{COFLNET}{McColorCodes.RED}Could not load {type} blacklist from {userName}");
                return;
            }
            var json = response.Content;
            var blacklist = JsonConvert.DeserializeObject<TfmBlacklist>(json);

            var elements = blacklist.Blacklist.Select(async s => type switch
            {
                "user" => new ListEntry()
                {
                    filter = new Dictionary<string, string>(){
                        {"Seller", s}
                    },
                    DisplayName = (await PlayerService.Instance.GetPlayer(s)).Name
                },
                "item" => await ConvertItem(s),
                "enchant" => new ListEntry()
                {
                    filter = new Dictionary<string, string>(){
                        {"Enchantment", s.Split('-').First()},
                        {"EnchantLvl", s.Split('-').Last()}
                    }
                },
                _ => null
            });

            socket.sessionLifesycle.FlipSettings.Value.BlackList.AddRange((await Task.WhenAll(elements)).Where(e => e != null));
            await socket.sessionLifesycle.FlipSettings.Update();
            socket.SendMessage($"{COFLNET}{McColorCodes.GREEN}Imported {type} blacklist from {userName}");
        }

        private static async Task<ListEntry> ConvertItem(string s)
        {
            var entry = new ListEntry()
            {
                filter = new Dictionary<string, string>(),
                DisplayName = (await PlayerService.Instance.GetPlayer(s)).Name
            };
            var nameAndRarity = s.Split("_+_");
            var rarity = nameAndRarity[1];
            entry.filter["Rarity"] = rarity;


            var things = nameAndRarity[0].Split("==");
            var isPet = false;
            if (nameAndRarity.Contains("==MAX"))
            {
                isPet = true;
                entry.filter["PetLevel"] = "100";
            }
            if (nameAndRarity.Contains("==CANDIED"))
            {
                isPet = true;
                entry.filter["Candy"] = ">0";
            }
            if(isPet)
                entry.ItemTag = "PET_" + things[0];
            else 
                entry.ItemTag = things[0];

            return entry;
        }

        public class TfmBlacklist
        {
            public bool Success { get; set; }
            public string[] Blacklist { get; set; }
        }
    }
}