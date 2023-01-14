using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Items.Client.Api;

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
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Contains('='))
                {
                    var filterParts = part.Split('=');
                    var filterName = allFilters.Where(f => f.ToLower() == filterParts[0].ToLower()).FirstOrDefault();
                    if (filterName == null)
                    {
                        filterName = allFilters.OrderBy(f => Fastenshtein.Levenshtein.Distance(f.ToLower(), filterParts[0].ToLower())).First();
                        socket.SendMessage(new DialogBuilder().MsgLine($"{McColorCodes.RED}Could not find {McColorCodes.AQUA}{filterParts[0]}{McColorCodes.WHITE}, using closest match {McColorCodes.AQUA}{filterName}{McColorCodes.WHITE} instead"));
                    }
                    var filterVal = filterParts[1];
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
                    val = val.Substring(0, val.Length - part.Length).Trim();
                }
            }

            return val;
        }

        private static string AssertValidOptionsFilter(string filterName, string filterVal, FilterType type, IEnumerable<object> optionsObjects)
        {
            var options = optionsObjects.Select(o => o.ToString()).ToList();
            if (options.Count > 1 && type.HasFlag(FilterType.Equal))
            {
                var match = options.Where(o => o == filterVal).FirstOrDefault();
                if (match == null)
                {
                    match = options.Where(o => o.ToLower() == filterVal.ToLower()).FirstOrDefault();
                    if (match != null)
                        return match;
                }
                var closestOption = options.OrderBy(f => Fastenshtein.Levenshtein.Distance(f.ToLower(), filterVal.ToLower())).First();
                throw new CoflnetException("invalid_value", $"The filter value {filterVal} did not match any option for {filterName}, {McColorCodes.WHITE}maybe you meant {McColorCodes.AQUA}{closestOption}");
            }
            return filterVal;
        }

        private static void AssertValidNumberFilter(string filterName, string filterVal, FilterType type)
        {
            if (type.HasFlag(Filter.FilterType.NUMERICAL) && !NumberDetailedFlipFilter.IsValidInput(filterVal))
                throw new Coflnet.Sky.Core.CoflnetException("invalid_value", $"The provided filter value {filterVal} is not valid for {filterName}");
        }
    }
}