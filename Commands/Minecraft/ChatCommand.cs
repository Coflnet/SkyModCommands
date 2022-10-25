using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Chat.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class ChatCommand : McCommand
    {
        static ChatService chat;
        public static string CHAT_PREFIX = "[§1C§6hat§f]";
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

            bool shouldToggle = string.IsNullOrEmpty(message) || message == "toggle";
            if (shouldToggle || (!socket.sessionLifesycle.FlipSettings?.Value?.ModSettings?.Chat ?? false))
            {
                var settings = socket.sessionLifesycle.FlipSettings;
                if (settings == null)
                    throw new CoflnetException("no_settings", "could not toggle the cofl chat likely because you are not logged in");
                settings.Value.ModSettings.Chat = !settings.Value.ModSettings.Chat;
                await settings.Update(settings.Value);
                socket.SendMessage(CHAT_PREFIX + $"Toggled the chat {(settings.Value.ModSettings.Chat ? "on" : "off")}");

                if (shouldToggle)
                    return;
            }

            if (socket.sessionLifesycle.UserId.Value == null)
            {
                socket.SendMessage(COFLNET + "Sorry, you have to be logged in to send messages, click [HERE] to do that", socket.sessionLifesycle.GetAuthLink(socket.SessionInfo.SessionId), "Some idiot abused the chat system so sadly this is necessary now");
                return;
            }
            if (!socket.SessionInfo.VerifiedMc)
            {
                if (!await socket.sessionLifesycle.VerificationHandler.CheckVerificationStatus(socket.sessionLifesycle.AccountInfo))
                {
                    socket.SendMessage(COFLNET + "Sorry, you need to verify your minecraft account to write in chat", null, "Some dude abused the chat system so sadly this is necessary now.\nSee above for instructions");
                    return;
                }
            }

            await MakeSureChatIsConnected(socket);
            if (DateTime.UtcNow - TimeSpan.FromSeconds(1) < socket.SessionInfo.LastMessage)
            {
                socket.SendMessage(COFLNET + "You are writing to fast please slow down");
                return;
            }
            if (DateTime.UtcNow < socket.SessionInfo.MutedUntil)
            {
                socket.SendMessage(COFLNET + $"You are muted for {(int)(socket.SessionInfo.MutedUntil - DateTime.UtcNow).TotalMinutes + 1} minutes");
                return;
            }
            if (message.Length > maxMsgLength)
            {
                socket.SendMessage(COFLNET + "Please use another chat for long messages", null, $"Messages over {maxMsgLength} characters are blocked");
                return;
            }
            if (string.IsNullOrEmpty(socket.SessionInfo.McName))
                throw new CoflnetException("no_username", "Sorry we couldn't load your chat profile. Please try again in a few seconds.");
            var tier = await socket.UserAccountTier();
            await chat.Send(new ChatService.ModChatMessage()
            {
                Message = message,
                SenderName = socket.SessionInfo.McName,
                Tier = tier,
                SenderUuid = socket.SessionInfo.McUuid
            }, socket.tracer.ActiveSpan);
            socket.SessionInfo.LastMessage = DateTime.UtcNow;
        }

        public static async Task MakeSureChatIsConnected(MinecraftSocket socket)
        {
            if (!socket.SessionInfo.ListeningToChat)
            {
                if (chat == null)
                {
                    chat = socket.GetService<ChatService>();
                }
                var sub = await chat.Subscribe(OnMessage(socket));
                socket.SessionInfo.ListeningToChat = true;

                socket.OnConClose += () =>
                {
                    sub.Unsubscribe();
                };
            }
        }

        private static Func<ChatMessage, bool> OnMessage(MinecraftSocket socket)
        {
            return m =>
            {
                if (!(socket.sessionLifesycle.FlipSettings?.Value?.ModSettings?.Chat ?? false))
                {
                    socket.SessionInfo.ListeningToChat = false;
                    return false;
                }
                try
                {
                    if (socket.sessionLifesycle.AccountSettings?.Value?.MutedUsers?.Where(mu => mu.Uuid == m.Uuid).Any() ?? false)
                    {
                        if (socket.SessionInfo.SentMutedNoteFor.Contains(m.Uuid))
                            return true;
                        socket.SendMessage(new ChatPart($"{CHAT_PREFIX} Blocked a message from a player you muted", null,
                            $"You muted {m.Name}. (undo with /cofl unmute {m.Name}) \nThis message is displayed once per session and player\nto avoid confusion why messages are not shown to you"));
                        socket.SessionInfo.SentMutedNoteFor.Add(m.Uuid);
                        return true;
                    }
                    var color = m.Prefix;
                    return socket.SendMessage(
                        new ChatPart($"{CHAT_PREFIX} {color}{m.Name}{McColorCodes.WHITE}: {m.Message}", $"/cofl dialog chatoptions {m.Name} {m.ClientName} {m.Message} {m.Uuid}", $"click for more options"),
                        new ChatPart("", "/cofl void"));
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, "chat message");
                }
                return false;
            };
        }
    }

    public class CCommand : ChatCommand
    {

    }


}