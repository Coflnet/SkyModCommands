using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using StackExchange.Redis;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Stores and syncs who is slowed down
/// </summary>
public class DelayService
{
    private static RedisChannel ChannelKey = RedisChannel.Literal("_slowdown");
    public static readonly TimeSpan DelayTime = TimeSpan.FromMinutes(5);
    private HashSet<string> _slowDowns = new HashSet<string>();
    private SettingsService settingsService;

    public DelayService(SettingsService settingsService)
    {
        this.settingsService = settingsService;
        settingsService?.Con.GetSubscriber().Subscribe(ChannelKey, (channel, message) =>
        {
            _slowDowns.Add(message);
            Task.Delay(DelayTime).ContinueWith(_ => _slowDowns.Remove(message));
        });
    }

    public bool IsSlowedDown(string uuid)
    {
        return _slowDowns.Contains(uuid);
    }
    public void SlowDown(string uuid)
    {
        _slowDowns.Add(uuid);
        settingsService.Con.GetSubscriber().Publish(ChannelKey, uuid);
    }
}