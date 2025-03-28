using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;
using StackExchange.Redis;

namespace Coflnet.Sky.ModCommands.Services.Vps;

public class VpsInstanceManager
{
    private Table<Instance> vpsTable;
    public event Action<VPsStateUpdate> OnInstanceCreated;
    private SettingsService settingsService;
    private ConnectionMultiplexer redis;
    private Dictionary<string, DateTime> activeInstances = new();
    private ILogger<VpsInstanceManager> logger;
    private IConfiguration configuration;

    public VpsInstanceManager(ISession session, SettingsService settingsService, ConnectionMultiplexer redis, ILogger<VpsInstanceManager> logger, IConfiguration configuration)
    {
        var mapping = new MappingConfiguration().Define(
            new Map<Instance>()
                .TableName("vps")
                .PartitionKey(u => u.HostMachineIp)
                .ClusteringKey(u => u.Id)
                .Column(u => u.Id, cm => cm.WithName("id").WithSecondaryIndex())
                .Column(u => u.HostMachineIp, cm => cm.WithName("host_machine_ip"))
                .Column(u => u.OwnerId, cm => cm.WithName("owner_id").WithSecondaryIndex())
                .Column(u => u.AppKind, cm => cm.WithName("app_kind"))
                .Column(u => u.CreatedAt, cm => cm.WithName("created_at"))
                .Column(u => u.PaidUntil, cm => cm.WithName("paid_until"))
                .Column(u => u.PublicIp, cm => cm.WithName("public_ip"))
        );
        vpsTable = new Table<Instance>(session, mapping);
        vpsTable.CreateIfNotExists();
        this.settingsService = settingsService;
        this.redis = redis;

        redis.GetSubscriber().Subscribe(RedisChannel.Literal("vps:state"), (channel, message) =>
        {
            var update = JsonConvert.DeserializeObject<VPsStateUpdate>(message);
            OnInstanceCreated?.Invoke(update);
        });

        redis.GetSubscriber().Subscribe(RedisChannel.Literal("vps:connected"), (channel, message) =>
        {
            activeInstances[message] = DateTime.UtcNow;
        });
        this.logger = logger;
        this.configuration = configuration;
    }

    public void Connected(string ip)
    {
        redis.GetSubscriber().Publish(RedisChannel.Literal("vps:connected"), ip);
    }

    public async Task AddVps(Instance instance, CreateOptions options)
    {
        var sameUser = await GetVpsForUser(instance.OwnerId);
        if (sameUser.Any())
        {
            throw new CoflnetException("duplicate_instance", "You already have an instance, currently you can only have one");
        }
        if (activeInstances.Count == 0)
        {
            throw new CoflnetException("no_active_instances", "There are no active hosts available, please try again later");
        }
        var allActive = (await vpsTable.ExecuteAsync()).Where(v => v.PaidUntil > DateTime.UtcNow).ToList();
        var grouped = allActive.GroupBy(v => v.HostMachineIp).ToDictionary(g => g.Key, g => g.Count());
        var putOn = activeInstances.Where(a => a.Value > DateTime.UtcNow.AddMinutes(-50))
                .OrderBy(v => grouped.GetValueOrDefault(v.Key)) // least other instances
                .Select(a => a.Key).FirstOrDefault();
        if (grouped.GetValueOrDefault(putOn) > 3)
        {
            throw new CoflnetException("too_many_instances", "It looks like we are out of servers to put you on. Thanks for your interest but we currently can't provide an instance to you, but please check back tomorrow.");
        }
        if (instance.Id == Guid.Empty)
            instance.Id = Guid.NewGuid();
        instance.HostMachineIp = putOn;
        instance.Context = new Dictionary<string, string>();
        instance.Context["sessionId"] = options.SessionId;
        await vpsTable.Insert(instance).ExecuteAsync();
        await PublishUpdate(instance, options);
        logger.LogInformation($"Created new instance {instance.Id} on {instance.HostMachineIp}");
    }

    private async Task PublishUpdate(Instance instance, CreateOptions options, TPM.Config configValue = null)
    {
        var update = await BuildFullUpdate(instance, options, configValue);
        redis.GetSubscriber().Publish(RedisChannel.Literal("vps:state"), JsonConvert.SerializeObject(update));
        logger.LogInformation($"Published update for {instance.Id}");
    }

