using System;
using System.Threading.Tasks;
using RestSharp;

namespace Coflnet.Sky.Commands.MC
{
    public class NextUpdateRetriever
    {
        static RestClient client = new RestClient("http://" + SimplerConfig.SConfig.Instance["UPDATER_HOST"]);
        public async Task<DateTime> Get()
        {
            try
            {
                DateTime last = default;
                while (last < new DateTime(2020, 1, 1))
                {
                    last = (await client.ExecuteAsync<DateTime>(new RestRequest("/api/time"))).Data;
                }
                return last + TimeSpan.FromSeconds(61);
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "getting next update time");
                throw e;
            }
        }
    }
}