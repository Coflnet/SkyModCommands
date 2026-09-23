namespace Coflnet.Sky.Commands.MC
{
    /// <summary>Payload for the infoDisplay websocket message. Slots are 1-3.</summary>
    public class InfoDisplay
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public ChatPart[] Lines { get; set; }
        /// <summary>Lifetime in seconds; zero keeps the content until replaced or cleared.</summary>
        public int Ttl { get; set; }
        public bool Clear { get; set; }
    }
}
