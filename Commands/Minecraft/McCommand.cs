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
            try
            {
                return JsonConvert.DeserializeObject<T>(JsonConvert.DeserializeObject<string>(arguments));
            }
            catch (System.Exception)
            {
                // try again with the raw string
                try
                {
                    return JsonConvert.DeserializeObject<T>(arguments.Trim('"'));
                }
                catch (System.Exception)
                {
                    throw new CoflnetException("invalid_arguments", $"Could not parse the arguments for {Slug} please check your input/ create a report.");
                }
            }
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
            if (string.IsNullOrEmpty(accountUuid))
                throw new CoflnetException("not_found", $"Could not find a minecraft account for `{minecraftName}`.");
            var userInfo = await socket.GetService<McAccountService>().GetUserId(accountUuid);
            if (userInfo == null)
                throw new CoflnetException("not_found", $"{minecraftName} does not appear to have verified their account yet.");
            var targetUser = userInfo.ExternalId;
            return targetUser;
        }
    }
}