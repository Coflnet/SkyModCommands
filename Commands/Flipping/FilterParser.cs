using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Items.Client.Api;
using System.Text.RegularExpressions;

namespace Coflnet.Sky.Commands.MC
{
    public class FilterParser
    {
        /// <summary>
        /// Parses filters from string
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="val"></param>
        /// <param name="filters"></param>
        /// <param name="allFilters"></param>
        /// <returns></returns>
        public async Task<string> ParseFiltersAsync(MinecraftSocket socket, string val, Dictionary<string, string> filters, IEnumerable<string> allFilters)
        {
            // has filter
            var parts = val.Split(' ').Reverse().ToList();
            var filterMatches = Regex.Matches(val, @"([^ ]+)=([^="" \n]+|""[^""]+"")");
            foreach (var match in filterMatches.Cast<Match>())
            {
                var key = match.Groups[1].Value.ToLower();
                var filterVal = match.Groups[2].Value.Trim('"');

                var filterName = allFilters.Where(f => f.ToLower() == key).FirstOrDefault();
                if (filterName == null)
                {
                    filterName = allFilters.OrderBy(f => Fastenshtein.Levenshtein.Distance(f.ToLower(), key)).First();
                    socket.SendMessage(new DialogBuilder().MsgLine($"{McColorCodes.RED}Could not find {McColorCodes.AQUA}{match.Groups[1].Value}{McColorCodes.WHITE}, using closest match {McColorCodes.AQUA}{filterName}{McColorCodes.WHITE} instead"));
                }
                if (FlipFilter.AdditionalFilters.TryGetValue(filterName, out DetailedFlipFilter dff))
                {
                    var type = dff.FilterType;
                    var options = dff.Options;
                    AssertValidNumberFilter(filterName, filterVal, type);
                    filterVal = AssertValidOptionsFilter(filterName, filterVal, type, options);
                }
                else if (filterName == "removeAfter")
                {
                    // is parseable
                    DateTime.Parse(filterVal);
                }
                else if (filterName == "duration")
                {
                    // tested further up
                }
                else if (filterName.ToLower() == "seller")
                {
                    if (filterVal.Length < 30)
                    {
                        var uuid = await socket.GetPlayerUuid(filterVal);
                        if (!string.IsNullOrEmpty(uuid))
                            filterVal = uuid;
                    }
                }
                else if (filterName == "tag" || filterName == "tags") { }
                else
                {
                    var allOptions = await DiHandler.GetService<IItemsApi>().ItemItemTagModifiersAllGetAsync("*");
                    var filter = FlipFilter.FilterEngine.AvailableFilters.Where(f => f.Name == filterName).First();
                    var type = filter.FilterType;
                    var options = filter.OptionsGet(new OptionValues(allOptions));
                    AssertValidNumberFilter(filterName, filterVal, type);
                    filterVal = AssertValidOptionsFilter(filterName, filterVal, type, options);
                }
                filters.Add(filterName, filterVal);
                // remove filter from search
                val = val.Replace(match.Value, "").Trim();
            }

            return val;
        }

        private static string AssertValidOptionsFilter(string filterName, string filterVal, FilterType type, IEnumerable<object> optionsObjects)
        {
            var options = optionsObjects.Select(o => o.ToString()).ToList();
            if (options.Count > 1 && type.HasFlag(FilterType.Equal))
            {
                var match = options.Where(o => o.ToLower() == filterVal.ToLower()).FirstOrDefault();
                if (match != null)
                    return match;
                var closestOption = options.OrderBy(f => Fastenshtein.Levenshtein.Distance(f.ToLower(), filterVal.ToLower())).First();
                throw new CoflnetException("invalid_value", $"The filter value {filterVal} did not match any option for {filterName}, {McColorCodes.WHITE}maybe you meant {McColorCodes.AQUA}{closestOption}.");
            }
            return filterVal;
        }

        private static void AssertValidNumberFilter(string filterName, string filterVal, FilterType type)
        {
            if (type.HasFlag(FilterType.NUMERICAL) && !IsValidInput(filterVal))
                throw new CoflnetException("invalid_value", $"The provided filter value {filterVal} is not valid for {filterName}");
        }

        private static bool IsValidInput(string input)
        {
            return NumberParser.TryDouble(input.Replace("<", "").Replace(">", "").Split("-")[0], out _);
        }
    }
}