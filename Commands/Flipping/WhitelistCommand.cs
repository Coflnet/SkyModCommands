using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Manage your whitelist", 
        "to add use /cl wl add <item> [filterName=Value]",
        "Allows you to skip entries on your blacklist",
        "Whitelist only things you definetly want to see",
        "Example /cl wl add Hyperion StartingBid=<50m")]
    public class WhitelistCommand : BlacklistCommand
    {
        public override bool IsPublic => true;
        protected override async Task Update(MinecraftSocket socket, List<ListEntry> newCol)
        {
            var list = GetSettings(socket);
            list.Value.WhiteList = newCol;
            list.Value.LastChanged = null;
            await list.Update(list.Value);
        }

        protected override Task<List<ListEntry>> GetList(MinecraftSocket socket)
        {
            SelfUpdatingValue<FlipSettings> settings = GetSettings(socket);
            return Task.FromResult(settings.Value.WhiteList);
        }
    }
}