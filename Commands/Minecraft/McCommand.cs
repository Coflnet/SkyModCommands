using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public abstract class McCommand
    {
        public string COFLNET => MinecraftSocket.COFLNET;
        public static string DEFAULT_COLOR => McColorCodes.GRAY;
        public abstract Task Execute(MinecraftSocket socket, string arguments);

        public string Slug => GetType().Name.Replace("Command", "").ToLower();

        protected T Convert<T>(string arguments)
        {
            if (typeof(T) == typeof(string))
                return JsonConvert.DeserializeObject<T>(arguments);
            return JsonConvert.DeserializeObject<T>(JsonConvert.DeserializeObject<string>(arguments));
        }
        /// <summary>
        /// Should this command be shown in the help menu
        /// </summary>
        public virtual bool IsPublic => false;

        protected static async Task<string> GetUserIdFromMcName(IMinecraftSocket socket, string minecraftName)
        {
            if (int.TryParse(minecraftName, out var id))
            {
                return id.ToString();
            }
            var accountUuid = await socket.GetPlayerUuid(minecraftName, false);
            if (accountUuid == null)
                throw new CoflnetException("not_found", $"Could not find a minecraft account for `{minecraftName}`.");
            var userInfo = await socket.GetService<McAccountService>().GetUserId(accountUuid);
            if (userInfo == null)
                throw new CoflnetException("not_found", $"{minecraftName} does not appear to have verified their account yet.");
            var targetUser = userInfo.ExternalId;
            return targetUser;
        }
    }
}