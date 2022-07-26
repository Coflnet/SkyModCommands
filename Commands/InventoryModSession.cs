using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class InventoryModSession : ModSessionLifesycle
    {
        private const string DefaultChatRegex = @"^(You cannot view this auction!|�r�eSell Offer|�r�6[Bazaar]|�r�cCancelled|�r�6Bazaar!|�r�eYou collected|�6[Auction]|�r�eBIN Auction started|�r�eYou �r�ccancelled|[Test]| - | \+ |Trade completed).*";

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
