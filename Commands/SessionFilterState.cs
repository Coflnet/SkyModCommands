using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;
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
        BaseConfig?.Dispose();
    }

    public async Task SubToConfigChanges()
    {
        var socket = lifesycle.socket;
        var AccountSettings = lifesycle.AccountSettings;
        var loadedConfigMetadata = AccountSettings.Value.LoadedConfig;
        if (lifesycle.TierManager.IsLicense)
        {
            var licenseInfo = await socket.GetService<SettingsService>().GetCurrentValue<LicenseSetting>(socket.UserId, "licenses", () => new LicenseSetting());
            var targetSettings = licenseInfo.Licenses.FirstOrDefault(l => l.UseOnAccount == socket.SessionInfo.McUuid);
            if (targetSettings != null && targetSettings.ConfigUsed != null)
            {
                if (targetSettings.ConfigUsed.StartsWith("backup:"))
                {
                    var backupConfig = await BackupCommand.GetBackupList(socket);
                    var backup = backupConfig.FirstOrDefault(b => b.Name == targetSettings.ConfigUsed.Substring(7));
                    if (backup != null)
                    {
                        lifesycle.FlipSettings = await SelfUpdatingValue<FlipSettings>.CreateNoUpdate(() => backup.settings);
                        socket.Dialog(db => db.MsgLine($"Backupconfig with name §6{backup.Name} §6loaded"));
                        return;
                    }
                    socket.Dialog(db => db.MsgLine($"Backupconfig with name §6{targetSettings.ConfigUsed.Substring(7)} §6not found, so default config loaded"));
                }
                else
                {
                    var ownedConfigs = await OwnConfigsCommand.GetOwnConfigs(socket);
                    loadedConfigMetadata = ownedConfigs.FirstOrDefault(c => c.Name == targetSettings.ConfigUsed);
                }
            }
        }
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
        if (lifesycle.socket.IsClosed)
            return;
        await lifesycle.socket.GetService<ConfigStatsService>().AddLoad(
            loadedConfigMetadata.OwnerId,
            loadedConfigMetadata.Name,
            lifesycle.SessionInfo.McUuid,
            lifesycle.AccountInfo.Value.UserId,
            loadedConfigMetadata.Version
        );

        void ShowConfigUpdateOption(OwnedConfigs.OwnedConfig loadedConfigMetadata, ConfigContainer newConfig)
        {
            span.Log($"new config {newConfig.Name} {newConfig.Version} > {loadedConfigMetadata.Version}");
            if (newConfig.Version > loadedConfigMetadata.Version)
            {
                var diffSuffix = "";
                if (newConfig.Diffs?.TryGetValue(newConfig.Version, out var diff) ?? false)
                {
                    diffSuffix = $" | {diff.GetDiffCount()} changes";
                }
                socket.Dialog(db => db.MsgLine($"Your config: §6{newConfig.Name} §7v{loadedConfigMetadata.Version} §6updated to v{newConfig.Version}")
                    .MsgLine($"§7{newConfig.ChangeNotes} {diffSuffix}")
                    .If(() => AccountSettings.Value.AutoUpdateConfig, db => db.MsgLine("Loading the updated version automatically.").Msg("To toggle this run /cofl configs autoupdate").AsGray(),
                    db => db.CoflCommand<UpdateCurrentConfigCommand>($"[click to load]", $"", "load new version\nWill override your current settings")));
                if (AccountSettings.Value.AutoUpdateConfig)
                {
                    MinecraftSocket.Commands["updatecurrentconfig"].Execute(socket, "");
                }
            }
        }
    }

    private async Task SubBaseConfig(ConfigContainer childConfig)
    {
        if (string.IsNullOrWhiteSpace(childConfig.Settings.BasedConfig))
            return;
        BaseConfig = await LoadConfigCommand.GetContainer(lifesycle.socket, childConfig.Settings.BasedConfig);
        if (BaseConfig.Value.Version > lifesycle.AccountSettings.Value.BaseConfigVersion && BaseConfig.Value.Version > 0)
        {
            BaseConfigUpdate(childConfig, BaseConfig);
        }
        BaseConfig.OnChange += (newBaseConfig) =>
        {
            BaseConfigUpdate(childConfig, newBaseConfig);
        };
    }

    private void BaseConfigUpdate(ConfigContainer childConfig, ConfigContainer newBaseConfig)
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
    }
}
