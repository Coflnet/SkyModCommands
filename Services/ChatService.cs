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

namespace Coflnet.Sky.ModCommands.Services;

public class ChatService
{
    private Chat.Client.Api.ChatApi api;
    private string chatAuthKey;

    public ChatService(IConfiguration config)
    {
        api = new(config["CHAT_BASE_URL"]);
        chatAuthKey = config["CHAT_API_KEY"];
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

    public async Task Send(ModChatMessage message)
    {
        try
        {
            var chatMsg = new ChatMessage(
                message.SenderUuid ?? throw new CoflnetException("invalid_sender", "Sender uuid is null"), 
                message.SenderName,
                message.Tier switch
                {
                    AccountTier.SUPER_PREMIUM => McColorCodes.RED,
                    AccountTier.PREMIUM_PLUS => McColorCodes.GOLD,
                    AccountTier.PREMIUM => McColorCodes.DARK_GREEN,
                    AccountTier.STARTER_PREMIUM => McColorCodes.WHITE,
                    _ => McColorCodes.GRAY
                },
                message.Message);
            Activity.Current?.Log("sending to service");
            await api.ApiChatSendPostAsync(chatAuthKey, chatMsg);
        }
        catch (ApiException e)
        {
            throw JsonConvert.DeserializeObject<CoflnetException>(e.Message?.Replace("Error calling ApiChatSendPost: ", ""));
        }
    }

    public async Task Mute(Mute mute)
    {
        await api.ApiChatMutePostAsync(chatAuthKey, mute);
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

    private static ISubscriber GetCon()
    {
        return CacheService.Instance.RedisConnection.GetSubscriber();
    }
}

