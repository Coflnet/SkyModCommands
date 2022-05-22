using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class BlacklistCommand : ListCommand<ListEntry, List<ListEntry>>
    {
        protected override string Format(ListEntry elem)
        {
            return FormatEntry(elem);
        }

        public static string FormatEntry(ListEntry elem)
        {
            return $"{elem.DisplayName ?? elem.ItemTag} {(elem.filter == null ? "" : string.Join(" & ", elem.filter.Select(f => $"{McColorCodes.AQUA}{f.Key}{DEFAULT_COLOR}={McColorCodes.GREEN}{f.Value}")))}";
        }

        protected override string LongFormat(ListEntry elem)
        {
            return Format(elem) + $"\nTag: {elem.ItemTag ?? McColorCodes.BOLD + "all flips are affected by this"}";
        }

        protected override string GetId(ListEntry elem)
        {
            return $"{elem.ItemTag} {(elem.filter == null ? "" : string.Join(',', elem.filter.Select(f => $"{f.Key}={f.Value}")))}";
        }

        protected override Task<List<ListEntry>> GetList(MinecraftSocket socket)
        {
            SelfUpdatingValue<FlipSettings> settings = GetSettings(socket);
            return Task.FromResult(settings.Value.BlackList);
        }

        protected SelfUpdatingValue<FlipSettings> GetSettings(MinecraftSocket socket)
        {
            var settings = socket.sessionLifesycle.FlipSettings;
            if (settings.Value == null)
                throw new Coflnet.Sky.Core.CoflnetException("login", "Login is required to use this command");
            if (settings.Value.BlackList == null)
                settings.Value.BlackList = new System.Collections.Generic.List<ListEntry>();
            if (settings.Value.WhiteList == null)
                settings.Value.WhiteList = new System.Collections.Generic.List<ListEntry>();
            return settings;
        }

        protected override async Task Update(MinecraftSocket socket, List<ListEntry> newCol)
        {
            var list = GetSettings(socket);
            list.Value.BlackList = newCol;
            await list.Update(list.Value);
        }

        protected override async Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
        {
            var filters = new Dictionary<string, string>();
            var allFilters = FlipFilter.AllFilters;
            if (val.Contains('='))
            {
                val = ParseFilters(socket, val, filters, allFilters);
            }
            List<Items.Client.Model.SearchResult> result = new List<Items.Client.Model.SearchResult>();
            if (val.Length < 1)
            {
                // filter only element
                result.Add(new Items.Client.Model.SearchResult());
            }
            else
                result = await socket.GetService<Items.Client.Api.IItemsApi>().ItemsSearchTermGetAsync(val);
            var isTag = val.ToUpper() == val && !val.Contains(' ');

            return result.Select(r =>
            {
                var entry = new ListEntry() { ItemTag = r.Tag, DisplayName = r.Text, filter = filters };
                entry.GetExpression().Compile().Invoke(new FlipInstance()
                {
                    Auction = new Core.SaveAuction()
                    {
                        ItemName = "test",
                        Tag = r.Tag,
                        NBTLookup = new List<Core.NBTLookup>(),
                        FlatenedNBT = new Dictionary<string, string>()
                    },

                });

                return new CreationOption()
                {
                    Element = entry
                };
            }).Where(e => !isTag || string.IsNullOrEmpty(val) || e.Element.ItemTag == val);
        }

        /// <summary>
        /// Parses filters from string
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="val"></param>
        /// <param name="filters"></param>
        /// <param name="allFilters"></param>
        /// <returns></returns>
        private static string ParseFilters(MinecraftSocket socket, string val, Dictionary<string, string> filters, IEnumerable<string> allFilters)
        {
            // has filter
            var parts = val.Split(' ').Reverse().ToList();
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Contains('='))
                {
                    var filterParts = part.Split('=');
                    var filterName = allFilters.Where(f => f.ToLower() == filterParts[0]).FirstOrDefault();
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
                    else
                    {
                        var filter = FlipFilter.FilterEngine.AvailableFilters.Where(f => f.Name == filterName).First();
                        var type = filter.FilterType;
                        var options = filter.Options;
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

        protected override async Task<ListEntry> UpdateElem(MinecraftSocket socket, ListEntry current, string args)
        {
            var filterList = FlipFilter.AllFilters;

            return current;
        }
    }
}