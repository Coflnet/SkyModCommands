using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class InventoryModSession : ModSessionLifesycle
    {
        public const string DefaultChatRegex =
                @"^(You cannot view this auction!|You claimed|\[Bazaar\]|\[NPC\] Kat|Cancelled"
                + @"|You collected|\[Auction\]|BIN Auction started|You cancelled|You purchased "
                + @"|Profile ID: |You placd a Trap|\+\d+ .* Attribute \(Level "
                + @"|You caught |\s+Chameleon" // catching shards
                + @"|Added items|Removed items" // stash adding notification
                + @"|You donated your" // museum donation
                + @"|: \d+m$" // chat lowballing discussion
                + @"| - | \+ |Trade completed|Bid of|\nClick the link to |\nClick th' li|You must set it to at least).*";
        public const string DefaultChatBlockRegex =
            @"^(You tipped ).*";

        public InventoryModSession(MinecraftSocket socket) : base(socket)
        {

        }

        protected override async Task SubToSettings(string val)
        {
            if (socket.IsClosed)
                return;
            await base.SubToSettings(val);
            var settings = await SelfUpdatingValue<PrivacySettings>.Create(val, "privacySettings", () =>
            {
                return new PrivacySettings()
                {
                    CollectInventory = true,
                    ExtendDescriptions = true,
                    ChatRegex = DefaultChatRegex,
                    ChatBlockRegex = DefaultChatBlockRegex,
                    CollectChat = true,
                    CollectScoreboard = true,
                    CollectChatClicks = true,
                    CommandPrefixes = new string[] { "/cofl", "/colf", "/ch" },
                    AutoStart = true,
                    CollectTab = true,
                    AllowProxy = true
                };
            });
            if (socket.IsClosed)
            {
                settings.Dispose();
                return;
            }
            socket.sessionLifesycle.PrivacySettings?.Dispose();
            socket.sessionLifesycle.PrivacySettings = settings;
            socket.sessionLifesycle.PrivacySettings.AfterChange -= UpdatePrivacySettings;
            socket.sessionLifesycle.PrivacySettings.AfterChange += UpdatePrivacySettings;
            var privacySettings = socket.sessionLifesycle.PrivacySettings.Value;
            if (privacySettings.ChatRegex != DefaultChatRegex)
                privacySettings.ChatRegex = DefaultChatRegex;

            if (!privacySettings.NoMessageBlocking && privacySettings.ChatBlockRegex != DefaultChatBlockRegex)
                privacySettings.ChatBlockRegex = DefaultChatBlockRegex;
            else if (privacySettings.NoMessageBlocking)
                privacySettings.ChatBlockRegex = "$^"; //matches nothing
            UpdatePrivacySettings(privacySettings);
            //if(isDefault)
            //    await socket.sessionLifesycle.PrivacySettings.Update();
        }

        private void UpdatePrivacySettings(PrivacySettings settings)
        {
            socket.Send(Response.Create("privacySettings", settings));
        }
    }
}