    public async Task<IEnumerable<VPsStateUpdate>> GetVps(string hostMachineIp)
    {
        var instance = await vpsTable.Where(v => v.HostMachineIp == hostMachineIp).ExecuteAsync();
        var result = new List<VPsStateUpdate>();
        foreach (var i in instance)
        {
            VPsStateUpdate update = await BuildFullUpdate(i);
            result.Add(update);
        }
        return result;
    }

    public async Task<List<Instance>> GetVpsForUser(string userId)
    {
        var instance = await vpsTable.Where(v => v.OwnerId == userId).ExecuteAsync();
        return instance.ToList();
    }

    public async Task PersistExtra(string userId, string extraConfig)
    {
        await settingsService.UpdateSetting(userId, "tpm_extra_config", extraConfig);
    }

    private async Task<VPsStateUpdate> BuildFullUpdate(Instance i, CreateOptions options = null, TPM.Config tpmConfig = null)
    {
        tpmConfig ??= await settingsService.GetCurrentValue<TPM.Config>(i.OwnerId, "tpm_config", () => CreatedConfigs(options));
        var extraConfig = await settingsService.GetCurrentValue<string>(i.OwnerId, "tpm_extra_config", () => "");
        var update = new VPsStateUpdate
        {
            Config = tpmConfig,
            Instance = i,
            ExtraConfig = extraConfig
        };
        return update;
    }

    private TPM.Config CreatedConfigs(CreateOptions options = null)
    {
        var withoutComments = Regex.Replace(TPM.Default, @"//.*\n", "");
        var split = withoutComments.Split("\n").Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        for (int i = 0; i < split.Length; i++)
        {
            Console.WriteLine((i + 1) + " " + split[i]);
        }
        var combined = string.Join("\n", split);
        var deserialized = JsonConvert.DeserializeObject<TPM.Config>(combined);
        if (options != null)
        {
            deserialized.igns = [options.UserName];
            deserialized.session = options.SessionId;
        }
        return deserialized;
    }

    internal async Task<TPM.Config> GetVpsConfig(string userId)
    {
        return await settingsService.GetCurrentValue<TPM.Config>(userId, "tpm_config", () => CreatedConfigs());
    }

    internal async Task UpdateVpsConfig(Instance instance, TPM.Config configValue)
    {
        await settingsService.UpdateSetting(instance.OwnerId, "tpm_config", configValue);
        await PublishUpdate(instance, null, configValue);
    }

    internal async Task TurnOffVps(Instance instance)
    {
        instance.Context["turnedOff"] = "true";
        await UpdateAndPublish(instance);
    }

    private async Task UpdateAndPublish(Instance instance)
    {
        await vpsTable.Insert(instance).ExecuteAsync();
        await PublishUpdate(instance, null);
    }

    internal async Task TurnOnVps(Instance instance)
    {
        instance.Context.Remove("turnedOff");
        await UpdateAndPublish(instance);
    }

    internal async Task<IEnumerable<string>> GetVpsLog(Instance instance)
    {
        // loki/api/v1/query_range?query={container="tpm-manager",%20instance_id="73b43b9b-353d-4ecd-ae2d-ce9dc31a6cd4"}&start=1741929959&end=1742292362&limit=20
        var client = new RestClient(configuration["LOKI_BASE_URL"]);
        var request = new RestRequest("loki/api/v1/query_range", Method.Get);
        request.AddQueryParameter("query", $"{{container=\"tpm-manager\", instance_id=\"{instance.Id}\"}}");
        request.AddQueryParameter("start", DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds().ToString());
        request.AddQueryParameter("end", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        request.AddQueryParameter("limit", "20");
        var response = await client.ExecuteAsync(request);
        var root = JsonConvert.DeserializeObject<Root>(response.Content);
        return root.data.result.SelectMany(r => r.values).Select(v => v[1]);
    }

    public class Root
    {
        [JsonPropertyName("status")]
        public string status { get; set; }

        [JsonPropertyName("data")]
        public Data data { get; set; }
    }

    public class Data
    {
        [JsonPropertyName("result")]
        public Result[] result { get; set; }
    }

    public class Result
    {
        [JsonPropertyName("stream")]
        public Stream stream { get; set; }

        [JsonPropertyName("values")]
        public string[][] values { get; set; }
    }

    public class Stream
    {
        [JsonPropertyName("container")]
        public string container { get; set; }

        [JsonPropertyName("instance_id")]
        public string instance_id { get; set; }
        [JsonPropertyName("user_id")]
        public string user_id { get; set; }
    }


    public class CreateOptions
    {
        public string UserName { get; set; }
        public string SessionId { get; set; }
    }
}
