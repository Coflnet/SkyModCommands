using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class WhitelistCommand : BlacklistCommand
    {
        public override bool IsPublic => true;
        protected override async Task Update(MinecraftSocket socket, List<ListEntry> newCol)
        {
            var list = GetSettings(socket);
            list.Value.WhiteList = newCol;
            await list.Update(list.Value);
        }

        protected override Task<List<ListEntry>> GetList(MinecraftSocket socket)
        {
            SelfUpdatingValue<FlipSettings> settings = GetSettings(socket);
            return Task.FromResult(settings.Value.WhiteList);
        }
    }
}