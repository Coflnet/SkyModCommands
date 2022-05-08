using System;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using MessagePack;
using Newtonsoft.Json;
using StackExchange.Redis;
using Coflnet.Sky.Chat.Client;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Chat.Client.Client;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Chat.Client.Model;
using Coflnet.Sky.Commands.Shared;
using OpenTracing;

namespace Coflnet.Sky.ModCommands.Services
{
    public class ChatService
    {
        Chat.Client.Api.ChatApi api;
        string chatAuthKey;

        public ChatService(IConfiguration config)
        {
            api = new(config["CHAT:BASE_URL"]);
            chatAuthKey = config["CHAT:API_KEY"];
        }
        public async Task<ChannelMessageQueue> Subscribe(Func<ChatMessage, bool> OnMessage)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var sub = await GetCon().SubscribeAsync("chat");

                    sub.OnMessage((value) =>
                    {
                        var message = JsonConvert.DeserializeObject<ChatMessage>(value.Message);
                        if (!OnMessage(message))
                            sub.Unsubscribe();
                    });
                    return sub;
                }
                catch (Exception)
                {
                    if (i >= 2)
                        throw;
                    await Task.Delay(300);
                }
            }
            throw new CoflnetException("connection_failed", "connection to chat failed");
        }
        public async Task Send(ModChatMessage message, ISpan span)
        {
            for (int i = 0; i < 5; i++)
                try
                {
                    var chatMsg = new Chat.Client.Model.ChatMessage(
                        message.SenderUuid, message.SenderName,
                        (int)message.Tier > 0 ? McColorCodes.DARK_GREEN : McColorCodes.WHITE,
                        message.Message);
                    span.Log("sending to service");
                    await api.ApiChatSendPostAsync(chatAuthKey, chatMsg);
                    span.Log("writing in redis");
                    await GetCon().PublishAsync("mcChat", JsonConvert.SerializeObject(message));
                    return;
                }
                catch (RedisTimeoutException)
                {

                }
                catch (ApiException e)
                {
                    throw JsonConvert.DeserializeObject<CoflnetException>(e.Message.Replace("Error calling ApiChatSendPost: ", ""));
                }
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
}
