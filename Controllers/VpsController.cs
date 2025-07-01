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

    [HttpPost]
    public async Task<Instance> Create(string userId, [FromBody] VpsCreateRequest request)
    {
        var instance = await vpsInstanceManager.CreateVps(userId, request.mcName, request.AppKind);
        return instance;
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

    [HttpPost("{user}/{instanceId}/reassign")]
    public async Task Reassign(string user, Guid instanceId)
    {
        Instance instance = await GetInstance(user, instanceId);
        await vpsInstanceManager.ReassignVps(instance);
    }

    [HttpPost("{user}/{instanceId}/reset")]
    public async Task Reset(string user, Guid instanceId, VpsCreateRequest request)
    {
        Instance instance = await GetInstance(user, instanceId);
        await vpsInstanceManager.ResetVps(instance, request.mcName, request.AppKind);
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
    [HttpPost("{user}/{instanceId}/extend")]
    public async Task<Instance> Extend(string user, Guid instanceId)
    {
        Instance instance = await GetInstance(user, instanceId);
        await vpsInstanceManager.ExtendVps(instance);
        return instance;
    }

    [HttpDelete("{user}/{instanceId}")]
    public async Task Delete(string user, Guid instanceId)
    {
        Instance instance = await GetInstance(user, instanceId);
        await vpsInstanceManager.DeleteVps(instance);
    }

    [HttpPost("{user}/{instanceId}/import")]
    public async Task Import(string user, Guid instanceId, [FromBody] string settings)
    {
        Instance instance = await GetInstance(user, instanceId);
        await vpsInstanceManager.ImportSettings(instance, settings);
    }

    [HttpGet("{user}/{instanceId}/export")]
    public async Task<string> Export(string user, Guid instanceId)
    {
        Instance instance = await GetInstance(user, instanceId);
        return await vpsInstanceManager.ExportSettings(instance);
    }

    [HttpPost("{user}/{instanceId}/publicIp")]
    public async Task<string> SetPublicIp(string user, Guid instanceId, [FromBody] string ip)
    {
        Instance instance = await GetInstance(user, instanceId);
        await vpsInstanceManager.SetPublicIp(instance, ip);
        return ip;
    }

    [HttpGet("ipGroups")]
    public async Task<Dictionary<string, IEnumerable<string>>> GetIpGroups()
    {
        return await vpsInstanceManager.GetIpGroups();
    }

    [HttpPost("proxies")]
    public async Task<ProxyInfo> SetProxy([FromBody] ProxyInfo proxy)
    {
        await vpsInstanceManager.AddProxy(proxy);
        return proxy;
    }
    [HttpGet("proxies")]
    public async Task<IEnumerable<ProxyInfo>> GetProxies()
    {
        return await vpsInstanceManager.GetProxies();
    }
    [HttpDelete("proxies/{id}")]
    public async Task DeleteProxy(string ip)
    {
        await vpsInstanceManager.DeleteProxy(ip);
    }

    public class VpsSettingUpdateRequest
    {
        public string Setting { get; set; }
        public string Value { get; set; }
    }
}

public class VpsCreateRequest
{
    public string AppKind { get; set; }
    public string mcName { get; set; }
}