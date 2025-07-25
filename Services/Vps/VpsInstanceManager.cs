using System;
using System.Collections;
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
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Commands;
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
    private Table<ProxyInfo> proxyTable;
    public event Action<VPsStateUpdate> OnInstanceCreated;
    private SettingsService settingsService;
    private ConnectionMultiplexer redis;
    private Dictionary<string, DateTime> activeInstances = new();
    private ILogger<VpsInstanceManager> logger;
    private IConfiguration configuration;
    private IUserApi userApi;
    private ITopUpApi topUpApi;
    private IdConverter idConverter;

    public VpsInstanceManager(ISession session, SettingsService settingsService, ConnectionMultiplexer redis, ILogger<VpsInstanceManager> logger, IConfiguration configuration, IUserApi userApi, ITopUpApi topUpApi, IdConverter idConverter)
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
        var proxyMapping = new MappingConfiguration().Define(
            new Map<ProxyInfo>()
                .TableName("vps_proxy")
                .PartitionKey(u => u.IP)
        );
        proxyTable = new Table<ProxyInfo>(session, proxyMapping);
        proxyTable.CreateIfNotExists();
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
        this.userApi = userApi;
        this.topUpApi = topUpApi;
        this.idConverter = idConverter;
    }

    public void Connected(string ip)
    {
        if (ip == "1.1.1.1")
            return; // test server
        var conSate = redis.GetSubscriber().Publish(RedisChannel.Literal("vps:connected"), ip);
        var received = redis.GetSubscriber().Publish(RedisChannel.Literal("vps:state"), JsonConvert.SerializeObject(new VPsStateUpdate()));
        logger.LogInformation($"{received} connections received state - {conSate}");
    }

    public async Task<Instance> CreateVps(string userId, string mcName, string appKind)
    {
        var instance = new Instance
        {
            OwnerId = userId,
            AppKind = appKind,
            CreatedAt = DateTime.UtcNow,
            PaidUntil = DateTime.UtcNow.AddDays(1),
        };
        var secret = Guid.NewGuid().ToString();
        (_, var hashed) = idConverter.ComputeConnectionId(mcName, secret);
        await AddVps(instance, new()
        {
            SessionId = secret,
            UserName = mcName,
        });
        using var accountInfo = await SelfUpdatingValue<AccountInfo>.Create(userId, "accountInfo", () => null);
        accountInfo.Value.ConIds.Add(hashed); // auth that id
        await accountInfo.Update();
        return instance;
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
            throw new CoflnetException("no_active_instances", "There are no active hosts available, please try again later!");
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
        var configValue = await GetVpsConfig(instance);
        configValue.skip ??= new();
        GenericSettingsUpdater updater = GetUpdater(instance);
        configValue.doNotRelist ??= new();
        configValue.sellInventory ??= new();
        configValue.skip ??= new();
        updater.Update(configValue, key, value);
        await UpdateVpsConfig(instance, configValue);
    }

    private static GenericSettingsUpdater GetUpdater(Instance instance)
    {
        return instance.AppKind switch
        {
            "tpm+" => GetUpdater<TPM.TpmPlusConfig>(),
            "tpm" => GetUpdater<TPM.TpmConfig>(),
            _ => throw new CoflnetException("invalid_app_kind", "The app kind your instance has is unknown, please let support know about this issue or try resetting your instance")
        };
    }

    public async Task ImportSettings(Instance instance, string settings)
    {
        try
        {

            var parsed = JsonConvert.DeserializeObject<TPM.TpmPlusConfig>(settings)
                ?? throw new CoflnetException("invalid_settings", "The settings are invalid");
            await UpdateVpsConfig(instance, parsed);
        }
        catch (JsonSerializationException e)
        {
            throw new CoflnetException("invalid_settings", "Settings are not valid format: " + e.Message);
        }
        catch (JsonReaderException e)
        {
            throw new CoflnetException("invalid_settings", "Settings are not valid format: " + e.Message);
        }
    }

    public Dictionary<string, SettingsUpdater.SettingDoc> SettingOptions()
    {
        var updater = GetUpdater<TPM.TpmPlusConfig>();
        var options = updater.ModOptions;
        return options.ToDictionary(k => k.Key, v => v.Value);
    }

    private static GenericSettingsUpdater GetUpdater<T>() where T : new()
    {
        var updater = new GenericSettingsUpdater();
        updater.AddSettings(typeof(T), "");
        updater.AddSettings(typeof(TPM.Skip), "skip", s => (s as TPM.TpmPlusConfig).skip);
        updater.AddSettings(typeof(TPM.DoNotRelist), "norelist", s => (s as TPM.TpmPlusConfig).doNotRelist);
        if (typeof(T) == typeof(TPM.TpmPlusConfig)) // only available in TPM+
            updater.AddSettings(typeof(TPM.SellInventory), "sell", s => (s as TPM.TpmPlusConfig).sellInventory);
        return updater;
    }

    private async Task PublishUpdate(Instance instance, CreateOptions options, TPM.TpmPlusConfig configValue = null)
    {
        var update = await BuildFullUpdate(instance, options, configValue);
        redis.GetSubscriber().Publish(RedisChannel.Literal("vps:state"), JsonConvert.SerializeObject(update));
        logger.LogInformation($"Published update for {instance.Id}");
    }

    public async Task<IEnumerable<VPsStateUpdate>> GetRunningVps(string hostMachineIp)
    {
        var instance = await vpsTable.Where(v => v.HostMachineIp == hostMachineIp).ExecuteAsync();
        var result = new List<VPsStateUpdate>();
        foreach (var i in instance)
        {
            if (i.PaidUntil < DateTime.UtcNow)
            {
                // skip expired instances
                continue;
            }
            VPsStateUpdate update = await BuildFullUpdate(i);
            result.Add(update);
        }
        return result;
    }

    public async Task<List<Instance>> GetVpsForUser(string userId)
    {
        var instance = await vpsTable.Where(v => v.OwnerId == userId).ExecuteAsync();
        var all = instance.ToList();
        foreach (var item in all.GroupBy(i => i.Id).Where(g => g.Count() > 1))
        {
            logger.LogWarning($"Found duplicate instance {item.Key} for user {userId}, removing duplicates");
            foreach (var duplicate in item.OrderByDescending(i => i.PublicIp != null ? 1 : 0).Skip(1))
            {
                await vpsTable.Where(v => v.HostMachineIp == duplicate.HostMachineIp && v.Id == duplicate.Id).Delete().ExecuteAsync();
            }
        }

        return all;
    }

    public async Task PersistExtra(string userId, string extraConfig)
    {
        await settingsService.UpdateSetting(userId, "tpm_extra_config", extraConfig);
    }

    private async Task<VPsStateUpdate> BuildFullUpdate(Instance i, CreateOptions options = null, TPM.TpmPlusConfig tpmConfig = null)
    {
        tpmConfig ??= await settingsService.GetCurrentValue<TPM.TpmPlusConfig>(i.OwnerId, "tpm_config", () => CreatedConfigs(i, options));
        tpmConfig ??= CreatedConfigs(i, options); // fallback to default config if not found
        var extraConfig = await settingsService.GetCurrentValue<string>(i.OwnerId, "tpm_extra_config", () => "");
        if (i.AppKind != "tpm" && i.AppKind != "tpm+")
        {
            i.AppKind = i.AppKind.Contains('+') ? "tpm+" : "tpm"; // ensure app kind is correct for discord issue
            await vpsTable.Insert(i).ExecuteAsync(); // update app kind in db
        }
        var update = new VPsStateUpdate
        {
            Config = tpmConfig,
            Instance = i,
            ExtraConfig = extraConfig
        };
        return update;
    }

    private TPM.TpmPlusConfig CreatedConfigs(Instance instance = null, CreateOptions options = null)
    {
        var withoutComments = Regex.Replace(instance?.AppKind == "tpm+" ? TPM.PlusDefault : TPM.NormalDefault, @"//.*\n", "");
        var split = withoutComments.Split("\n").Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        for (int i = 0; i < split.Length; i++)
        {
            Console.WriteLine((i + 1) + " " + split[i]);
        }
        var combined = string.Join("\n", split);
        var deserialized = JsonConvert.DeserializeObject<TPM.TpmPlusConfig>(combined);
        if (options != null)
        {
            deserialized.igns = [options.UserName];
            deserialized.session = options.SessionId;
        }
        return deserialized;
    }

    internal async Task<TPM.TpmPlusConfig> GetVpsConfig(Instance instance)
    {
        return await settingsService.GetCurrentValue<TPM.TpmPlusConfig>(instance.OwnerId, "tpm_config", () => CreatedConfigs(instance));
    }

    internal async Task UpdateVpsConfig(Instance instance, TPM.TpmPlusConfig configValue)
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
        if (instance.PaidUntil < DateTime.UtcNow)
        {
            throw new CoflnetException("expired", "The instance has expired, please renew it");
        }
        var activeOnServer = (await vpsTable.Where(v => v.HostMachineIp == instance.HostMachineIp).ExecuteAsync())
            .Where(v => v.PaidUntil > DateTime.UtcNow && !v.Context.ContainsKey("turnedOff") && v.Id != instance.Id && v.PublicIp == null).ToList();
        if (instance.PublicIp == null && activeOnServer.Count >= 3)
        {
            // to many on one server try to reassign
            await ReassignVps(instance);
        }
        instance.Context.Remove("turnedOff");
        await UpdateAndPublish(instance);
        if (!activeInstances.TryGetValue(instance.HostMachineIp, out var hostContact) || hostContact < DateTime.UtcNow.AddMinutes(-10))
        {
            await ReassignVps(instance);
        }
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
        string putOn = await GetAvailableServer(instance.HostMachineIp);
        var previousIp = instance.HostMachineIp;
        if (previousIp == putOn)
        {
            throw new CoflnetException("no_change", "The instance is already on the best server");
        }
        await TurnOffVps(instance); // make sure it doesn't keep running on the old server
        instance.HostMachineIp = putOn;
        instance.PublicIp = null; // reset public IP to use host system
        instance.Context.Remove("turnedOff");
        await UpdateAndPublish(instance);
        await vpsTable.Where(v => v.HostMachineIp == previousIp && v.Id == instance.Id).Delete().ExecuteAsync();
    }

    public async Task<string> GetAvailableServer(string avoidIp = null)
    {
        var allActive = (await vpsTable.ExecuteAsync()).Where(v => v.PaidUntil > DateTime.UtcNow).ToList();
        var grouped = allActive.GroupBy(v => v.HostMachineIp).ToDictionary(g => g.Key, g => (total: g.Count(), running: g.Count(s => !s.Context.ContainsKey("turnedOff") && s.PublicIp == null)));
        var putOn = activeInstances.Where(a => a.Value > DateTime.UtcNow.AddMinutes(-10))
                .OrderBy(v => grouped.GetValueOrDefault(v.Key).running + (v.Key == avoidIp ? 3 : 0)) // least other instances
                .Select(a => a.Key).FirstOrDefault();
        if (putOn == null)
        {
            logger.LogError("No active instances available {active}", JsonConvert.SerializeObject(activeInstances));
            throw new CoflnetException("no_active_instances", "There are no active hosts available, please try again later");
        }
        var active = grouped.GetValueOrDefault(putOn);
        if (active.running >= 3)
        {
            throw new CoflnetException("too_many_instances", "It looks like we are out of servers to put you on. Thanks for your interest but we currently can't provide an instance to you, but please check back tomorrow");
        }

        return putOn;
    }

    internal async Task UpdateInstance(Instance instance)
    {
        await UpdateAndPublish(instance);
    }

    internal async Task<string> GetLog(string token, long timeStamp, string user = null)
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
        if (!string.IsNullOrWhiteSpace(user))
        {
            if (!int.TryParse(user, out _))
            {
                if (user.Length != 32)
                    user = await DiHandler.GetService<PlayerName.PlayerNameService>()
                                .GetUuid(user);
                var mcService = DiHandler.GetService<McAccountService>();
                user = (await mcService.GetUserId(user.Trim('"'))).ExternalId;
            }
            query = $"{{user_id=\"{user}\", container=\"tpm-manager\"}}";
        }
        var start = parsed.AddHours(-24).ToUnixTimeSeconds();
        var end = timeStamp;
        var log = await QueryLokiJson(query, start, end, 5_000);
        return string.Join("\n", log.data.result.SelectMany(r => r.values.Select(v => (long.Parse(v[0]), v[0] + "-" + r.stream.user_id + ": " + v[1]))).OrderBy(v => v.Item1).Select(v => v.Item2));
    }

    internal async Task DeleteVps(Instance instance)
    {
        await TurnOffVps(instance);
        await vpsTable.Where(v => v.HostMachineIp == instance.HostMachineIp && v.Id == instance.Id).Delete().ExecuteAsync();
    }

    internal async Task<Dictionary<string, string>> GetSettings(string userId, Instance instance)
    {
        var configValue = await GetVpsConfig(instance);
        var updater = GetUpdater(instance);
        var options = updater.ModOptions;
        var result = new Dictionary<string, string>();
        foreach (var option in options)
        {
            var value = updater.GetCurrent(configValue, option.Key);
            result.Add(option.Key, Format(value));
        }
        return result;

        static string Format(object value)
        {
            if (value is Dictionary<string, string> dict)
            {
                return string.Join("\n", dict.Select(kv => $"{kv.Key} {kv.Value}"));
            }
            else if (value is IEnumerable enumerable && !(value is string))
            {
                return string.Join(",", enumerable.Cast<object>());
            }
            return value?.ToString() ?? "";
        }
    }

    internal async Task ExtendVps(Instance instance)
    {
        // checks that there is a server available
        await GetAvailableServer();
        var kind = instance.AppKind switch
        {
            "tpm+" => "vps+",
            _ => "vps"
        };
        var currentTime = await userApi.UserUserIdOwnsProductSlugUntilGetAsync(instance.OwnerId, kind);
        if (currentTime > DateTime.UtcNow.AddDays(25))
        {
            instance.PaidUntil = currentTime;
            await UpdateAndPublish(instance);
            throw new CoflnetException("already_extended", "The instance has more than 25 days left so you can't extend it yet");
        }
        try
        {
            await userApi.UserUserIdServicePurchaseProductSlugPostAsync(instance.OwnerId, kind, instance.Id.ToString().Split('-').Last() + DateTime.UtcNow.ToString("-yyyyMMdd"), 1);
        }
        catch (Payments.Client.Client.ApiException ex)
        {
            if (ex.Message.Contains("already purchased"))
            {
                throw new CoflnetException("already_purchased", "You already extended this instance, can only be done once a day");
            }
            if (ex.Message.Contains("insuficcient balance"))
            {
                throw new CoflnetException("not_enough_coins", "You don't have enough CoflCoins to extend this instance, you can get some at https://sky.coflnet.com/premium");
            }
            throw;
        }
        var time = await userApi.UserUserIdOwnsProductSlugUntilGetAsync(instance.OwnerId, kind);
        instance.PaidUntil = time;
        await UpdateAndPublish(instance);
        logger.LogInformation($"Extended instance {instance.Id} to {time}");

        if (instance.AppKind == "tpm+")
            await topUpApi.TopUpCustomPostAsync("28258", new()
            {
                Amount = 1800,
                ProductId = "compensation",
                Reference = "tpm+" + instance.Id.ToString().Split('-').Last() + "-" + DateTime.UtcNow.ToString("yyyyMMdd"),
            });
        else if (instance.AppKind == "tpm")
            await topUpApi.TopUpCustomPostAsync("28258", new()
            {
                Amount = 300,
                ProductId = "compensation",
                Reference = "tpm" + instance.Id.ToString().Split('-').Last() + "-" + DateTime.UtcNow.ToString("yyyyMMdd"),
            });

        if (instance.PublicIp == null && instance.HostMachineIp.StartsWith("23."))
        {
            var allProxies = (await proxyTable.ExecuteAsync()).ToList();
            var allServers = (await vpsTable.ExecuteAsync()).Where(v => v.PaidUntil > DateTime.UtcNow && v.PublicIp != null).ToList();
            var usedCount = allServers.GroupBy(v => v.PublicIp).ToDictionary(g => g.Key, g => g.Count());
            var fewestOthers = allProxies.OrderBy(p => usedCount.GetValueOrDefault(p.IP)).First();
            instance.PublicIp = fewestOthers.IP;
            await UpdateAndPublish(instance);
            logger.LogInformation($"Assigned public IP {fewestOthers.IP} to instance {instance.Id}");
        }
    }

    internal async Task<string> ExportSettings(Instance instance)
    {
        var configValue = await GetVpsConfig(instance);
        configValue.session = null;
        var serialized = JsonConvert.SerializeObject(configValue);
        return serialized;
    }

    internal async Task SetPublicIp(Instance instance, string ip)
    {
        var isDelete = ip == "delete";
        if (!isDelete && !System.Net.IPAddress.TryParse(ip, out _))
        {
            throw new CoflnetException("invalid_proxy_format", "The provided address is not a valid ip format for a SOCKS5 proxy.");
        }

        // Further validation could involve attempting a connection, but that's complex.
        // For now, we assume format validation is sufficient.
        instance.PublicIp = ip;
        if (isDelete)
            instance.PublicIp = null;
        await UpdateAndPublish(instance);
        logger.LogInformation($"Set public IP for instance {instance.Id} to {ip}");
    }

    internal async Task<Dictionary<string, IEnumerable<string>>> GetIpGroups(bool preventProxy = false)
    {
        var allActive = (await vpsTable.ExecuteAsync()).Where(v => v.PaidUntil > DateTime.UtcNow).ToList();
        var grouped = allActive.GroupBy(v => v.PublicIp == null || preventProxy ? v.HostMachineIp : v.PublicIp).ToDictionary(g => g.Key, g => g.Select(s => $"{s.Id}/{(s.Context.ContainsKey("turnedOff") ? "Off" : "running")}/{s.PaidUntil}/{s.OwnerId}"));
        return grouped;
    }

    internal async Task AddProxy(ProxyInfo proxy)
    {
        if (!System.Net.IPAddress.TryParse(proxy.IP, out _))
        {
            throw new CoflnetException("invalid_proxy_format", "The provided address is not a valid ip format for a SOCKS5 proxy.");
        }
        var existing = await proxyTable.Where(p => p.IP == proxy.IP).ExecuteAsync();
        if (existing.Any())
        {
            throw new CoflnetException("proxy_already_exists", "The provided proxy already exists in the database.");
        }
        await proxyTable.Insert(proxy).ExecuteAsync();
        logger.LogInformation($"Added new proxy {proxy.IP}");
    }

    public async Task<IEnumerable<ProxyInfo>> GetProxies()
    {
        var proxies = await proxyTable.ExecuteAsync();
        return proxies.ToList();
    }

    internal async Task DeleteProxy(string ip)
    {
        await proxyTable.Where(p => p.IP == ip).Delete().ExecuteAsync();
        logger.LogInformation($"Deleted proxy {ip}");
    }

    internal async Task ResetVps(Instance instance, string mcName, string appKind)
    {
        if(instance.PaidUntil > DateTime.UtcNow.AddDays(2))
        {
            throw new CoflnetException("too_far_in_future", "You can only switch the instance if it has less than 2 days left");
        }
        instance.AppKind = appKind;
        await UpdateAndPublish(instance);
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
