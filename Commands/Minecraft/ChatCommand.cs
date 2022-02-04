using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class ChatCommand : McCommand
    {
        static ChatService chat = new ChatService();
        public static string CHAT_PREFIX = "[§1C§6hat§f]";
        private static string[] BadWords = new string[]{"my ah", "nigger"};
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var maxMsgLength = 150;
            var message = JsonConvert.DeserializeObject<string>(arguments);
            await MakeSureChatIsConnected(socket);
            if (DateTime.Now - TimeSpan.FromSeconds(1) < socket.SessionInfo.LastMessage)
            {
                socket.SendMessage(COFLNET + "You are writing to fast please slow down");
                return;
            }
            if(DateTime.Now > socket.SessionInfo.MutedUntil)
            {
                socket.SendMessage(COFLNET + $"You are muted for {(int)(socket.SessionInfo.MutedUntil-DateTime.Now).TotalMinutes +1} minutes");
                return;
            }
            if(BadWords.Any(w=>message.ToLower().Contains(w)))
            {
                socket.SendMessage(COFLNET + $"Your message violated either rule 1 or rule 2. Please don't violate any rules. You are muted for 1 hour.",null,"1. Be nice\n2. Don't advertise something nobody asked for");
                socket.SessionInfo.MutedUntil = DateTime.Now + TimeSpan.FromHours(1);
                return;
            }
            if (message.Length > maxMsgLength)
            {
                socket.SendMessage(COFLNET + "Please use another chat for long messages", null, $"Messages over {maxMsgLength} characters are blocked");
                return;
            }
            await chat.Send(new ChatService.ModChatMessage()
            {
                Message = message,
                SenderName = socket.SessionInfo.McName,
                Tier = socket.LatestSettings.Tier
            });
            socket.SessionInfo.LastMessage = DateTime.Now;
        }

        public static async Task MakeSureChatIsConnected(MinecraftSocket socket)
        {
            if (!socket.SessionInfo.ListeningToChat)
            {
                await chat.Subscribe(m =>
                {
                    var color = ((int)m.Tier) > 0 ? McColorCodes.DARK_GREEN : McColorCodes.WHITE;
                    return socket.SendMessage(
                        new ChatPart($"{CHAT_PREFIX} {color}{m.SenderName}{McColorCodes.WHITE}: {m.Message}", $"/cofl dialog chatreport {m.SenderName} {m.Message}", "click to report message"),
                        new ChatPart("", "/cofl void"));
                });
                socket.SessionInfo.ListeningToChat = true;
            }
        }
    }

    public class CCommand : ChatCommand
    {

    }


}