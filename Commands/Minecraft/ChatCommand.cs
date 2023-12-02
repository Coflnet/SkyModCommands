using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Chat.Client.Model;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{

    [CommandDescription("Writes a message to the chat",
        "Alias /fc <msg>",
        "Writes a message to the cofl chat",
        "Be nice and don't advertise or you may get muted")]
    public class ChatCommand : McCommand
    {
        private static ChatService chat;
        public static string CHAT_PREFIX = "[§1C§6hat§f]";
        public override bool IsPublic => true;

        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var maxMsgLength = 150;
            var message = JsonConvert.DeserializeObject<string>(arguments);

            bool shouldToggle = string.IsNullOrEmpty(message) || message == "toggle";
            if (shouldToggle || (!socket.sessionLifesycle.FlipSettings?.Value?.ModSettings?.Chat ?? false))
            {
                await Togglechat(socket);
                if (shouldToggle)
                    return;
            }

            if (socket.sessionLifesycle.UserId.Value == null)
            {
                if (socket.SessionInfo.IsMacroBot)
                {
                    socket.SendMessage(COFLNET + "Sorry, you have to be logged in to send messages, open this link to login:\n" + socket.sessionLifesycle.GetAuthLink(socket.SessionInfo.SessionId), socket.sessionLifesycle.GetAuthLink(socket.SessionInfo.SessionId));
                    return;
                }
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
                socket.SendMessage(COFLNET + "You are writing too fast please slow down");
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
            });
            socket.SessionInfo.LastMessage = DateTime.UtcNow;
            await socket.TriggerTutorial<ModCommands.Tutorials.ChatRulesTutorial>();
            if(Regex.IsMatch(message, "(people|how|ppl).*(claiming|buy|snipe).*(fast|quick)"))
                await socket.TriggerTutorial<ModCommands.Tutorials.QuickBuyTutorial>();

        }

        private static async Task Togglechat(MinecraftSocket socket)
        {
            var settings = socket.sessionLifesycle.FlipSettings;
            if (settings == null)
                throw new CoflnetException("no_settings", "could not toggle the cofl chat likely because you are not logged in");
            settings.Value.LastChanged = "chat";
            settings.Value.ModSettings.Chat = !settings.Value.ModSettings.Chat;
            await settings.Update(settings.Value);
            socket.SendMessage(CHAT_PREFIX + $"Toggled the chat {(settings.Value.ModSettings.Chat ? "on" : "off")}");
            if (!settings.Value.ModSettings.Chat)
                await socket.TriggerTutorial<ModCommands.Tutorials.ChatToggleTutorial>();
        }

        public static async Task MakeSureChatIsConnected(MinecraftSocket socket)
        {
            if (socket.SessionInfo.ListeningToChat)
            {
                return;
            }
            if (chat == null)
            {
                chat = socket.GetService<ChatService>();
            }
            if (chat.MutedUuids?.Contains(socket.SessionInfo.McUuid) ?? false)
                return; // muted user
            var sub = await chat.Subscribe(OnMessage(socket));
            var dm = await chat.SubscribeToChannel("dm-" + socket.SessionInfo.McName.ToLower(), OnMessage(socket));
            socket.SessionInfo.ListeningToChat = true;

            socket.OnConClose += () =>
            {
                sub.Unsubscribe();
                dm.Unsubscribe();
            };
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
                    if (chat.MutedUuids?.Contains(socket.SessionInfo.McUuid) ?? false)
                    {
                        socket.SendMessage(new ChatPart($"Blocked message because you are muted, follow the rules in the future!"));
                        return true;
                    }
                    var color = m.Prefix;
                    socket.TryAsyncTimes(() => socket.TriggerTutorial<ModCommands.Tutorials.ChatTutorial>(), "chat tutorial");
                    var message = m.Message;
                    var optionsCmd = $"/cofl dialog chatoptions {m.Name} {m.ClientName} {m.Message} {m.Uuid}";
                    if (message.Contains("物"))
                    {
                        var parts = message.Split('物');
                        var itemJson = parts[1];
                        var item = JsonConvert.DeserializeObject<Coflnet.Sky.PlayerState.Client.Model.Item>(itemJson);
                        var displayDescription = item.Description;
                        if (item.Color != null)
                            displayDescription += $"\nHex Color: {item.Color.Value.ToString("X").PadLeft(6, '0')}";
                        return socket.SendMessage(new ChatPart($"{CHAT_PREFIX} {color}{m.Name}{McColorCodes.WHITE}: {parts[0]}", optionsCmd, $"click for more options"),
                        new ChatPart(item.ItemName, "", displayDescription),
                            new ChatPart("", "/cofl void"));
                    }
                    return socket.SendMessage(
                        new ChatPart($"{CHAT_PREFIX} {color}{m.Name}{McColorCodes.WHITE}: {m.Message}", optionsCmd, $"click for more options"),
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
}