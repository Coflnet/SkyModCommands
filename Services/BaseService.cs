using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.ModCommands.Services;
public class CounterService
{
    private ISession session;
    private IConfiguration config;

    public CounterService(ISession session, IConfiguration config)
    {
        this.session = session;
        this.config = config;
    }

    public Table<CountTable> GetTable()
    {
        return new Table<CountTable>(session);
    }

    public async Task<long> GetCount(string id, string name)
    {
        return await GetTable().Where(x => x.Id == id && x.Name == name)
            .Select(x => x.Value)
            .FirstOrDefault().ExecuteAsync();
    }

    public async Task Increment(string id, string name, long amount = 1)
    {
        await GetTable().Where(x => x.Id == id && x.Name == name)
            .Select(x => new CountTable() { Value = amount })
            .Update().ExecuteAsync();
    }
}
