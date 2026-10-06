using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class FoundModsCommand : McCommand
{
    /// <summary>
    /// Mods known to insert their own lines into auction house lore, which breaks the line replacement of the started time
    /// </summary>
    private static readonly string[] AuctionLoreMods = ["firmament"];

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var mods = JsonConvert.DeserializeObject<Response>(arguments);
        if (mods?.FileNames == null)
            return;
        var current = socket.AccountInfo?.CaptchaType;
        if (current != "optifine" && current != "vertical" && mods.FileNames.Any(n => n.ToLower().Contains("optifine")))
        {
            socket.AccountInfo.CaptchaType = "optifine";
            Activity.Current.Log("Changed captcha type because you use optifine");
            socket.Dialog(db => db.Msg("Changed captcha type because you use optifine"));
        }
        else
            Activity.Current.Log("No optifine found");
        socket.SessionInfo.ModsFound = mods.FileNames;
        if (mods.FileNames.Any(InsertsIntoAuctionLore))
            await DisableAuctionStartedTime(socket);
    }

    private static bool InsertsIntoAuctionLore(string fileName)
    {
        return fileName != null && AuctionLoreMods.Any(m => fileName.Contains(m, System.StringComparison.OrdinalIgnoreCase));
    }

    private static async Task DisableAuctionStartedTime(MinecraftSocket socket)
    {
        var userId = socket.sessionLifesycle?.UserId?.Value ?? socket.AccountInfo?.UserId;
        if (userId == null)
            return; // not logged in, no lore settings to adjust
        var service = socket.GetService<SettingsService>();
        var settings = await service.GetCurrentValue(userId, "description", () => DescriptionSetting.Default);
        if (settings == null || settings.DisableAuctionStartedTime)
            return;
        settings.DisableAuctionStartedTime = true;
        await service.UpdateSetting(userId, "description", settings);
        LoreCommand.ForgetCachedSettings(userId);
        Activity.Current.Log("Disabled auction started time because of incompatible mod");
        socket.Dialog(db => db.MsgLine($"{McColorCodes.YELLOW}Disabled the auction started time in lore because one of your mods also modifies auction lore")
            .CoflCommand<SetCommand>("[Click here to enable it again]", "loreDisableAuctionStartedTime false", "Enable auction started time again"));
    }

    public class Response
    {
        public string[] FileNames { get; set; }
        public string[] FileHashes { get; set; }
        public string[] ModNames { get; set; }
    }
}