using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;

namespace Coflnet.Sky.Commands.MC;
public class AltChecker
{
    private const string StatsBaseUrl = "https://sky.shiiiyu.moe";

    /// <summary>
    /// Returns 0-100 probability of the player being an alt
    /// </summary>
    /// <param name="uuid"></param>
    /// <returns></returns>
    public async Task<int> AltLevel(string uuid)
    {
        var client = new RestClient();
        var request = new RestRequest($"{StatsBaseUrl}/api/v2/coins/{uuid}", Method.Get);
        var response = await client.ExecuteGetAsync(request);
        if (string.IsNullOrWhiteSpace(response.Content))
            return -1;

        if (response.Content.Contains("no SkyBlock profile", StringComparison.OrdinalIgnoreCase))
        {
            await client.ExecuteGetAsync(new RestRequest($"{StatsBaseUrl}/stats/{uuid}")); // request profile loading
            return -1;
        }
        Response result;
        try
        {
            result = JsonConvert.DeserializeObject<Response>(response.Content);
        }
        catch (JsonException)
        {
            return -1;
        }

        if (result?.profiles == null || result.profiles.Count == 0)
            return -1;

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
