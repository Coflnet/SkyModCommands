namespace Coflnet.Sky.Commands.MC
{
    public class InventoryModSession : ModSessionLifesycle
    {
        public InventoryModSession(MinecraftSocket socket) : base(socket)
        {
            socket.Send(Response.Create("privacySettings", new PrivacySettings()
            {
                CollectInventory = true,
                ExtendDescriptions = true,
                ChatRegex = "(�r�eSell Offer|�r�6[Bazaar]|�r�cCancelled|�r�6Bazaar!|�r�eYou collected|�6[Auction]|�r�eBIN Auction started|�r�eYou �r�ccancelled|[Test]).*",
                CollectChat = true,
                CollectScoreboard = true,
                CollectChatClicks = true,
                CommandPrefixes = new string[] { "/cofl", "/cofl", "/ch" }
            }));
        }
    }

    public class PrivacySettings
    {
        public string ChatRegex;
        public bool CollectChat;
        public bool CollectInventory;
        public bool CollectTab;
        public bool CollectScoreboard;
        public bool AllowProxy;
        public bool CollectInvClick;
        public bool CollectChatClicks;
        public bool CollectLobbyChanges;
        public bool CollectEntities;
        /// <summary>
        /// Wherever or not to send item descriptions for extending to the server
        /// </summary>
        public bool ExtendDescriptions;
        /// <summary>
        /// Chat input starting with one of these prefixes is sent to the server
        /// </summary>
        public string[] CommandPrefixes;
    }
}
