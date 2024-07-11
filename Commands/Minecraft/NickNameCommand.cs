using System;
using System.Threading.Tasks;
using Coflnet.Sky.McConnect.Api;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Coflnet.Sky.Commands.MC
{
    public class NickNameCommand : McCommand
    {
        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            
            var nickName = JsonConvert.DeserializeObject<string>(arguments);
            if (nickName == "clear")
            {
                socket.AccountInfo.NickName = null;
                await socket.sessionLifesycle.AccountInfo.Update();
                socket.SendMessage(COFLNET + "Cleared your nickname");
                return;
            }
            if (!await socket.ReguirePremPlus())
            {
                return;
            }
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
            var alphaNumericRegex = new System.Text.RegularExpressions.Regex("^[a-zA-Z0-9_]*$");
            if(!alphaNumericRegex.IsMatch(nickName))
            {
                socket.SendMessage(COFLNET + "Nicknames can only contain letters, numbers and underscores");
                return;
            }

            var redis = socket.GetService<IConnectionMultiplexer>();
            var playerId = socket.UserId;
            if ((await redis.GetDatabase().StringGetAsync("nickname" + playerId)).HasValue)
            {
                socket.Dialog(db => db.MsgLine("You changed your nickname recently already this can only be done every 2 days"));
                return;
            }
            var existingUuid = await socket.GetPlayerUuid(nickName, true);
            if (!string.IsNullOrEmpty(existingUuid))
            {
                var connectApi = socket.GetService<IConnectApi>();
                var userInfo = await connectApi.ConnectMinecraftMcUuidGetAsync(existingUuid);
                if (userInfo?.ExternalId != socket.UserId)
                {
                    socket.Dialog(db => db.MsgLine($"You can't nick as {nickName} as it is already used by another user"));
                    return;
                }
            }
            if((await redis.GetDatabase().StringGetAsync("nicknameu" + nickName)).HasValue)
            {
                socket.Dialog(db => db.MsgLine($"The nickname {nickName} is already in use"));
                return;
            }

            socket.AccountInfo.NickName = nickName;
            await socket.sessionLifesycle.AccountInfo.Update();
            socket.Dialog(db => db.MsgLine($"Set your account nickname to {McColorCodes.AQUA}{nickName}")
                .Msg("Note that if your nickname contains unapropiate words your account may be suspended", null,
                    "This will be displayed in chat instead of your minecraft name\n"
                    + "You can clear it by typing /cofl nickname clear"));
            await redis.GetDatabase().StringSetAsync("nickname" + playerId, "true", TimeSpan.FromDays(2));
            await redis.GetDatabase().StringSetAsync("nicknameu" + nickName, "true", TimeSpan.FromDays(2));
        }
    }
}