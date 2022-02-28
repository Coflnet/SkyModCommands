namespace Coflnet.Sky.Commands.MC
{
    public class InventoryModSession : ModSessionLifesycle
    {
        public InventoryModSession(MinecraftSocket socket) : base(socket)
        {
            socket.Send(Response.Create("privacySettings", new PrivacySettings()
            {
                CollectInventory = true,
                ExtendDescriptions = true
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
