using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Coflnet.Sky.Commands.MC
{
    public class Response
    {
        public string type;
        public string data;

        public Response()
        {
        }

        public Response(string type, string data)
        {
            this.type = type;
            this.data = data;
        }

        private static JsonSerializerSettings Settings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = new JsonConverter[] { new Newtonsoft.Json.Converters.StringEnumConverter() }
        };

        public static Response Create<T>(string type, T data)
        {
            return new Response(type, JsonConvert.SerializeObject(data, Formatting.None, Settings));
        }

    }
}