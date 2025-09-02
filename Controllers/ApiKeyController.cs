using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Controllers;

/// <summary>
/// Controller for managing API keys and providing key information to other microservices
/// </summary>
[ApiController]
[Route("[controller]")]
public class ApiKeyController : ControllerBase
{
    private readonly ApiKeyService apiKeyService;
    private readonly ILogger<ApiKeyController> logger;

    public ApiKeyController(ApiKeyService apiKeyService, ILogger<ApiKeyController> logger)
    {
        this.apiKeyService = apiKeyService;
        this.logger = logger;
    }

    /// <summary>
    /// Validates an API key and returns associated user information
    /// </summary>
    /// <param name="key">The API key to validate</param>
    /// <returns>API key information if valid, null if invalid or inactive</returns>
    [HttpGet("validate/{key}")]
    public async Task<ActionResult<ApiKeyInfoResponse>> ValidateApiKey(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                return BadRequest("API key is required");
            }

            var apiKeyInfo = await apiKeyService.GetApiKeyInfo(key);
            
            if (apiKeyInfo == null)
            {
                logger.LogWarning($"API key validation failed - key not found: {key.Substring(0, Math.Min(8, key.Length))}...");
                return NotFound("API key not found");
            }

            if (!apiKeyInfo.IsActive)
            {
                logger.LogWarning($"API key validation failed - key inactive: {key.Substring(0, Math.Min(8, key.Length))}...");
                return Unauthorized("API key is inactive");
            }

            // Update usage statistics asynchronously
            _ = Task.Run(async () => await apiKeyService.UpdateKeyUsage(apiKeyInfo));

            var response = new ApiKeyInfoResponse
            {
                UserId = apiKeyInfo.UserId,
                MinecraftUuid = apiKeyInfo.MinecraftUuid,
                ProfileId = apiKeyInfo.ProfileId,
                MinecraftName = apiKeyInfo.MinecraftName,
                CreatedAt = apiKeyInfo.CreatedAt,
                LastUsed = apiKeyInfo.LastUsed,
                UsageCount = apiKeyInfo.UsageCount,
                IsActive = apiKeyInfo.IsActive
            };

            logger.LogInformation($"API key validated successfully for user {apiKeyInfo.UserId}");
            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error validating API key {key.Substring(0, Math.Min(8, key.Length))}...");
            return StatusCode(500, "Internal server error during API key validation");
        }
    }

    /// <summary>
    /// Gets API key information by key (without updating usage)
    /// </summary>
    /// <param name="key">The API key</param>
    /// <returns>API key information</returns>
    [HttpGet("info/{key}")]
    public async Task<ActionResult<ApiKeyInfoResponse>> GetApiKeyInfo(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                return BadRequest("API key is required");
            }

            var apiKeyInfo = await apiKeyService.GetApiKeyInfo(key);
            
            if (apiKeyInfo == null)
            {
                return NotFound("API key not found");
            }

            var response = new ApiKeyInfoResponse
            {
                UserId = apiKeyInfo.UserId,
                MinecraftUuid = apiKeyInfo.MinecraftUuid,
                ProfileId = apiKeyInfo.ProfileId,
                MinecraftName = apiKeyInfo.MinecraftName,
                CreatedAt = apiKeyInfo.CreatedAt,
                LastUsed = apiKeyInfo.LastUsed,
                UsageCount = apiKeyInfo.UsageCount,
                IsActive = apiKeyInfo.IsActive
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error retrieving API key info {key.Substring(0, Math.Min(8, key.Length))}...");
            return StatusCode(500, "Internal server error during API key retrieval");
        }
    }

    /// <summary>
    /// Deactivates an API key
    /// </summary>
    /// <param name="key">The API key to deactivate</param>
    /// <returns>Success message</returns>
    [HttpPost("deactivate/{key}")]
    public async Task<ActionResult> DeactivateApiKey(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                return BadRequest("API key is required");
            }

            await apiKeyService.DeactivateApiKey(key);
            
            logger.LogInformation($"API key deactivated: {key.Substring(0, Math.Min(8, key.Length))}...");
            return Ok(new { message = "API key deactivated successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error deactivating API key {key.Substring(0, Math.Min(8, key.Length))}...");
            return StatusCode(500, "Internal server error during API key deactivation");
        }
    }

    /// <summary>
    /// Gets all API keys for a specific user
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <returns>List of API keys for the user</returns>
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<ApiKeyInfoResponse[]>> GetUserApiKeys(string userId)
    {
        try
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest("User ID is required");
            }

            var apiKeys = await apiKeyService.GetUserApiKeys(userId);
            
            var response = apiKeys.Select(k => new ApiKeyInfoResponse
            {
                // Don't include the actual key in the response for security
                KeyPreview = k.Key.Substring(0, Math.Min(12, k.Key.Length)) + "...",
                UserId = k.UserId,
                MinecraftUuid = k.MinecraftUuid,
                ProfileId = k.ProfileId,
                MinecraftName = k.MinecraftName,
                CreatedAt = k.CreatedAt,
                LastUsed = k.LastUsed,
                UsageCount = k.UsageCount,
                IsActive = k.IsActive
            }).ToArray();

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error retrieving API keys for user {userId}");
            return StatusCode(500, "Internal server error during user API keys retrieval");
        }
    }
}

/// <summary>
/// Response model for API key information
/// </summary>
public class ApiKeyInfoResponse
{
    public string? KeyPreview { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string MinecraftUuid { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string MinecraftName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsed { get; set; }
    public long UsageCount { get; set; }
    public bool IsActive { get; set; }
}
