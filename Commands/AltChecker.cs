using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;

namespace Coflnet.Sky.Commands.MC;
public class AltChecker
{
    /// <summary>
    /// Returns 0-100 probability of the player being an alt
    /// </summary>
    /// <param name="uuid"></param>
    /// <returns></returns>
    public async Task<int> AltLevel(string uuid)
    {
        var client = new RestClient();
        var request = new RestRequest($"https://sky.shiiyu.moe/api/v2/coins/{uuid}", Method.Get);
        var response = await client.ExecuteGetAsync(request);
        if(response.Content.Contains("no SkyBlock profile"))
        {
            await client.ExecuteGetAsync(new RestRequest($"https://sky.shiiyu.moe/stats/{uuid}")); // request profile loading
            return -1;
        }
        var result = JsonConvert.DeserializeObject<Response>(response.Content);
        var maxPurse = result.profiles.Values.Max(p => p.purse);
        return (int)Math.Max((20_000_000 - maxPurse) / 1_000_000 * 5, 0);
    }

    public class Response
    {
        public Dictionary<string, Profile> profiles { get; set; }
    }

    public class Profile
    {
        public string profile_id { get; set; }
        public string cute_name { get; set; }
        public bool selected { get; set; }
        public double purse { get; set; }
        public double bank { get; set; }
    }
}
