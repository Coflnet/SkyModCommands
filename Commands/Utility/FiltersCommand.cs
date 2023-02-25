using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class FiltersCommand : ReadOnlyListCommand<FilterOptions>
    {
        public override bool IsPublic => true;
        protected override void Format(MinecraftSocket socket, DialogBuilder db, FilterOptions elem)
        {
            var hover = $"Sample options: \n{McColorCodes.YELLOW + string.Join(",\n", elem.Options.Batch(10).Select(o => string.Join(McColorCodes.GRAY + ", " + McColorCodes.YELLOW, o)))}";
            if (elem.Type.HasFlag(FilterType.NUMERICAL))
            {
                hover = $"Min: {elem.Options.First()} Max: {elem.Options.Last()}";
                if (elem.Type.HasFlag(FilterType.RANGE))
                    hover += $"\n{McColorCodes.GREEN} supports ranges{McColorCodes.RESET}\neg. \"1-10\" or \">2\"";
            }
            db.MsgLine($"{McColorCodes.GRAY}>{McColorCodes.YELLOW}{elem.Name} {McColorCodes.GRAY}{elem.Description}", null, hover);
        }

        protected override async Task<IEnumerable<FilterOptions>> GetElements(MinecraftSocket socket, string val)
        {
            var itemsApi = socket.GetService<IItemsApi>();
            var fe = new Sky.Filter.FilterEngine();
            var optionsTask = itemsApi.ItemItemTagModifiersAllGetAsync("*");
            socket.SendMessage("Loading all filters with all options, this may take a while");
            var all = await optionsTask;
            IEnumerable<FilterOptions> extraFilters = GetOptionsForDetailedFlipFilters();
            return fe.AvailableFilters.Where(f =>
            {
                try
                {
                    var options = f.OptionsGet(new OptionValues(all));
                    return true;
                }
                catch (System.Exception e)
                {
                    dev.Logger.Instance.Error(e, "retrieving filter options");
                    return false;
                }
            }).Select(f => new FilterOptions(f, all))
            .Concat(extraFilters.Where(f => f != null))
            .ToList();

        }

        private static IEnumerable<FilterOptions> GetOptionsForDetailedFlipFilters()
        {
            return FlipFilter.AdditionalFilters.Select(f =>
            {
                try
                {
                    var description = (f.Value.GetType().GetCustomAttributes(typeof(FilterDescriptionAttribute), true).FirstOrDefault() as FilterDescriptionAttribute);
                    if (description != null)
                        System.Console.WriteLine(description.Description);
                    return new FilterOptions()
                    {
                        Name = f.Key,
                        Options = f.Value.Options.Select(o => o.ToString()).ToList(),
                        Type = f.Value.FilterType,
                        LongType = f.Value.FilterType.ToString(),
                        Description = description?.Description
                    };
                }
                catch (System.Exception e)
                {
                    dev.Logger.Instance.Error(e, "retrieving detailed filter");
                    return null;
                }
            });
        }

        protected override string GetId(FilterOptions elem)
        {
            return elem.Name;
        }
    }

    static class LinqExtensions
    {
        /// <summary>
        /// Split a List into chunks of a given size.
        /// From https://stackoverflow.com/a/13710023
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="batchSize"></param>
        /// <returns></returns>
        public static IEnumerable<IEnumerable<T>> Batch<T>(
            this IEnumerable<T> source, int batchSize)
        {
            using (var enumerator = source.GetEnumerator())
                while (enumerator.MoveNext())
                    yield return YieldBatchElements(enumerator, batchSize - 1);
        }

        private static IEnumerable<T> YieldBatchElements<T>(
            IEnumerator<T> source, int batchSize)
        {
            yield return source.Current;
            for (int i = 0; i < batchSize && source.MoveNext(); i++)
                yield return source.Current;
        }
    }

}