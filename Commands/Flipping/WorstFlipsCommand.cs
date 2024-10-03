using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Worst flips of x days")]
    public class WorstFlipsCommand : BestFlipsCommand
    {
        protected override string word => "worst";
        protected override List<FlipDetails> Sort(FlipSumary response)
        {
            return response.Flips.OrderBy(f => f.Profit).ToList();
        }
    }
}