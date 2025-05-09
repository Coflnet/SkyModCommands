using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Cassandra.Data.Linq;
using System.Collections;
using System.Collections.Generic;

namespace Coflnet.Sky.ModCommands.Controllers;

[ApiController]
[Route("[controller]")]
public class ConfigController : ControllerBase
{
    [HttpPost("{userId}/{configId}")]
    public async Task SetConfig(string userId, string configId, [FromBody] ConfigContainer config)
    {
        await SellConfigCommand.UpdateConfigRating(userId, configId, config.Price);
    }
    [HttpGet()]
    public async Task<IEnumerable<ConfigsCommand.ConfigRating>> GetConfigs()
    {
        return await MinecraftSocket.Commands.GetBy<ConfigsCommand>().GetTable().Where(c => c.Type == "config").ExecuteAsync();
    }
}
