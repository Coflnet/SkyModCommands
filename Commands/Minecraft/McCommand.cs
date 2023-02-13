using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public abstract class McCommand
    {
        public string COFLNET => MinecraftSocket.COFLNET;
        public static string DEFAULT_COLOR => McColorCodes.GRAY;
        public abstract Task Execute(MinecraftSocket socket, string arguments);

        public string Slug => this.GetType().Name.Replace("Command", "").ToLower();

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
    }
}