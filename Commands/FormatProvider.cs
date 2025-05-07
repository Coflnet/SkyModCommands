using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Core;
using System.Globalization;
using System.Net.Sockets;

namespace Coflnet.Sky.Commands.MC
{
    public class FormatProvider
    {
        private IFlipConnection con;
        private FlipSettings Settings => con.Settings;

        public FormatProvider(IFlipConnection connection)
        {
            con = connection;
        }

        /// <summary>
        /// By RenniePet on Stackoverflow
        /// https://stackoverflow.com/a/30181106
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        public static string FormatPriceShort(double num)
        {
            if (num == 0) // there was an issue with flips attempting to be devided by 0
                return "0";
            var minusPrefix = num < 0 ? "-" : "";
            num = Math.Abs(num);
            // Ensure number has max 3 significant digits (no rounding up can happen)
            long i = (long)Math.Pow(10, (long)Math.Max(0, Math.Log10(num) - 2));
            var roundNum = (long)num / i * i;

            if (num >= 1000000000)
                return Format(1000000000, "B");
            if (num > 1000000 - 1)
                return Format(1000000, "M");
            if (num >= 1000)
                return Format(1000, "K");

            return minusPrefix + num.ToString("0.#", CultureInfo.InvariantCulture);

            string Format(double devider, string suffix, string format = "0.##")
            {
                return minusPrefix + (roundNum / devider).ToString(format, CultureInfo.InvariantCulture) + suffix;
            }
        }


        public string GetProfitColor(int profit)
        {
            if (profit >= 50_000_000)
                return McColorCodes.GOLD;
            if (profit >= 10_000_000)
                return McColorCodes.AQUA;
            if (profit >= 1_000_000)
                return McColorCodes.GREEN;
            if (profit >= 100_000)
                return McColorCodes.DARK_GREEN;
            return McColorCodes.DARK_GRAY;
        }

        public string FormatFlip(FlipInstance flip, string blockedReason = null)
        {
            if (Settings.Visibility == null)
                Settings.Visibility = new VisibilitySettings();
            if (Settings.ModSettings == null)
                Settings.ModSettings = new ModSettings();

            //Settings.GetPrice(flip, out long targetPrice, out long profit);
            var profit = flip.Profit;
            var targetPrice = flip.Target;
            var priceColor = GetProfitColor((int)profit);
            string finderType = FinderText(flip);
            var a = flip.Auction;
            string itemName = GetItemName(flip.Auction);
            var rarityColor = GetRarityColor(a.Tier);
            var cost = a.HighestBidAmount == 0 ? a.StartingBid : a.HighestBidAmount;
            var formatString = blockedReason == null ? Settings.ModSettings?.Format : Settings.ModSettings?.BlockedFormat;
            var targetPriceFormatted = targetPrice < 0 ? $"{McColorCodes.DARK_GREEN}NoRelist" : FormatPrice(targetPrice);
            if (!string.IsNullOrWhiteSpace(formatString) && flip.Auction?.Context != null)
            {
                /*
                    "\n{0}: {1}{2} {3}{4} -> {5} (+{6} {7}) Med: {8} Lbin: {9} Volume: {10}"
                    {0} FlipFinder
                    {1} Item Rarity Color
                    {2} Item Name
                    {3} Price color
                    {4} Starting bid
                    {5} Target Price
                    {6} Estimated Profit
                    {7} Provit percentage
                    {8} Median Price
                    {9} Lowest Bin
                    {10}Volume
                    {11} Flip source
                    {12} Blocked reason
                    
                */
                string source = GetSource(flip);
                return string.Format(formatString.Replace("\\n", "\n"),
                    finderType,
                    rarityColor,
                    itemName.Replace(rarityColor == string.Empty ? "ignore" : rarityColor, "").Replace(McColorCodes.GRAY, ""), // pet level
                    priceColor,
                    FormatPrice(cost),
                    targetPriceFormatted, // this is {5}
                    FormatPrice(profit),
                    FormatPrice(flip.ProfitPercentage),
                    FormatPrice(flip.MedianPrice),
                    FormatPrice(flip.LowestBin ?? 0),
                    flip.Volume.ToString("0.#"),  // this is {10}
                    source,
                    blockedReason
                );
            }
            var textAfterProfit = (Settings?.Visibility?.ProfitPercentage ?? false) ? $" {McColorCodes.DARK_RED}{FormatPrice(flip.ProfitPercentage)}%{priceColor}" : "";

            var builder = new StringBuilder(80);

            builder.Append($"\n{finderType}: {rarityColor}{itemName} {priceColor}{FormatPrice(cost)} -> {targetPriceFormatted} ");
            try
            {
                if ((Settings?.Visibility?.Profit ?? false) || (Settings?.Visibility?.EstimatedProfit ?? false))
                    builder.Append($"(+{FormatPrice(profit)}{textAfterProfit}) ");
                if (Settings?.Visibility?.MedianPrice ?? false)
                    builder.Append(McColorCodes.GRAY + " Med: " + McColorCodes.AQUA + FormatPrice(flip.MedianPrice));
                if (Settings?.Visibility?.LowestBin ?? false)
                    builder.Append(McColorCodes.GRAY + " LBin: " + McColorCodes.AQUA + FormatPrice(flip.LowestBin ?? 0));
                if (Settings?.Visibility?.Volume ?? false)
                    builder.Append(McColorCodes.GRAY + " Vol: " + McColorCodes.AQUA + flip.Volume.ToString("0.#"));
            }
            catch (Exception e)
            {
                if (con == null)
                    throw new Exception("connection is null " + profit, e);
                if (Settings == null)
                    throw new Exception("settings are null " + profit, e);
                con.Log(e.ToString(), Microsoft.Extensions.Logging.LogLevel.Error);
                throw new Exception(e + Environment.NewLine + JSON.Stringify(Settings), e);
            }
            return builder.ToString();
        }

