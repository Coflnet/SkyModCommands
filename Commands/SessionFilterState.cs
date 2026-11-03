using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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

    /// <summary>Always the current value, the session replaces it when the settings are subscribed again</summary>
    private SelfUpdatingValue<AccountSettings> AccountSettings => lifesycle.AccountSettings;

    public async Task SubToConfigChanges()
    {
        var socket = lifesycle.socket;
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
                        await lifesycle.ReplaceFlipSettings(SelfUpdatingValue<FlipSettings>.CreateNoUpdate(backup.settings));
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
        var currentAccess = (await OwnConfigsCommand.GetOwnConfigs(socket)).FirstOrDefault(
            config => config.OwnerId == loadedConfigMetadata.OwnerId
                && config.Name.Equals(loadedConfigMetadata.Name,
                    StringComparison.OrdinalIgnoreCase));
        if (currentAccess == null)
        {
            await ConfigsCommand.Unloadconfig(socket);
            socket.Dialog(db => db.MsgLine(
                "Your Config licence was removed, so the managed Config was unloaded."));
            return;
        }
        loadedConfigMetadata.AccessUntilUtc = currentAccess.AccessUntilUtc;
        if (!BuyConfigCommand.HasManagedUpdates(loadedConfigMetadata))
        {
            socket.Dialog(db => db.MsgLine(
                "Your managed Config update period ended. The supplied version remains loaded, but it will not receive automatic updates."));
            return;
        }
        if (string.IsNullOrWhiteSpace(loadedConfigMetadata.Name))
        {
            await ConfigsCommand.Unloadconfig(socket);
            socket.Dialog(db => db.MsgLine("Your saved loaded config was invalid and has been unloaded.")
                .MsgLine("Your current settings were kept. Load the config again to restore automatic updates."));
            return;
        }
        using var span = socket.CreateActivity("subToConfigChanges", lifesycle.ConSpan);
        if (AccountSettings.Value == null)
            await AccountSettings.Update(new AccountSettings());
        span.Log("loaded config " + loadedConfigMetadata?.Name);
        if (loadedConfigMetadata != null)
        {
            var created = await SelfUpdatingValue<ConfigContainer>.Create(loadedConfigMetadata.OwnerId, SellConfigCommand.GetKeyFromname(loadedConfigMetadata.Name), () => throw new Exception("config not found"));
            // overlapping runs each dispose what they replace, a released session disposes the created value
            if (!lifesycle.TryAdopt(ref LoadedConfig, created))
                return;
            // the field may already hold the value of an overlapping run, this run only hooks its own value
            var newConfig = created.Value;
            span.Log("got config " + newConfig?.Name);
            if (newConfig != null)
            {
                ShowConfigUpdateOption(loadedConfigMetadata, newConfig);
                created.OnChange += (config) => ShowConfigUpdateOption(loadedConfigMetadata, config);
                await SubBaseConfig(newConfig);
            }
        }
        if (lifesycle.socket.IsClosed)
            return;
        await lifesycle.socket.GetService<ConfigStatsService>().AddLoad(
            loadedConfigMetadata.OwnerId,
            loadedConfigMetadata.Name,
            lifesycle.SessionInfo.McUuid,
            lifesycle.AccountInfo?.Value?.UserId ?? socket.UserId,
            loadedConfigMetadata.Version
        );

        void ShowConfigUpdateOption(OwnedConfigs.OwnedConfig loadedConfigMetadata, ConfigContainer newConfig)
        {
            span.Log($"new config {newConfig.Name} {newConfig.Version} > {loadedConfigMetadata.Version}");
            if (newConfig.Version > loadedConfigMetadata.Version)
            {
                using var updateSpan = socket.CreateActivity("configUpdate", span);
                updateSpan?.SetTag("newVersion", newConfig.Version);
                updateSpan?.SetTag("oldVersion", loadedConfigMetadata.Version);
                updateSpan?.SetTag("configName", newConfig.Name);
                updateSpan?.SetTag("ownerId", newConfig.OwnerId);
                updateSpan?.SetTag("userId", socket.UserId);
                var diffSuffix = "";
                if (newConfig.Diffs?.TryGetValue(newConfig.Version, out var diff) ?? false)
                {
                    diffSuffix = $" | {diff.GetDiffCount()} changes";
                }
                socket.Dialog(db => db.MsgLine($"Your config: §6{newConfig.Name} §7v{loadedConfigMetadata.Version} §6updated to v{newConfig.Version}")
                    .MsgLine($"§7{newConfig.ChangeNotes} {diffSuffix}")
                    .If(() => AccountSettings?.Value?.AutoUpdateConfig ?? false, db => db.MsgLine("Loading the updated version automatically.").Msg("To toggle this run /cofl configs autoupdate").AsGray(),
                    db => db.CoflCommand<UpdateCurrentConfigCommand>($"[click to load]", $"", "load new version\nWill override your current settings")));
                if (AccountSettings?.Value?.AutoUpdateConfig ?? false)
                {
                    updateSpan.Log("auto updating config");
                    MinecraftSocket.Commands["updatecurrentconfig"].Execute(socket, "");
                }
            }
        }
    }

    private async Task SubBaseConfig(ConfigContainer childConfig)
    {
        if (string.IsNullOrWhiteSpace(childConfig.Settings.BasedConfig))
            return;
        var created = await LoadConfigCommand.GetContainer(lifesycle.socket, childConfig.Settings.BasedConfig);
        if (!lifesycle.TryAdopt(ref BaseConfig, created))
            return;
        var baseConfig = created?.Value;
        if (baseConfig == null)
            return;
        var baseAccess = (await OwnConfigsCommand.GetOwnConfigs(lifesycle.socket))
            .FirstOrDefault(config => config.OwnerId == baseConfig.OwnerId
                && config.Name.Equals(
                    baseConfig.Name, StringComparison.OrdinalIgnoreCase));
        if (!BuyConfigCommand.HasManagedUpdates(baseAccess))
        {
            // only clears the field when no overlapping run replaced it meanwhile
            Interlocked.CompareExchange(ref BaseConfig, null, created);
            created.Dispose();
            return;
        }
        Activity.Current.Log($"Baseconfig version {baseConfig.Version} > {lifesycle.AccountSettings?.Value?.BaseConfigVersion}");
        if (baseConfig.Version > lifesycle.AccountSettings.Value.BaseConfigVersion && baseConfig.Version > 0)
        {
            BaseConfigUpdate(childConfig, baseConfig);
        }
        created.OnChange += (newBaseConfig) =>
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
            MinecraftSocket.Commands["loadconfig"].Execute(lifesycle.socket, childConfig.OwnerId + " " + childConfig.Name);
        }
    }
}
