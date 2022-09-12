using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Core;

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
        public static string FormatPriceShort(long num)
        {
            if (num <= 0) // there was an issue with flips attempting to be devided by 0
                return "0";
            // Ensure number has max 3 significant digits (no rounding up can happen)
            long i = (long)Math.Pow(10, (long)Math.Max(0, Math.Log10(num) - 2));
            num = num / i * i;

            if (num >= 1000000000)
                return (num / 1000000000D).ToString("0.##") + "B";
            if (num >= 1000000)
                return (num / 1000000D).ToString("0.##") + "M";
            if (num >= 1000)
                return (num / 1000D).ToString("0.##") + "k";

            return num.ToString("#,0");
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

        public string FormatFlip(FlipInstance flip)
        {
            if (Settings.Visibility == null)
                Settings.Visibility = new VisibilitySettings();
            if (Settings.ModSettings == null)
                Settings.ModSettings = new ModSettings();

            Settings.GetPrice(flip, out long targetPrice, out long profit);
            var priceColor = GetProfitColor((int)profit);
            var finderType = flip.Finder switch
            {
                LowPricedAuction.FinderType.SNIPER => "SNIPE",
                LowPricedAuction.FinderType.SNIPER_MEDIAN => "MS",
                _ => "FLIP"
            };
            var a = flip.Auction;
            var cost = a.HighestBidAmount == 0 ? a.StartingBid : a.HighestBidAmount;
            if (!string.IsNullOrWhiteSpace(Settings.ModSettings?.Format))
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
                */
                return String.Format(Settings.ModSettings.Format,
                    finderType,
                    GetRarityColor(a.Tier),
                    flip.Auction.Context.ContainsKey("cname") ? flip.Auction.Context["cname"] : a.ItemName,
                    priceColor,
                    FormatPrice(cost),
                    FormatPrice(targetPrice), // this is {5}
                    FormatPrice(profit),
                    FormatPrice((profit * 100 / cost)),
                    FormatPrice(flip.MedianPrice),
                    FormatPrice(flip.LowestBin ?? 0),
                    flip.Volume.ToString("0.#")  // this is {10}
                );
            }
            var textAfterProfit = (Settings?.Visibility?.ProfitPercentage ?? false) ? $" {McColorCodes.DARK_RED}{FormatPrice((profit * 100 / cost))}%{priceColor}" : "";

            var builder = new StringBuilder(80);

            string itemName = flip.Auction?.Context?.ContainsKey("cname") ?? false ? flip.Auction.Context["cname"] : $"{GetRarityColor(a.Tier)}{a.ItemName}";

            builder.Append($"\n{finderType}: {itemName} {priceColor}{FormatPrice(cost)} -> {FormatPrice(targetPrice)} ");
            try
            {
                if ((Settings.Visibility?.Profit ?? false) || (Settings.Visibility?.EstimatedProfit ?? false))
                    builder.Append($"(+{FormatPrice(profit)}{textAfterProfit}) ");
                if (Settings.Visibility?.MedianPrice ?? false)
                    builder.Append(McColorCodes.GRAY + " Med: " + McColorCodes.AQUA + FormatPrice(flip.MedianPrice));
                if (Settings.Visibility?.LowestBin ?? false)
                    builder.Append(McColorCodes.GRAY + " LBin: " + McColorCodes.AQUA + FormatPrice(flip.LowestBin ?? 0));
                if (Settings.Visibility?.Volume ?? false)
                    builder.Append(McColorCodes.GRAY + " Vol: " + McColorCodes.AQUA + flip.Volume.ToString("0.#"));
            }
            catch (Exception e)
            {
                if (con == null)
                    throw new Exception("connection is null " + profit, e);
                if (Settings == null)
                    throw new Exception("settings are null " + profit, e);
                con.Log(e.ToString(), Microsoft.Extensions.Logging.LogLevel.Error);
                throw new Exception(e.ToString() + Environment.NewLine + JSON.Stringify(Settings), e);
            }
            return builder.ToString();
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
                Tier.SUPREME => "§4",
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
        public string FormatPrice(long price)
        {
            if (Settings?.ModSettings?.ShortNumbers ?? false)
                return FormatProvider.FormatPriceShort(price);
            return string.Format("{0:n0}", price);
        }


        public ChatPart[] WelcomeMessage()
        {
            var text = $"§fFound and loaded settings for your connection\n"
                                    + $"{McColorCodes.GRAY} MinProfit: {McColorCodes.AQUA}{FormatPrice(Settings.MinProfit)}  "
                                    + $"{McColorCodes.GRAY} MaxCost: {McColorCodes.AQUA}{FormatPrice(Settings.MaxCost)}"
                                    + $"{McColorCodes.GRAY} Blacklist-Size: {McColorCodes.AQUA}{Settings?.BlackList?.Count ?? 0}\n "
                                    + "§8: nothing else to do have a nice day :)";
            var hover = $"{McColorCodes.GRAY} Volume: {McColorCodes.AQUA}{Settings.MinVolume}\n"
                        + $"{McColorCodes.GRAY} MinProfitPercent: {McColorCodes.AQUA}{FormatPrice(Settings.MinProfitPercent)}";
            return new DialogBuilder().MsgLine(text, "https://sky.coflnet.com/flipper", hover)
                    .MsgLine($"{McColorCodes.AQUA}: click this if you want to change a setting", null, 
                            $"Opens the website.\nalternatively you can use the {McColorCodes.AQUA}/cofl set{McColorCodes.WHITE} command to change settings in game");
        }


        public string GetHoverText(FlipInstance flip)
        {
            if (Settings.Visibility.Lore)
                return flip.Auction?.Context?.GetValueOrDefault("lore");
            return string.Join('\n', flip.Interesting.Select(s => "・" + s)) + "\n" + flip.SellerName;
        }
    }
}
