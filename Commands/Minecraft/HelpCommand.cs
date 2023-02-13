using System;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;
public class HelpCommand : McCommand
{
    public override bool IsPublic => true;
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        switch (arguments.Trim('"'))
        {
            case "login":
                socket.Dialog(db => db.MsgLine("The mod asks you to login to save and restore your settings.")
                    .MsgLine("The login link connects the minecraft account you are currently running to whatever email you choose to login with.")
                    .MsgLine("To ultilize more feature you may need to verify your minecraft account").CoflCommand<HelpCommand>(McColorCodes.AQUA + "more about verifying", "verify", "prints more help"));
                break;
            case "verify":
                socket.Dialog(db => db.MsgLine(McColorCodes.GREEN + "Verifying your account makes sure you are in control of it. That is to check that you are using it in online mode and aren't an impersonator.")
                    .Msg("For this you have to bid a specific amount of coins on any auction on the auction house.")
                    .MsgLine("This allows us to check your bid amount via the api and verify that you have control of the minecraft account.")
                    .MsgLine("The alternative was to use the minecraft login system. That would require the mod to create a session login which would get users worried about their session id being stolen.")
                    .MsgLine("Verification is required to make sure that you have ownership of the account. You get 1 free day of premium for verifying your account."));
                break;
            default:
                socket.ExecuteCommand("/cofl");
                break;
        }
        return Task.CompletedTask;
    }
}
