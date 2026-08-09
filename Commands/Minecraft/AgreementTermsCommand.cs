using System;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Review and accept the current SkyCofl agreement")]
public class AgreementTermsCommand : McCommand
{
    public override bool IsPublic => true;

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var parts = arguments.Trim('"').Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            CurrentAgreement.Ask(socket);
            return;
        }

        await CurrentAgreement.Accept(
            socket,
            parts[0],
            parts.Length > 1 ? parts[1] : null);
        socket.Dialog(dialog => dialog.MsgLine(
            "The current SkyCofl agreement package was accepted."));
    }
}
