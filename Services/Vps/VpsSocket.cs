using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Coflnet.Sky.ModCommands.Services.Vps;

public class VpsSocket : WebSocketBehavior
{
    public string IP { get; set; }
    private ILogger<VpsSocket> logger;

    protected override void OnOpen()
    {
        logger = DiHandler.GetService<ILogger<VpsSocket>>();
        var args = QueryString;
        IP = args["ip"];
        var secret = args["secret"];
        var configuration = DiHandler.GetService<IConfiguration>();
        if (secret != configuration["vps:secret"] && configuration["vps:secret"] != null)
        {
            Close();
            return;
        }
        var vpsService = DiHandler.GetService<VpsInstanceManager>();
        vpsService.OnInstanceCreated += update =>
        {
            if (update.Instance.HostMachineIp == IP)
            {
                Send(JsonConvert.SerializeObject(Response.Create("configUpdate", update)));
            }
        };
        Task.Run(async () =>
        {
            try
            {
                var vps = await vpsService.GetVps(IP);
                Send(JsonConvert.SerializeObject(Response.Create("init", vps)));
                vpsService.Connected(IP);
            }
            catch (System.Exception e)
            {
                logger.LogError(e, "Boostraping VPS list");
            }
            while(this.ReadyState == WebSocketState.Open)
            {
                await Task.Delay(60_000);
                vpsService.Connected(IP);
            }
        });

    }
    protected override void OnMessage(MessageEventArgs e)
    {
        Task.Run(async () =>
        {
            var source = DiHandler.GetService<ActivitySource>();
            using var activity = source.StartActivity("VpsSocket.OnMessage");
            try
            {
                await RunCommand(e);
            }
            catch (Exception ex)
            {
                using var errorActivity = source.StartActivity("error");
                errorActivity.Log(ex.ToString());
                logger.LogError(ex, "Error in VpsSocket");
                Send(JsonConvert.SerializeObject(new { type = "error", data = ex.Message }));
            }
        });
    }

    private static async Task RunCommand(MessageEventArgs e)
    {
        var response = JsonConvert.DeserializeObject<Response>(e.Data);
        Activity.Current.AddTag("type", response.type);
        Activity.Current.Log(response.data);
        if (response.type == "extraUpdate")
        {
            var extraConfig = JsonConvert.DeserializeObject<ExtraConfig>(response.data);
            await DiHandler.GetService<VpsInstanceManager>().PersistExtra(extraConfig.userId, extraConfig.extraConfig);
        }
    }

    public class ExtraConfig
    {
        public string userId { get; set; }
        public string extraConfig { get; set; }
    }
}

public class TPM
{
    public static readonly string Default = """
    {
    //Put your minecraft IGN here. To use multiple, follow this format: ["account1", "account2"],
    "igns": [""],
    //Used in backend. Get it from the /get_discord_id command in TPM server
    "discordID": "",
    //Refer to https://discord.com/channels/1261825756615540836/1265035635845234792 for help
    "webhook": "",
    //{0} is item. {1} is profit. {2} is price. {3} is target. {4} is buyspeed. {5} is BED or NUGGET. {6} is finder. {7} is the auctionID. {8} is the shortened price. {9} is the bot's username. {10} is the flip volume. {11} is the flip profit percentage
    "webhookFormat": "You bought [``{0}``](https:\/\/sky.coflnet.com\/auction\/{7}) for ``{2}`` (``{1}`` profit) in ``{4}ms``",
    
    //Send every flip seen to this webhook. Good for testing configs
    "sendAllFlips": "",
    //Flip on a friend's island
    "visitFriend": "",
    //Required to use relist!! Will flip in if you don't have a cookie
    "useCookie": true,
    //If cookie is under this time then buy a new one. Leave blank to never auto buy a cookie.
    //Use y, d, or h to set time
    "autoCookie": "1h",
    //Don't claim coop's auctions
    "angryCoopPrevention": false,
    //Automatically list auctions
    "relist": true,
    //Pings you when TPM updates (webhook required)
    "pingOnUpdate": false,
    //Delay between actions. For example, opening flips
    "delay": 250,
    //Delay for beds. Refer to https://discord.com/channels/1261825756615540836/1275546557508223048 for help
    "waittime": 15,
    //Set up different list price ranges and their corresponding percent off of target price. (The lower value of the range is inclusive, the higher value is exclusive)
    "percentOfTarget": ["0", "10b", 97],
    //Amount of time (hours) to list an auction.
    //Works the same as percentOfTarget but for time auctions are listed!
    "listHours": ["0", "10b", 48],
    //Delay between clicks for bed spam (ideally use 100-125)
    "clickDelay": 125,
    //Decides the way to  
    "bedSpam": false,
    //Won't show spam messages
    "blockUselessMessages": true,
    //Digit to round relist price to. For example 6 would round 1,234,567 to 1,200,000
    "roundTo": 6,
    //Skip the confirm screen on NUGGET flips (50ms faster but higher ban rate)
    //This is an OR statement btw
    "skip": {

        //Skip on every flip
        "always": false,
        //Skip on flips with a profit over x
        "minProfit": "25m",
        //Skip on flips over this %
        "profitPercentage": "500",
        //Skip on flips over this price
        "minPrice": "500m",
        //Skip on user finder flips
        "userFinder": true,
        //Skip on cosmetic flips
        "skins": true

    },
    //Items that you don't want automatically listed
    "doNotRelist": {

        //Items over x profit
        "profitOver": "50m",
        //Cosmetic items
        "skinned": true,
        //Don't list certain item tags
        "tags": ["HYPERION"],
        //Finders to not list. Options: USER, CraftCost, TFM, AI, SNIPER, STONKS, FLIPPER
        "finders": ["USER", "CraftCost"],
        //If an item is in a new stack then this controls if it's listed
        //For example, if you have 1 spooky fragment in your inventory and then buy 4 you will now obviously have 5 in a stack.
        //If this is set to true, it will list the 5 fragments for the price of 1 fragment multiplied by 5.
        "stacks": false,
        //Pings you when an item doesn't list
        "pingOnFailedListing": false,
        //Doesn't list a drill if it has any parts (It will then automatically remove and sell the parts)
        "drillWithParts": true,
        //Will automatically get a price from the cofl API and relist auctions that are expired with the new price. False = don't list. True = list
        "expiredAuctions": false,
        //Pricing to use for expired auctions. Set to 1 if you want to use cofl lore pricing stuff, set to 2:x if you want to cut away x percentage from the price
        "relistMode": "2:97"

    },
    //Choose how long to flip for and rest for in hours.
    "autoRotate": {

        //Put your IGN (CAPS MATTER).
        //If you run multiple accounts, put a , after the value (second quote), press enter, and follow the same format as the first.
        //Add an r after the number that you want it to rest for and an F for how long you want it to flip for.
        "ign": "12r:12f"

    },
    
    //Cofl account password. DO NOT SHARE
    "session": ""

    }
    """;
public class AutoRotate
    {
        [DataMember(Name="ign")]
        public string ign;
    }

