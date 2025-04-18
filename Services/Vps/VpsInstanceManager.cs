using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        var conSate = redis.GetSubscriber().Publish(RedisChannel.Literal("vps:connected"), ip);
        var received = redis.GetSubscriber().Publish(RedisChannel.Literal("vps:state"), JsonConvert.SerializeObject(new VPsStateUpdate()));
        logger.LogInformation($"{received} connections received state - {conSate}");
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
        var putOn = await GetAvailableServer();
        if (instance.Id == Guid.Empty)
            instance.Id = Guid.NewGuid();
        instance.HostMachineIp = putOn;
        instance.Context = new Dictionary<string, string>();
        instance.Context["sessionId"] = options.SessionId;
        await vpsTable.Insert(instance).ExecuteAsync();
        await PublishUpdate(instance, options);
        logger.LogInformation($"Created new instance {instance.Id} on {instance.HostMachineIp}");
    }

    public async Task UpdateSetting(string userId, string key, string value, Instance instance)
    {
        var configValue = await GetVpsConfig(userId);
        configValue.skip ??= new();
        var updater = new GenericSettingsUpdater();
        updater.AddSettings(typeof(TPM.Config), "");
        updater.AddSettings(typeof(TPM.Skip), "skip", s => (s as TPM.Config).skip);
        updater.AddSettings(typeof(TPM.DoNotRelist), "relist", s => (s as TPM.Config).relist);
        updater.AddSettings(typeof(TPM.SellInventory), "sell", s => (s as TPM.Config).sellInventory);
        configValue.doNotRelist ??= new();
        configValue.sellInventory ??= new();
        configValue.skip ??= new();
        updater.Update(configValue, key, value);
        await UpdateVpsConfig(instance, configValue);
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

    internal async Task<IEnumerable<string>> GetVpsLog(Instance instance, DateTimeOffset from, DateTimeOffset to)
    {
        var query = $"{{container=\"tpm-manager\", instance_id=\"{instance.Id}\"}}";
        var start = from.ToUnixTimeSeconds();
        var end = to.ToUnixTimeSeconds();
        return await QueryLoki(query, start, end);
    }

    private async Task<IEnumerable<string>> QueryLoki(string query, long start, long end, int limit = 20)
    {
        Root root = await QueryLokiJson(query, start, end, limit);
        return root.data.result.SelectMany(r => r.values).Select(v => v[1]);
    }

    private async Task<Root> QueryLokiJson(string query, long start, long end, int limit)
    {
        var client = new RestClient(configuration["LOKI_BASE_URL"]);
        var request = new RestRequest("loki/api/v1/query_range", Method.Get);
        request.AddQueryParameter("query", query);
        request.AddQueryParameter("start", start);
        request.AddQueryParameter("end", end);
        request.AddQueryParameter("limit", limit);
        var response = await client.ExecuteAsync(request);
        logger.LogInformation($"Querying loki with {client.BuildUri(request)}");
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new CoflnetException("loki_error", $"The loki server returned an error {response.StatusCode} {response.Content}");
        }
        var root = JsonConvert.DeserializeObject<Root>(response.Content);
        return root;
    }

    internal async Task ReassignVps(Instance instance)
    {
        string putOn = await GetAvailableServer();
        var previousIp = instance.HostMachineIp;
        if (previousIp == putOn)
        {
            throw new CoflnetException("no_change", "The instance is already on the best server");
        }
        instance.HostMachineIp = putOn;
        await UpdateAndPublish(instance);
        await vpsTable.Where(v => v.HostMachineIp == previousIp && v.Id == instance.Id).Delete().ExecuteAsync();
    }

    private async Task<string> GetAvailableServer()
    {
        var allActive = (await vpsTable.ExecuteAsync()).Where(v => v.PaidUntil > DateTime.UtcNow).ToList();
        var grouped = allActive.GroupBy(v => v.HostMachineIp).ToDictionary(g => g.Key, g => g.Count());
        var putOn = activeInstances.Where(a => a.Value > DateTime.UtcNow.AddMinutes(-50))
                .OrderBy(v => grouped.GetValueOrDefault(v.Key)) // least other instances
                .Select(a => a.Key).FirstOrDefault();
        if (putOn == null)
        {
            throw new CoflnetException("no_active_instances", "There are no active hosts available, please try again later");
        }
        if (grouped.GetValueOrDefault(putOn) > 3)
        {
            throw new CoflnetException("too_many_instances", "It looks like we are out of servers to put you on. Thanks for your interest but we currently can't provide an instance to you, but please check back tomorrow");
        }

        return putOn;
    }

    internal async Task UpdateInstance(Instance instance)
    {
        await UpdateAndPublish(instance);
    }

    internal async Task<string> GetLog(string token, long timeStamp)
    {
        var compareHash = configuration["VPS:LOG_TOKEN"];
        var hashed = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var hex = BitConverter.ToString(hashed).Replace("-", "").ToLower();
        if (hex != compareHash)
        {
            Console.WriteLine($"Token {token} does not match {compareHash}");
            throw new CoflnetException("invalid_token", "The token is invalid");
        }

        var parsed = DateTimeOffset.FromUnixTimeSeconds(timeStamp);
        if (parsed > DateTimeOffset.UtcNow)
        {
            throw new CoflnetException("invalid_timestamp", "The timestamp is in the future, use unix seconds");
        }
        if (parsed < DateTimeOffset.UtcNow.AddDays(-4))
        {
            throw new CoflnetException("too_old", "The timestamp is older than 4 days, we only store logs for 4 days");
        }
        var query = $"{{container=\"tpm-manager\"}}";
        var start = parsed.AddHours(-24).ToUnixTimeSeconds();
        var end = timeStamp;
        var log = await QueryLokiJson(query, start, end, 5_000);
        return string.Join("\n", log.data.result.SelectMany(r=>r.stream.user_id + ": " + r.values[0][1]));
    }

    internal async Task DeleteVps(Instance instance)
    {
        await TurnOffVps(instance);
        await vpsTable.Where(v => v.HostMachineIp == instance.HostMachineIp && v.Id == instance.Id).Delete().ExecuteAsync();
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
