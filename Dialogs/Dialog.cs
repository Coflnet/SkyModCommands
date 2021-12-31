using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public abstract class Dialog
    {
        public abstract ChatPart[] GetResponse(string context);

        protected DialogBuilder New()
        {
            return new DialogBuilder();
        }
    }
}
