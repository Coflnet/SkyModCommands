using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class ChatCommand : McCommand
    {
        static ChatService chat;
        public static string CHAT_PREFIX = "[§1C§6hat§f]";
        private static string[] BadWords = new string[] { "my ah", "nigger" };
        private static HashSet<string> MutedUsers = new HashSet<string>() { "850cfa6e7f184ed4b72a8c304734bcbe" };

        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (MutedUsers.Contains(socket.SessionInfo.McUuid))
            {
                socket.SendMessage(COFLNET + "You have been muted from the chat because you repeadetly violated the rules", "I am blocked from the Coflnet chat :(", $"Click to express your sadness");
                return;
            }
            var maxMsgLength = 150;
            var message = JsonConvert.DeserializeObject<string>(arguments);
            await MakeSureChatIsConnected(socket);
            if (DateTime.Now - TimeSpan.FromSeconds(1) < socket.SessionInfo.LastMessage)
            {
                socket.SendMessage(COFLNET + "You are writing to fast please slow down");
                return;
            }
            if (DateTime.Now < socket.SessionInfo.MutedUntil)
            {
                socket.SendMessage(COFLNET + $"You are muted for {(int)(socket.SessionInfo.MutedUntil - DateTime.Now).TotalMinutes + 1} minutes");
                return;
            }
            if (BadWords.Any(w => message.ToLower().Contains(w)))
            {
                socket.SendMessage(COFLNET + $"Your message violated either rule 1 or rule 2. Please don't violate any rules. You are muted for 1 hour.", null, "1. Be nice\n2. Don't advertise something nobody asked for");
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
                Tier = socket.sessionLifesycle.AccountInfo?.Value?.Tier ?? hypixel.AccountTier.NONE,
                SenderUuid = socket.SessionInfo.McUuid
            });
            socket.SessionInfo.LastMessage = DateTime.Now;
        }

        public static async Task MakeSureChatIsConnected(MinecraftSocket socket)
        {
            if (!socket.SessionInfo.ListeningToChat)
            {
                if(chat == null)
                {
                    chat = socket.GetService<ChatService>();
                }
                var sub = await chat.Subscribe(m =>
                {
                    try
                    {

                        if (socket.Settings?.BlackList?.Any(b => b.filter.Where(f => f.Key == "Seller" && f.Value == m.SenderUuid).Any()) ?? false)
                        {
                            Console.WriteLine("blacklist " + m.Message);
                            socket.SendMessage(new ChatPart($"{CHAT_PREFIX} Blocked a message from a player on your blacklist", null, $"You blacklisted {m.SenderName}"));
                            return true;
                        }
                        Console.WriteLine("got message " + m.Message);
                        var color = ((int)m.Tier) > 0 ? McColorCodes.DARK_GREEN : McColorCodes.WHITE;
                        return socket.SendMessage(
                            new ChatPart($"{CHAT_PREFIX} {color}{m.SenderName}{McColorCodes.WHITE}: {m.Message}", $"/cofl dialog chatreport {m.SenderName} {m.Message}", "click to report message"),
                            new ChatPart("", "/cofl void"));
                    } catch(Exception e)
                    {
                        dev.Logger.Instance.Error(e, "chat message");
                    }
                    return false;
                });
                socket.SessionInfo.ListeningToChat = true;

                socket.OnConClose += () =>
                {
                    sub.Unsubscribe();
                };
            }
        }
    }

    public class CCommand : ChatCommand
    {

    }


}