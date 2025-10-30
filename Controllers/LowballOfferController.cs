using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Controllers;

[ApiController]
[Route("api/lowball")]
public class LowballOfferController : ControllerBase
{
    private readonly LowballOfferService lowballService;
    private readonly ILogger<LowballOfferController> logger;

    public LowballOfferController(LowballOfferService lowballService, ILogger<LowballOfferController> logger)
    {
        this.lowballService = lowballService;
        this.logger = logger;
    }

    /// <summary>
    /// Get lowball offers by user, paginated
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="before">Optional: Get offers before this timestamp (ISO 8601 format)</param>
    /// <param name="limit">Number of results to return (default 20, max 100)</param>
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<List<LowballOffer>>> GetUserOffers(
        string userId,
        [FromQuery] DateTimeOffset? before = null,
        [FromQuery] int limit = 20)
    {
        try
        {
            if (limit > 100)
                limit = 100;

            var offers = await lowballService.GetOffersByUser(userId, before, limit);
            return Ok(offers);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error getting lowball offers for user {userId}");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Get lowball offers by item tag with optional filters
    /// </summary>
    /// <param name="itemTag">The item tag</param>
    /// <param name="before">Optional: Get offers before this timestamp (ISO 8601 format)</param>
    /// <param name="limit">Number of results to return (default 20, max 100)</param>
    /// <param name="filter">Optional filter parameters in format key=value (can be multiple)</param>
    [HttpGet("item/{itemTag}")]
    public async Task<ActionResult<List<LowballOfferByItem>>> GetItemOffers(
        string itemTag,
        [FromQuery] DateTimeOffset? before = null,
        [FromQuery] int limit = 20,
        [FromQuery] Dictionary<string, string> filter = null)
    {
        try
        {
            if (limit > 100)
                limit = 100;

            var offers = await lowballService.GetOffersByItem(itemTag, filter, before, limit);
            return Ok(offers);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error getting lowball offers for item {itemTag}");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Delete a lowball offer
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="offerId">The offer ID</param>
    [HttpDelete("user/{userId}/offer/{offerId}")]
    public async Task<ActionResult> DeleteOffer(string userId, Guid offerId)
    {
        try
        {
            var success = await lowballService.DeleteOffer(userId, offerId);
            if (success)
                return Ok();
            else
                return NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error deleting lowball offer {offerId} for user {userId}");
            return StatusCode(500, "Internal server error");
        }
    }
}
