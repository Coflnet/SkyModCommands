using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Controllers;

/// <summary>
/// Controller for managing proxy requests via mod users
/// </summary>
[ApiController]
[Route("api/proxy")]
public class ProxyController : ControllerBase
{
    private readonly ProxyService proxyService;
    private readonly ILogger<ProxyController> logger;

    public ProxyController(ProxyService proxyService, ILogger<ProxyController> logger)
    {
        this.proxyService = proxyService;
        this.logger = logger;
    }

    /// <summary>
    /// Request a proxy for a given URL
    /// </summary>
    /// <param name="request">The proxy request details</param>
    /// <returns>The request ID</returns>
    [HttpPost("request")]
    public async Task<IActionResult> RequestProxy([FromBody] ProxyRequestDto request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Url))
                return BadRequest(new { error = "URL is required" });

            var requestId = await proxyService.RequestProxy(
                request.Url,
                request.UploadTo ?? (MinecraftSocket.IsDevMode ? "http://localhost:5005/api/data/proxy" : "https://sky.coflnet.com/api/data/proxy"),
                request.Locale,
                request.Regex
            );

            return Ok(new { id = requestId, message = "Proxy request sent" });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No proxy users available");
            return StatusCode(503, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to request proxy");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Submit a proxy response from a mod user
    /// </summary>
    /// <param name="response">The proxy response data</param>
    /// <returns>Success status</returns>
    [HttpPost("response")]
    public async Task<IActionResult> SubmitResponse([FromBody] ProxyResponseDto response)
    {
        try
        {
            if (string.IsNullOrEmpty(response.Id))
                return BadRequest(new { error = "ID is required" });

            var success = await proxyService.StoreProxyResponse(
                response.Id,
                response.RequestUrl,
                response.ResponseBody,
                response.StatusCode,
                response.Headers,
                response.UserId,
                response.Locale
            );

            if (success)
                return Ok(new { message = "Response stored successfully" });
            else
                return StatusCode(500, new { error = "Failed to store response" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to submit proxy response");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get a proxy response by ID
    /// </summary>
    /// <param name="id">The request ID</param>
    /// <returns>The proxy response</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetResponse(string id)
    {
        try
        {
            var response = await proxyService.GetProxyResponse(id);

            if (response == null)
                return NotFound(new { error = "Response not found or expired" });

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to get proxy response {id}");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get statistics about available proxy users
    /// </summary>
    /// <returns>Socket statistics</returns>
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        try
        {
            var totalCount = proxyService.GetAvailableSocketCount();
            var byLocale = proxyService.GetSocketCountByLocale();

            return Ok(new
            {
                totalAvailable = totalCount,
                byLocale = byLocale
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get proxy stats");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

/// <summary>
/// Data transfer object for proxy requests
/// </summary>
public class ProxyRequestDto
{
    /// <summary>
    /// The URL to proxy
    /// </summary>
    public string Url { get; set; }

#nullable enable
    /// <summary>
    /// The URL to upload the response to
    /// </summary>
    public string? UploadTo { get; set; }

    /// <summary>
    /// Optional locale filter for selecting proxy users
    /// </summary>
    public string? Locale { get; set; }

    /// <summary>
    /// Optional regex pattern to match in the response
    /// </summary>
    public string? Regex { get; set; }
#nullable disable
}

/// <summary>
/// Data transfer object for proxy responses
/// </summary>
public class ProxyResponseDto
{
    /// <summary>
    /// The request ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The URL that was requested
    /// </summary>
    public string RequestUrl { get; set; }

    /// <summary>
    /// The response body content
    /// </summary>
    public string ResponseBody { get; set; }

    /// <summary>
    /// The HTTP status code
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// The response headers (JSON serialized)
    /// </summary>
    public string Headers { get; set; }

    /// <summary>
    /// The user ID who proxied the request
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// The locale of the user
    /// </summary>
    public string Locale { get; set; }
}
