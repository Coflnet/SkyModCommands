using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class InventoryModSession : ModSessionLifesycle
    {
        public const string DefaultChatRegex =
                @"^(You cannot view this auction!|You claimed|\[Bazaar\]|�r�cCancelled|�r�6Bazaar!"
                + @"|You collected|�6[Auction]|BIN Auction started|You cancelled|You purchased "
                + @"| - | \+ |Trade completed|Bid of|\nClick the link to ).*";

        public InventoryModSession(MinecraftSocket socket) : base(socket)
        {

        }

        protected override async Task SubToSettings(string val)
        {
            await base.SubToSettings(val);
            socket.sessionLifesycle.PrivacySettings = await SelfUpdatingValue<PrivacySettings>.Create(val, "privacySettings", () =>
            {
                return new PrivacySettings()
                {
                    CollectInventory = true,
                    ExtendDescriptions = true,
                    ChatRegex = DefaultChatRegex,
                    CollectChat = true,
                    CollectScoreboard = true,
                    CollectChatClicks = true,
                    CommandPrefixes = new string[] { "/cofl", "/colf", "/ch" },
                    AutoStart = true,
                    CollectTab = true,
                    AllowProxy = true,
                    CollectLobbyChanges = true,
                    CollectInvClick = true,
                    CollectEntities = true
                };
            });
            socket.sessionLifesycle.PrivacySettings.AfterChange -= UpdatePrivacySettings;
            socket.sessionLifesycle.PrivacySettings.AfterChange += UpdatePrivacySettings;
            if (socket.sessionLifesycle.PrivacySettings.Value.ChatRegex != DefaultChatRegex)
                socket.sessionLifesycle.PrivacySettings.Value.ChatRegex = DefaultChatRegex;
            UpdatePrivacySettings(socket.sessionLifesycle.PrivacySettings.Value);
            //if(isDefault)
            //    await socket.sessionLifesycle.PrivacySettings.Update();
        }

        private void UpdatePrivacySettings(PrivacySettings settings)
        {
            socket.Send(Response.Create("privacySettings", settings));
        }
    }
}
