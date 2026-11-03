using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services.RealizedProfit;
using Microsoft.AspNetCore.Mvc;

namespace Coflnet.Sky.ModCommands.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RealizedProfitController : ControllerBase
{
    private readonly RealizedProfitTierService tiers;

    public RealizedProfitController(RealizedProfitTierService tiers)
    {
        this.tiers = tiers;
    }

    /// <summary>
    /// Realized profit of the last day per tier: auction flips, bazaar flips and realized task value
    /// of users that disconnected recently and kept their tier for at least another day.
    /// Cached for a minute.
    /// </summary>
    [HttpGet("tiers")]
    public Task<List<TierRealizedProfit>> GetTiers() => tiers.GetTiers();

    /// <summary>
    /// The per tier numbers plus the ALL (every tier) and PAID (STARTER_PREMIUM and above) aggregates,
    /// including medians over the combined users. Same one minute cache as the tiers endpoint.
    /// </summary>
    [HttpGet("summary")]
    public Task<RealizedProfitSummary> GetSummary() => tiers.GetSummary();
}