        private static string GetSource(FlipInstance flip)
        {
            return flip.Auction.Context.ContainsKey("pre-api") ?
                                    (flip.Context.ContainsKey("isRR") ? McColorCodes.RED + "PRE-RR" : "PRE")
                                    : (IsPremiumPlus(flip)
                                        ? "PREM+" : "");
        }

        public string GetItemName(SaveAuction auction)
        {
            string itemName = auction?.Context?.ContainsKey("cname") ?? false ?
                auction.Context["cname"]
                : $"{auction.ItemName}";
            if (Settings.ModSettings.ShortNames)
            {
                foreach (var item in ItemReferences.reforges)
                {
                    if (itemName.ToLower().Contains(item))
                        itemName = itemName.Replace(item, "", true, CultureInfo.InvariantCulture);
                }
            }
            return itemName;
        }

        private static string FinderText(FlipInstance flip)
        {
            return flip.Finder switch
            {
                LowPricedAuction.FinderType.SNIPER => "SNIPE",
                LowPricedAuction.FinderType.SNIPER_MEDIAN => "MS",
                LowPricedAuction.FinderType.USER => "USER",
                LowPricedAuction.FinderType.STONKS => "RISKY",
                LowPricedAuction.FinderType.TFM => "TFM obsolete",
                LowPricedAuction.FinderType.AI => "AI",
                LowPricedAuction.FinderType.FLIPPER => "FLIP",
                LowPricedAuction.FinderType.CraftCost => "Craft",
                _ => "Flip"
            };
        }

        private static bool IsPremiumPlus(FlipInstance flip)
        {
            return flip.Auction.Context != null && flip.Auction.Context.TryGetValue("cname", out var colorName) && colorName.Contains(McColorCodes.DARK_GRAY + "!");
        }

        public string GetRarityColor(Tier rarity)
        {
            return rarity switch
            {
                Tier.COMMON => "§f",
                Tier.EPIC => "§5",
                Tier.UNCOMMON => "§a",
                Tier.RARE => "§9",
                Tier.SPECIAL => "§c",
                Tier.DIVINE => "§4",
                Tier.VERY_SPECIAL => "§4",
                Tier.LEGENDARY => "§6",
                Tier.MYTHIC => "§d",
                _ => ""
            };
        }

