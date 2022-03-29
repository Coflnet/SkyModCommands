using System;

namespace Coflnet.Sky.Commands.MC
{
    public class SessionInfo
    {
        public string sessionId = "";
        public bool ListeningToChat;
        public string McName;
        public string McUuid = "00000000000000000";
        public DateTime LastMessage;
        public DateTime MutedUntil;
        public bool SentWelcome;
        /// <summary>
        /// Speed penalty for various bad actions eg botting
        /// </summary>
        public TimeSpan Penalty;
    }
}
