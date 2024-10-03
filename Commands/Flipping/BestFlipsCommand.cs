using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Best flips")]
    public class BestFlipsCommand : ProfitCommand
    {
        protected virtual string word => "best";
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = JsonConvert.DeserializeObject<string>(arguments).Split(' ');
            TimeSpan time = await GetTimeSpan(socket, arguments, args);
            // replace this call with stored socket.sessionLifesycle.AccountInfo.Value.McIds

            IEnumerable<string> accounts = await GetAccounts(socket, args);
            Task<Dictionary<string, string>> namesTask = GetNames(socket, accounts);
            var response = await socket.GetService<FlipTrackingService>().GetPlayerFlips(accounts, time);

            List<FlipDetails> sorted = Sort(response);
            var names = namesTask == null ? null : await namesTask;
            socket.Dialog(db => db.MsgLine($"The {word} flips were:")
                .ForEach(sorted.Take(3), (db, f) => db.MsgLine($"{FormatFlipName(socket, f)}", null, FormatFlip(socket, f) + (names == null ? "" : $"by {McColorCodes.AQUA}{names[f.Seller]}"))));
        }

        protected virtual List<FlipDetails> Sort(FlipSumary response)
        {
            return response.Flips.OrderByDescending(f => f.Profit).ToList();
        }
    }
}