        /// <summary>
        /// Formats the price either with decimal delimitors or by shortening it
        /// </summary>
        /// <param name="price"></param>
        /// <returns></returns>
        public string FormatPrice(long? price)
        {
            if (Settings?.ModSettings?.ShortNumbers ?? false)
                return FormatPriceShort(price ?? 0);
            return string.Format(CultureInfo.InvariantCulture, "{0:n0}", price ?? 0);
        }

        /// <summary>
        /// Formats a timespan in accordance with shortest unit
        /// </summary>
        /// <param name="timeSpan"></param>
        /// <returns></returns>
        public static string FormatTimeGlobal(TimeSpan timeSpan)
        {
            var prefix = timeSpan.TotalSeconds < 0 ? "-" : "";
            timeSpan = timeSpan.Duration();
            if (timeSpan.TotalDays > 1.05)
                return $"{timeSpan.TotalDays:0.#}d";
            if (timeSpan.TotalHours > 1)
                return $"{timeSpan.TotalHours:0.#}h";
            if (timeSpan.TotalMinutes > 1)
                return $"{timeSpan.TotalMinutes:0.#}m";
            return $"{prefix}{timeSpan.TotalSeconds:0.#}s";
        }

        public string FormatTime(TimeSpan time)
        {
            return FormatTimeGlobal(time);
        }

        public ChatPart[] WelcomeMessage()
        {
            var text = $"§fFound and loaded settings for your connection\n"
                                                + FormatSettingsSummary(Settings)
                                    + "\n§8: nothing else to do have a nice day :)";
            var hover = $"{McColorCodes.GRAY} MinVolume: {McColorCodes.AQUA}{Settings?.MinVolume}\n"
                        + $"{McColorCodes.GRAY} MinProfitPercent: {McColorCodes.AQUA}{FormatPrice(Settings?.MinProfitPercent)}"
                        + $"{McColorCodes.GRAY} The Black and Whitelist is the count of entries";
            var spacer = $"{McColorCodes.DARK_RED}----------------------------";
            return new DialogBuilder().MsgLine(text, null, hover)
                    .MsgLine($"{McColorCodes.AQUA}: click this if you want to change a setting", "https://sky.coflnet.com/flipper",
                            $"Opens the website.\nalternatively you can use the {McColorCodes.AQUA}/cofl set{McColorCodes.WHITE} command to change settings in game")
                        .If(() => DateTime.UtcNow < new DateTime(2022, 11, 26, 6, 0, 0), db => db
                        .CoflCommand<TopUpCommand>($"{spacer}\n CoflCoin packages 5400 or more are {McColorCodes.DARK_GREEN}{McColorCodes.BOLD}20% OFF{McColorCodes.RESET} today\n Click to purchase {McColorCodes.ITALIC}in game\n{spacer}", "", "Show options"));
        }

        public string FormatSettingsSummary(FlipSettings s)
        {
            var full = $"{McColorCodes.GRAY} MinProfit: {McColorCodes.AQUA}{FormatPrice(s.MinProfit)}  ";
            if (s.UsedVersion == 0)
                full += $"{McColorCodes.GRAY} Whitelist: {McColorCodes.AQUA}{s?.WhiteList?.Count ?? 0}"
                    + $"{McColorCodes.GRAY} Blacklist: {McColorCodes.AQUA}{s?.BlackList?.Count ?? 0} ";
            else
                full += $"{McColorCodes.GRAY} Using: {McColorCodes.AQUA}{s.PublishedAs} {McColorCodes.GRAY}v{McColorCodes.GOLD}{s.UsedVersion} ";
            return full;
        }

        public string GetHoverText(FlipInstance flip)
        {
            if (Settings.Visibility.Lore)
                return flip.Auction?.Context?.GetValueOrDefault("lore");
            return string.Join('\n', flip.Interesting.Select(s => "・" + s)) + "\n" + flip.SellerName;
        }
    }
}
