using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;
public class SessionFilterState : IDisposable
{
    public SelfUpdatingValue<ConfigContainer> LoadedConfig;
    public SelfUpdatingValue<ConfigContainer> BaseConfig;
    private readonly ModSessionLifesycle lifesycle;

    public SessionFilterState(ModSessionLifesycle lifesycle)
    {
        this.lifesycle = lifesycle;
    }

    public void Dispose()
    {
        LoadedConfig?.Dispose();
    }

    public async Task SubToConfigChanges()
    {
        var socket = lifesycle.socket;
        var AccountSettings = lifesycle.AccountSettings;
        var loadedConfigMetadata = AccountSettings.Value.LoadedConfig;
        if (loadedConfigMetadata == null)
            return;
        using var span = socket.CreateActivity("subToConfigChanges", lifesycle.ConSpan);
        if (AccountSettings.Value == null)
            await AccountSettings.Update(new AccountSettings());
        span.Log("loaded config " + loadedConfigMetadata?.Name);
        if (loadedConfigMetadata != null)
        {
            LoadedConfig = await SelfUpdatingValue<ConfigContainer>.Create(loadedConfigMetadata.OwnerId, SellConfigCommand.GetKeyFromname(loadedConfigMetadata.Name), () => throw new Exception("config not found"));
            span.Log("got config " + LoadedConfig?.Value?.Name);
            if (LoadedConfig.Value != null)
            {
                var newConfig = LoadedConfig.Value;
                ShowConfigUpdateOption(loadedConfigMetadata, newConfig);
                LoadedConfig.OnChange += (config) => ShowConfigUpdateOption(loadedConfigMetadata, config);
                await SubBaseConfig(newConfig);
            }
        }

        void ShowConfigUpdateOption(OwnedConfigs.OwnedConfig loadedConfigMetadata, ConfigContainer newConfig)
        {
            span.Log($"new config {newConfig.Name} {newConfig.Version} > {loadedConfigMetadata.Version}");
            if (newConfig.Version > loadedConfigMetadata.Version)
            {
                socket.Dialog(db => db.MsgLine($"Your config: §6{newConfig.Name} §7v{loadedConfigMetadata.Version} §6updated to v{newConfig.Version}")
                    .MsgLine($"§7{newConfig.ChangeNotes}")
                    .If(() => AccountSettings.Value.AutoUpdateConfig, db => db.MsgLine("Loading the updated version automatically.").Msg("To toggle this run /cofl configs autoupdate").AsGray(),
                    db => db.CoflCommand<LoadConfigCommand>($"[click to load]", $"{newConfig.OwnerId} {newConfig.Name}", "load new version\nWill override your current settings")));
                if (AccountSettings.Value.AutoUpdateConfig)
                {
                    socket.ExecuteCommand("/cofl loadconfig " + newConfig.OwnerId + " " + newConfig.Name);
                }
            }
        }
    }

    private async Task SubBaseConfig(ConfigContainer childConfig)
    {
        if (childConfig.Settings.BasedConfig == null)
            return;
        var baseConfig = await LoadConfigCommand.GetContainer(lifesycle.socket, childConfig.Settings.BasedConfig);
        baseConfig.OnChange += (newBaseConfig) =>
        {
            var autoUpdate = lifesycle.AccountSettings.Value.AutoUpdateConfig;
            lifesycle.socket.Dialog(db => db.MsgLine($"Your base config: §6{newBaseConfig.Name} §6updated to v{newBaseConfig.Version}")
                    .MsgLine($"§7{childConfig.ChangeNotes}")
                    .If(() => autoUpdate, db => db.MsgLine("Loading the updated version automatically.").Msg("To toggle this run /cofl configs autoupdate").AsGray(),
                    db => db.CoflCommand<LoadConfigCommand>($"[click to load]", $"{childConfig.OwnerId} {childConfig.Name}", "load new version\nWill override your current settings")));
            if (autoUpdate)
            {
                lifesycle.socket.ExecuteCommand("/cofl loadconfig " + childConfig.OwnerId + " " + childConfig.Name);
            }
        };
    }
}