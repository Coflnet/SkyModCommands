using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System;
using Coflnet.Sky.ModCommands.Services;
using System.Collections.Generic;
using static Coflnet.Sky.ModCommands.Services.BlockedService;
using Coflnet.Sky.ModCommands.Services.Vps;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace Coflnet.Sky.ModCommands.Controllers;
[ApiController]
[Route("[controller]")]
public class VpsController : ControllerBase
{
    private VpsInstanceManager vpsInstanceManager;
    private IBlockedService blockedService;
    private ILogger<VpsController> logger;
    private CommandSyncService commandSyncService;

    public VpsController(VpsInstanceManager vpsInstanceManager, IBlockedService blockedService, ILogger<VpsController> logger, CommandSyncService commandSyncService)
    {
        this.vpsInstanceManager = vpsInstanceManager;
        this.blockedService = blockedService;
        this.logger = logger;
        this.commandSyncService = commandSyncService;
    }

    [HttpGet]
    [Route("blocked")]
    public async Task<IEnumerable<BlockedReason>> GetBlockedReasons(string userId, string auctionUuid)
    {
        return await blockedService.GetBlockedReasons(userId, Guid.Parse(auctionUuid));
    }

    [HttpPost("execute")]
    public async Task ExecuteCommand([FromBody] CommandSyncService.ExecuteRequest request)
    {
        await commandSyncService.Publish(request);
    }

    [HttpGet("instances")]
    public async Task<IEnumerable<Instance>> GetInstances(string userId)
    {
        return await vpsInstanceManager.GetVpsForUser(userId);
    }

    [HttpGet("settings")]
    [ResponseCache(Duration = 3600)]
    public Dictionary<string, Commands.Shared.SettingsUpdater.SettingDoc> GetSettings()
    {
        return vpsInstanceManager.SettingOptions();
    }

    [HttpGet("{user}/{instanceId}/settings")]
    public async Task<Dictionary<string, string>> GetUserSettings(string user, Guid instanceId)
    {
        Instance instance = await GetInstance(user, instanceId);
        return await vpsInstanceManager.GetSettings(user, instance);
    }

    [HttpPost("{user}/{instanceId}/set")]
    public async Task UpdateSetting(string user, Guid instanceId, [FromBody] VpsSettingUpdateRequest request)
    {
        Instance instance = await GetInstance(user, instanceId);
        await vpsInstanceManager.UpdateSetting(user, request.Setting, request.Value, instance);
    }

    private async Task<Instance> GetInstance(string user, Guid instanceId)
    {
        var instances = await vpsInstanceManager.GetVpsForUser(user);
        var instance = instances.FirstOrDefault(i => i.Id == instanceId);
        if (instance == null)
            throw new Exception("VPS instance not found");
        return instance;
    }

    [HttpPost("{user}/{instanceId}/turnOn")]
    public async Task TurnOn(string user, Guid instanceId)
    {
        Instance instance = await GetInstance(user, instanceId);
        await vpsInstanceManager.TurnOnVps(instance);
    }
    [HttpPost("{user}/{instanceId}/turnOff")]
    public async Task TurnOff(string user, Guid instanceId)
    {
        Instance instance = await GetInstance(user, instanceId);
        await vpsInstanceManager.TurnOffVps(instance);
    }
    [HttpPost("{user}/{instanceId}/import")]
    public async Task Import(string user, Guid instanceId, [FromBody] string settings)
    {
        Instance instance = await GetInstance(user, instanceId);
        await vpsInstanceManager.ImportSettings(instance, settings);
    }

    public class VpsSettingUpdateRequest
    {
        public string Setting { get; set; }
        public string Value { get; set; }
    }
}