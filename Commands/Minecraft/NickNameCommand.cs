using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Coflnet.Sky.Commands.MC
{
    public class NickNameCommand : McCommand
    {
        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (!await socket.ReguirePremPlus())
            {
                return;
            }
            var nickName = JsonConvert.DeserializeObject<string>(arguments);
            if (string.IsNullOrEmpty(nickName))
            {
                socket.SendMessage(COFLNET + "Please provide a nickname");
                return;
            }
            if (nickName.Length > 16)
            {
                socket.SendMessage(COFLNET + "Nicknames can't be longer than 16 characters");
                return;
            }
            if (nickName.Contains(" "))
            {
                socket.SendMessage(COFLNET + "Nicknames can't contain spaces");
                return;
            }
            if (nickName == "clear")
            {
                socket.AccountInfo.NickName = null;
                await socket.sessionLifesycle.AccountInfo.Update();
                socket.SendMessage(COFLNET + "Cleared your nickname");
                return;
            }

            var redis = socket.GetService<IConnectionMultiplexer>();
            var playerId = socket.UserId;
            if ((await redis.GetDatabase().StringGetAsync("nickname" + playerId)).HasValue)
            {
                socket.Dialog(db => db.MsgLine("You changed your nickname recently already this can only be done every 2 days"));
                return;
            }
            await redis.GetDatabase().StringSetAsync("nickname" + playerId, "true", TimeSpan.FromDays(2));
            socket.AccountInfo.NickName = nickName;
            await socket.sessionLifesycle.AccountInfo.Update();
            socket.Dialog(db => db.MsgLine($"Set your account nickname to {McColorCodes.AQUA}{nickName}")
                .Msg("Note that if your nickname contains unapropiate words your account may be suspended", null,
                    "This will be displayed in chat instead of your minecraft name\n"
                    + "You can clear it by typing /cofl nickname clear"));
        }
    }
}