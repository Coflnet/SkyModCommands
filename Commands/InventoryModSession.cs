using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class InventoryModSession : ModSessionLifesycle
{
    /// <summary>Set by /cofl debug to upload all chat for the current session.</summary>
    internal const string DebugChatRegex = ".*";

    public InventoryModSession(MinecraftSocket socket) : base(socket)
    {

    }

    protected override async Task SubToSettings(string val)
    {
        if (IsReleased)
            return;
        await base.SubToSettings(val);
        await SubToPrivacySettings(val);
    }

    internal async Task SubToPrivacySettings(string val)
    {
        if (IsReleased)
            return;
        var settings = await SelfUpdatingValue<PrivacySettings>.Create(val, "privacySettings", () =>
        {
            return Shared.PrivacySettings.Default;
        });
        if (!TryAdopt(ref PrivacySettings, settings))
            return;
        socket.sessionLifesycle.PrivacySettings.AfterChange -= UpdatePrivacySettings;
        socket.sessionLifesycle.PrivacySettings.AfterChange += UpdatePrivacySettings;
        var stored = socket.sessionLifesycle.PrivacySettings.Value;
        // a debug ".*" from an earlier session must not keep uploading all chat after a reconnect
        NormalizePrivacySettings(stored, keepDebugRegex: false);
        socket.Send(Response.Create("privacySettings", stored));
    }

    private void UpdatePrivacySettings(PrivacySettings settings)
    {
        NormalizePrivacySettings(settings, keepDebugRegex: true);
        socket.Send(Response.Create("privacySettings", settings));
    }

    /// <summary>
    /// Replaces any stored (possibly historic) chat regex with the current default, so writing back old stored
    /// settings never downgrades the filter. Only the debug ".*" survives, and only within the session that set it.
    /// </summary>
    internal static void NormalizePrivacySettings(PrivacySettings settings, bool keepDebugRegex)
    {
        if (!keepDebugRegex || settings.ChatRegex != DebugChatRegex)
            settings.ChatRegex = Shared.PrivacySettings.DefaultChatRegex;
        settings.ChatBlockRegex = settings.NoMessageBlocking ? "$^" : Shared.PrivacySettings.DefaultChatBlockRegex;
    }
}
