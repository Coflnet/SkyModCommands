using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Chat.Client.Model;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;
using Coflnet.Sky.Api.Models.Mod;

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
            if (message.StartsWith("/cofl"))
            {
                socket.SendMessage(COFLNET + "Seems like you accientially sent a command in chat. If that was intentional, add a . in front to send it as message");
                return;
            }
            if (message == "disable")
            {
                socket.SendMessage(COFLNET + $"To turn the chat off just do {McColorCodes.AQUA}/cofl chat");
                return;
            }
            if (string.IsNullOrEmpty(socket.SessionInfo.McName))
                throw new CoflnetException("no_username", "Sorry we couldn't load your chat profile. Please try again in a few seconds.");
            var tier = socket.SessionInfo.SessionTier;
            await chat.Send(socket, new ChatService.ModChatMessage()
            {
                Message = message,
                SenderName = tier >= AccountTier.PREMIUM_PLUS ? socket.AccountInfo?.NickName ?? socket.SessionInfo.McName : socket.SessionInfo.McName,
                Tier = tier,
                SenderUuid = socket.SessionInfo.McUuid
            });
            socket.SessionInfo.LastMessage = DateTime.UtcNow;
            await socket.TriggerTutorial<ModCommands.Tutorials.ChatRulesTutorial>();
            if (Regex.IsMatch(message, "(people|how|ppl).*(claiming|buy|snipe).*(fast|quick)"))
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
            var commands = await socket.GetService<CommandSyncService>().Subscribe(socket.SessionInfo, h =>
            {
                if (socket.IsClosed)
                    return false;
                socket.ExecuteCommand(h);
                return true;
            });
            if (socket.SessionInfo.ListeningToChat)
            {
                return;
            }
            if (chat == null)
            {
                chat = socket.GetService<ChatService>();
            }
            if (chat.MutedUuids?.Contains(socket.SessionInfo.McUuid) ?? false)
            {
                Activity.Current.Log("muted user");
                return; // muted user
            }
            var sub = await chat.Subscribe(OnMessage(socket));
            var dm = await chat.SubscribeToChannel("dm-" + socket.SessionInfo.McName.ToLower(), OnMessage(socket));
            socket.SessionInfo.ListeningToChat = true;

            socket.OnConClose += () =>
            {
                sub.Unsubscribe();
                dm.Unsubscribe();
                commands.Unsubscribe();
            };
        }

        /// <summary>
        /// Called when a message is received
        /// </summary>
        /// <param name="socket"></param>
        /// <returns>true if the connection should be kept open</returns>
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

                    var mentionsSelf = message.Contains(socket.SessionInfo.McName);
                    if (mentionsSelf)
                    {
                        socket.SendSound("random.orb");
                        // highlight name in yellow 
                        message = message.Replace(socket.SessionInfo.McName, $"{McColorCodes.YELLOW}{socket.SessionInfo.McName}{McColorCodes.WHITE}");
                    }
                    var optionsCmd = $"/cofl dialog chatoptions {m.Name} {m.ClientName} {m.Message} {m.Uuid}";
                    if (message.Contains("物"))
                    {
                        return Shareitem(socket, m, color, message, optionsCmd);
                    }
                    if (message.Contains('傳'))
                    {
                        return ShareLore(socket, m, color, message, optionsCmd);
                    }
                    var chatparts = new List<ChatPart>
                    {
                        new ($"{CHAT_PREFIX} {color}{m.Name}{McColorCodes.WHITE}: ", optionsCmd, $"click for more options"),
                        new ($"{McColorCodes.WHITE}{message}", optionsCmd, $"click for more options"),
                        new ("", "/cofl void")
                    };
                    var isLinkInMessage = message.Contains("http");
                    if (isLinkInMessage)
                    {
                        message = InsertChatLink(message, chatparts);
                    }
                    return socket.SendMessage(chatparts.ToArray());
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, "chat message");
                }
                return false;
            };
        }

        private static bool ShareLore(MinecraftSocket socket, ChatMessage m, string color, string message, string optionsCmd)
        {
            Console.WriteLine("Sharing lore");
            var parts = message.Split('傳');
            var loreJson = parts[1];
            var lore = JsonConvert.DeserializeObject<DescriptionSetting>(loreJson);
            socket.Dialog(db => db.CoflCommand<LoreCommand>($"{m.Name} shared his lore settings with you, {McColorCodes.YELLOW}[click to use them]", loreJson, "Import the lore settings"));
            return true;
        }

        private static bool Shareitem(MinecraftSocket socket, ChatMessage m, string color, string message, string optionsCmd)
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

        private static string InsertChatLink(string message, List<ChatPart> chatparts)
        {
            var parts = message.Split(' ');
            var link = parts.Where(p => p.Contains("http")).FirstOrDefault();
            var linkIndex = Array.IndexOf(parts, link);
            var linkText = link;
            if (link.Length > 30)
                linkText = link.Substring(0, 30) + "...";
            parts[linkIndex] = linkText;
            message = string.Join(" ", parts);
            chatparts[1] = new ChatPart(message, link, $"click to open {link}");
            return message;
        }
    }
}