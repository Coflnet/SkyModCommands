namespace Coflnet.Sky.Commands.MC
{
    public class ChatPart
    {
        public string text;
        public string onClick;
        public string hover;

        public ChatPart()
        {
        }

        public ChatPart(string text, string onClick = null, string hover = null)
        {
            this.text = text;
            this.onClick = onClick;
            this.hover = hover;
        }

    }
}