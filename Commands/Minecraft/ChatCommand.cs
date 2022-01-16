using System;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class ChatCommand : McCommand
    {
        static ChatService chat = new ChatService();
        public static string CHAT_PREFIX = "[§1C§6hat§f]";
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var maxMsgLength = 150;
            var message = JsonConvert.DeserializeObject<string>(arguments);
            await MakeSureChatIsConnected(socket);
            if (DateTime.Now - TimeSpan.FromSeconds(1) < socket.sessionInfo.LastMessage)
            {
                socket.SendMessage(COFLNET + "You are writing to fast please slow down");
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
                SenderName = socket.McId,
                Tier = socket.LatestSettings.Tier
            });
        }

        public static async Task MakeSureChatIsConnected(MinecraftSocket socket)
        {
            if (!socket.sessionInfo.ListeningToChat)
            {
                await chat.Subscribe(m =>
                {
                    var color = ((int)m.Tier) > 0 ? McColorCodes.DARK_GREEN : McColorCodes.WHITE;
                    return socket.SendMessage(
                        new ChatPart($"{CHAT_PREFIX} {color}{m.SenderName}{McColorCodes.WHITE}: {m.Message}", $"/cofl dialog chatreport {m.SenderName} {m.Message}", "click to report message"),
                        new ChatPart("", "/cofl void"));
                });
                socket.sessionInfo.ListeningToChat = true;
            }
        }
    }

    public class CCommand : ChatCommand
    {

    }


}