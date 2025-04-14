using System;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using MessagePack;
using Newtonsoft.Json;
using StackExchange.Redis;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Chat.Client.Client;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Chat.Client.Model;
using Coflnet.Sky.Commands.Shared;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.ModCommands.Services;
public class ChatService
{
    private Chat.Client.Api.ChatApi api;
    private string chatAuthKey;
    private List<string> mutedUuids;
    public List<string> MutedUuids => mutedUuids;
    private SettingsService settingsService;
    private ModeratorService moderatorService;
    public ChatService(IConfiguration config, SettingsService settingsService, ModeratorService moderatorService)
    {
        api = new(new Configuration(){
            BasePath = config["CHAT_BASE_URL"],
            ApiKey = new Dictionary<string, string>(){
                {"Authorization", config["CHAT_API_KEY"]}
            }
        });
        chatAuthKey = config["CHAT_API_KEY"];
        RefreshMutedUsers();
        this.settingsService = settingsService;
        this.moderatorService = moderatorService;
    }
    public async Task<ChannelMessageQueue> Subscribe(Func<ChatMessage, bool> onMessage)
    {
        return await SubscribeToChannel("chat", onMessage);
    }

    public async Task<ChannelMessageQueue> SubscribeToChannel(string channel, Func<ChatMessage, bool> onMessage)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                var sub = await GetCon().SubscribeAsync(channel);

                sub.OnMessage((value) =>
                {
                    var message = JsonConvert.DeserializeObject<ChatMessage>(value.Message);
                    if (!onMessage(message))
                        sub.Unsubscribe();
                });
                return sub;
            }
            catch (Exception)
            {
                if (i >= 2)
                    throw;
                await Task.Delay(300).ConfigureAwait(false);
            }
        }
        throw new CoflnetException("connection_failed", "connection to chat failed");
    }

    public async Task SendToChannel(string channel, ChatMessage message)
    {
        await GetCon().PublishAsync(channel, JsonConvert.SerializeObject(message));
    }

    public async Task<List<string>> GetMuteUuids()
    {
        return (await api.ApiChatMutesGetAsync()).Select(m => m.Uuid).ToList();
    }

    private void RefreshMutedUsers()
    {
        Task.Run(async () =>
        {
            try
            {
                mutedUuids = await GetMuteUuids();
                Console.WriteLine($" {mutedUuids.Count} muted users refreshed");
            }
            catch (Exception e)
            {
                Activity.Current?.Log(e.Message);
            }
        });
    }

    public async Task Send(MinecraftSocket socket, ModChatMessage message)
    {
        try
        {
            var isMod = moderatorService.IsModerator(socket);
            var prefix = message.Tier switch
            {
                AccountTier.SUPER_PREMIUM => McColorCodes.RED,
                AccountTier.PREMIUM_PLUS => McColorCodes.GOLD,
                AccountTier.PREMIUM => McColorCodes.DARK_GREEN,
                AccountTier.STARTER_PREMIUM => McColorCodes.WHITE,
                _ => McColorCodes.GRAY
            };
            if (isMod)
            {
                if (message.Tier < AccountTier.PREMIUM)
                    prefix = McColorCodes.DARK_GREEN;
                prefix = McColorCodes.GOLD + "ⓂⓄⒹ" + prefix;
            }
            var chatMsg = new ChatMessage(
                message.SenderUuid ?? throw new CoflnetException("invalid_sender", "Sender uuid is null"),
                message.SenderName,
                prefix,
                message.Message);
            Activity.Current?.Log("sending to service");
            await api.ApiChatSendPostAsync(chatMsg);
        }
        catch (ApiException e)
        {
            RefreshMutedUsers();
            throw JsonConvert.DeserializeObject<CoflnetException>(e.Message?.Replace("Error calling ApiChatSendPost: ", ""));
        }
    }

    public async Task Mute(Mute mute)
    {
        await api.ApiChatMutePostAsync(mute);
        RefreshMutedUsers();
    }

    [MessagePackObject]
    public class ModChatMessage
    {
        [Key(0)]
        public string SenderName;
        [Key(1)]
        public string Message;
        [Key(2)]
        public AccountTier Tier;
        [Key(3)]
        public string SenderUuid;
    }

    private ISubscriber GetCon()
    {
        return settingsService.Con?.GetSubscriber() ?? throw new Exception("No chat connection available");
    }
}

