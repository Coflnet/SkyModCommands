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
        public void Subscribe(Func<ModChatMessage, bool> OnMessage)
        {
            var sub = GetCon().Subscribe("mcChat");
            
            sub.OnMessage((value) =>
            {
                var message = JsonConvert.DeserializeObject<ModChatMessage>(value.Message);
                if(!OnMessage(message))
                    sub.Unsubscribe();
            });
        }
        public async Task Send(ModChatMessage message)
        {
            await GetCon().PublishAsync("mcChat", JsonConvert.SerializeObject(message));
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
