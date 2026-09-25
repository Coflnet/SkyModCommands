using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Addon removed")]
    public class RustAddonCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {   
            socket.Dialog(d => d.MsgLine("The Rust Finder is superseeded by updates to the Median and AI finder."));
        }
    }
}