using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using StackExchange.Redis;

namespace Coflnet.Sky.ModCommands.Services;

internal sealed class OwnedConfigLock : IAsyncDisposable
{
    private readonly IDatabase database;
    private readonly RedisKey key;
    private readonly RedisValue token;

    private OwnedConfigLock(IDatabase database, RedisKey key, RedisValue token)
    {
        this.database = database;
        this.key = key;
        this.token = token;
    }

    public static async Task<OwnedConfigLock> Acquire(
        SettingsService settings,
        string userId)
    {
        var database = settings.Con?.GetDatabase()
            ?? throw new CoflnetException(
                "config_storage_unavailable",
                "Config access storage is temporarily unavailable.");
        RedisKey key = $"lock:owned-configs:{userId}";
        RedisValue token = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await database.LockTakeAsync(
                    key, token, TimeSpan.FromMinutes(2)))
                return new(database, key, token);
            await Task.Delay(250);
        }
        throw new CoflnetException(
            "config_update_busy",
            "Another Config update is still running. Please try again.");
    }

    public async ValueTask DisposeAsync() =>
        await database.LockReleaseAsync(key, token);
}
