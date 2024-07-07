using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using Coflnet.Sky.Core;
using RestSharp;
using System.Net;

namespace Coflnet.Sky.Commands.MC;
public class RewardHandler
{
    static CookieContainer cookies = new CookieContainer();
    public static async Task SendRewardOptions(MinecraftSocket socket, Match match)
    {
        var rewardLink = match.Groups[1].Value;
        var id = rewardLink.Split('/').Last();
        var restClient = new RestClient();
        Console.WriteLine("fetching reward " + rewardLink);
        var request = new RestRequest(rewardLink, Method.Get);
        request.CookieContainer = cookies;
        // set user agent to avoid cloudflare bot protection
        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
        var responseData = await restClient.ExecuteAsync(request);
        var rewsponse = responseData.Content;
        Console.WriteLine("got response " + rewsponse.Truncate(2000));
        cookies = new();
        foreach (var item in responseData.Cookies)
        {
            Console.WriteLine("got cookie " + item);
            Console.WriteLine(item.GetType().Name);
            cookies.Add((Cookie)item);
        }
        var lines = rewsponse.Split('\n');
        var data = JsonConvert.DeserializeObject<Root>(Regex.Match(lines[14], @"'.*'").Value.Trim('\''));
        if (!data.skippable)
        {
            socket.Dialog(db => db.MsgLine("Seems like you don't have a rank. You have to watch the ad to claim the reward"));
            return;
        }
        var securityToken = lines[13].Split('"')[1];
        var rewards = data.rewards;
        socket.Dialog(db => db.MsgLine("Click to claim your reward").ForEach(rewards, (db, reward, i) =>
        {
            Enum.TryParse<Tier>(reward.rarity, out Tier tier);
            var paddedRarity = reward.rarity.PadRight(10);
            var gameName = reward.gameType != null ? GameNameMapping.GetValueOrDefault(reward.gameType, reward.gameType) : null;
            var formattedLine = $"{McColorCodes.GRAY}->{socket.formatProvider.GetRarityColor(tier) + paddedRarity} {gameName} {reward.amount} {reward.reward}\n";
            db.CoflCommand<ClaimHypixelRewardCommand>(formattedLine, $"{i} {securityToken} {id}");
        }));
    }

    public class ClaimHypixelRewardCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = JsonConvert.DeserializeObject<string>(arguments).Split(" ");
            var selected = int.Parse(args[0]);
            var token = args[1];
            var id = args[2];
            var restClient = new RestClient();
            var request = new RestRequest("https://rewards.hypixel.net/claim-reward/claim", Method.Post);
            request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
            request.AddQueryParameter("option", selected);
            request.AddQueryParameter("id", id);
            request.AddQueryParameter("activeAd", 0);
            request.AddQueryParameter("_csrf", token);
            request.AddQueryParameter("watchedFallback", false);
            request.CookieContainer = cookies;
            Console.WriteLine("claiming reward " + string.Join("\n", request.Parameters.Select(p => p.Name + " " + p.Value)));
            Console.WriteLine($"FullUrl: {restClient.BuildUri(request)}");
            var responseData = restClient.Execute(request);
            Console.WriteLine("claimed reward " + responseData.Content);
            if (responseData.StatusCode == System.Net.HttpStatusCode.OK)
                socket.Dialog(db => db.MsgLine("Claimed reward"));
            else
                socket.Dialog(db => db.MsgLine("Failed to claim reward"));

            return Task.CompletedTask;
        }
    }

    private static Dictionary<string, string> GameNameMapping = new()
    {
        {"WALLS3", "Mega Walls" },
        {"QUAKECRAFT", "Quakecraft" },
        {"WALLS", "Walls"},
        {"PAINTBALL", "Paintball"},
        {"SURVIVAL_GAMES", "Blitz SG"},
        {"TNTGAMES", "TNT Games"},
        {"VAMPIREZ", "VampireZ"},
        {"ARCADE", "Arcade"},
        {"ARENA", "Arena"},
        {"UHC", "UHC"},
        {"MCGO", "Cops and Crims"},
        {"BATTLEGROUND", "Warlords" },
        {"SUPER_SMASH", "Smash Heroes"},
        {"GINGERBREAD", "Turbo Kart Racers"},
        {"SKYWARS", "SkyWars" },
        {"TRUE_COMBAT", "CrazyWalls"},
        {"SPEEDUHC", "Speed UHC"},
        {"BEDWARS", "Bed Wars" },
        {"BUILD_BATTLE", "Build Battle" },
        {"MURDER_MYSTERY", "Murder Mystery"},
        {"DUELS", "Duels"},
        {"LEGACY", "Classic" }
    };

    public class Ad
    {
        public string video { get; set; }
        public int duration { get; set; }
        public string link { get; set; }
        public string buttonText { get; set; }
    }

    public class DailyStreak
    {
        public int value { get; set; }
        public int score { get; set; }
        public int highScore { get; set; }
        public bool keeps { get; set; }
        public bool token { get; set; }
    }

    public class Reward
    {
        public string gameType { get; set; }
        public int amount { get; set; }
        public string rarity { get; set; }
        public string reward { get; set; }
    }

    public class Root
    {
        public List<Reward> rewards { get; set; }
        public string id { get; set; }
        public bool skippable { get; set; }
        public DailyStreak dailyStreak { get; set; }
        public Ad ad { get; set; }
        public int activeAd { get; set; }
        public bool playwire { get; set; }
    }
}