    public class DoNotRelist
    {
        [DataMember(Name="profitOver")]
        public string profitOver;

        [DataMember(Name="skinned")]
        public bool skinned;

        [DataMember(Name="tags")]
        public string[] tags;

        [DataMember(Name="finders")]
        public string[] finders;

        [DataMember(Name="stacks")]
        public bool stacks;

        [DataMember(Name="pingOnFailedListing")]
        public bool pingOnFailedListing;

        [DataMember(Name="drillWithParts")]
        public bool drillWithParts;

        [DataMember(Name="expiredAuctions")]
        public bool expiredAuctions;

        [DataMember(Name="relistMode")]
        public string relistMode;
    }

    public class Config
    {
        [DataMember(Name="igns")]
        public string[] igns;

        [DataMember(Name="discordID")]
        public string discordID;

        [DataMember(Name="webhook")]
        public string webhook;

        [DataMember(Name="webhookFormat")]
        public string webhookFormat;

        [DataMember(Name="sendAllFlips")]
        public string sendAllFlips;

        [DataMember(Name="visitFriend")]
        public string visitFriend;

        [DataMember(Name="useCookie")]
        public bool useCookie;

        [DataMember(Name="autoCookie")]
        public string autoCookie;

        [DataMember(Name="angryCoopPrevention")]
        public bool angryCoopPrevention;

        [DataMember(Name="relist")]
        public bool relist;

        [DataMember(Name="pingOnUpdate")]
        public bool pingOnUpdate;

        [DataMember(Name="delay")]
        public int delay;

        [DataMember(Name="waittime")]
        public int waittime;

        [DataMember(Name="percentOfTarget")]
        public string[] percentOfTarget;

        [DataMember(Name="listHours")]
        public string[] listHours;

        [DataMember(Name="clickDelay")]
        public int clickDelay;

        [DataMember(Name="bedSpam")]
        public bool bedSpam;

        [DataMember(Name="blockUselessMessages")]
        public bool blockUselessMessages;

        [DataMember(Name="roundTo")]
        public int roundTo;

        [DataMember(Name="skip")]
        public Skip skip;

        [DataMember(Name="doNotRelist")]
        public DoNotRelist doNotRelist;

        [DataMember(Name="autoRotate")]
        public AutoRotate autoRotate;

        [DataMember(Name="session")]
        public string session;
    }

    public class Skip
    {
        [DataMember(Name="always")]
        public bool always;

        [DataMember(Name="minProfit")]
        public string minProfit;

        [DataMember(Name="profitPercentage")]
        public string profitPercentage;

        [DataMember(Name="minPrice")]
        public string minPrice;

        [DataMember(Name="userFinder")]
        public bool userFinder;

        [DataMember(Name="skins")]
        public bool skins;
    }

}

public class VPsStateUpdate
{
    public TPM.Config Config { get; set; }
    public Instance Instance { get; set; }
    public string ExtraConfig { get; set; }
}

public class Instance
{
    public string HostMachineIp { get; set; }
    public string OwnerId { get; set; }
    public Guid Id { get; set; }
    public string AppKind { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime PaidUntil { get; set; }
    public Dictionary<string, string> Context { get; set; }
    public string PublicIp { get; set; }
}
