using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class BlacklistCommand : ListCommand<ListEntry, List<ListEntry>>
    {
        protected override string Format(ListEntry elem)
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
            var result = await socket.GetService<Items.Client.Api.IItemsApi>().ItemsSearchTermGetAsync(val);

            return result.Select(r => new CreationOption()
            {
                Element = new ListEntry() { ItemTag = r.Tag, DisplayName = r.Text }
            });
        }
    }
}