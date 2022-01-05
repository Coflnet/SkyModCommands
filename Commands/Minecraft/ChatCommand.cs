using System;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC
{
    public class ChatCommand : McCommand
    {
        static ChatService chat = new ChatService();
        public static string CHAT_PREFIX = "[§1C§6hat§f]";
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (!socket.sessionInfo.ListeningToChat)
            {
                chat.Subscribe(m =>
                {
                    var color = ((int)m.Tier) > 0 ? McColorCodes.DARK_GREEN : McColorCodes.WHITE;
                    return socket.SendMessage(
                        new ChatPart($"{CHAT_PREFIX} {color}{m.SenderName}{McColorCodes.WHITE}: {m.Message}", $"/cofl dialog chatreport {m.SenderName} {m.Message}", "click to report message"),
                        new ChatPart("","/cofl void"));
                });
                socket.sessionInfo.ListeningToChat = true;
            }
            if(DateTime.Now - TimeSpan.FromSeconds(1) < socket.sessionInfo.LastMessage )
            {
                socket.SendMessage(COFLNET + "You are writing to fast please slow down");
                return;
            }
            await chat.Send(new ChatService.ModChatMessage()
            {
                Message = arguments.Trim('"'),
                SenderName = socket.McId,
                Tier = socket.LatestSettings.Tier
            });
        }
    }


}