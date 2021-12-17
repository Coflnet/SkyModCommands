using Newtonsoft.Json;

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

        public static Response Create<T>(string type, T data)
        {
            return new Response(type, JsonConvert.SerializeObject(data));
        }

    }
}