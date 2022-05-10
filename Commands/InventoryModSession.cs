using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class InventoryModSession : ModSessionLifesycle
    {
        public InventoryModSession(MinecraftSocket socket) : base(socket)
        {

        }

        protected override async Task SubToSettings(string val)
        {
            await base.SubToSettings(val);
            socket.sessionLifesycle.PrivacySettings = await SelfUpdatingValue<PrivacySettings>.Create(val, "privacySettings", () => new PrivacySettings()
            {
                CollectInventory = true,
                ExtendDescriptions = true,
                ChatRegex = "^(�r�eSell Offer|�r�6[Bazaar]|�r�cCancelled|�r�6Bazaar!|�r�eYou collected|�6[Auction]|�r�eBIN Auction started|�r�eYou �r�ccancelled|[Test]| - | + |Trade completed).*",
                CollectChat = true,
                CollectScoreboard = true,
                CollectChatClicks = true,
                CommandPrefixes = new string[] { "/cofl", "/colf", "/ch" },
                AutoStart = true
            });
        }
    }
}
