using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Controllers;

/// <summary>
/// Internal endpoint exposing the item lore settings a signed-out mod instance kept
/// </summary>
[ApiController]
[Route("api/instance")]
public class InstanceSettingsController(IInstanceSettingsService instanceSettings, ILogger<InstanceSettingsController> logger) : ControllerBase
{
    /// <summary>
    /// The description settings of the mod instance, 204 when it kept none.
    /// The id is a query parameter because it is base64 and may contain / + and =.
    /// 503 when the read failed so callers can tell it apart from "kept none"
    /// </summary>
    [HttpGet("description")]
    public async Task<IActionResult> GetDescription([FromQuery] string sessionId)
    {
        try
        {
            var description = await instanceSettings.GetDescriptionStrict(sessionId);
            if (description == null)
                return NoContent();
            return Content(JsonConvert.SerializeObject(description), "application/json");
        }
        catch (System.Exception e)
        {
            logger.LogError(e, "Error loading instance description");
            return StatusCode(503);
        }
    }
}
