using System;
using System.Threading.Tasks;
using hypixel;
using MessagePack;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Coflnet.Sky.ModCommands.Services
{
    public class ChatService
    {
        public async Task Subscribe(Func<ModChatMessage, bool> OnMessage)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var sub = await GetCon().SubscribeAsync("mcChat");

                    sub.OnMessage((value) =>
                    {
                        var message = JsonConvert.DeserializeObject<ModChatMessage>(value.Message);
                        if (!OnMessage(message))
                            sub.Unsubscribe();
                    });
                    return;
                }
                catch (Exception)
                {
                    if(i >= 2)
                        throw;
                    await Task.Delay(300);
                }
            }
        }
        public async Task Send(ModChatMessage message)
        {
            for (int i = 0; i < 5; i++)
                try
                {
                    await GetCon().PublishAsync("mcChat", JsonConvert.SerializeObject(message));
                    return;
                }
                catch (RedisTimeoutException e)
                {

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
        }

        private static ISubscriber GetCon()
        {
            return CacheService.Instance.RedisConnection.GetSubscriber();
        }
    }
}
