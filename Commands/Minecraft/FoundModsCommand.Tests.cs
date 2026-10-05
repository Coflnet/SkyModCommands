using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class FoundModsCommandTests
{
    [TestCase("Firmament-3.9.0.jar", true)]
    [TestCase("sodium-fabric-0.6.jar", false)]
    public async Task AuctionStartedTimeIsDisabledOnlyForModsInsertingIntoAuctionLore(string modFile, bool expectDisabled)
    {
        const string userId = "42";
        string stored = null;
        var settingsApi = new Mock<ISettingsApi>();
        settingsApi.Setup(api => api.GetSettingWithHttpInfoAsync(userId, "description", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(HttpStatusCode.OK, JsonConvert.SerializeObject(DescriptionSetting.Default)));
        settingsApi.Setup(api => api.UpdateSettingAsync(userId, "description", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, string body, int _, CancellationToken _) => stored = body);
        var socket = new ModsSocket(new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, settingsApi.Object));
        socket.SetLifecycle(new ModSessionLifesycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate(userId),
            AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { UserId = userId })
        });

        await new FoundModsCommand().Execute(socket, JsonConvert.SerializeObject(new FoundModsCommand.Response { FileNames = [modFile] }));

        if (!expectDisabled)
        {
            Assert.That(stored, Is.Null, "Lore settings of users without incompatible mods must stay untouched.");
            return;
        }
        Assert.That(stored, Is.Not.Null, "The started time has to be disabled when an incompatible mod is uploaded.");
        var saved = JsonConvert.DeserializeObject<DescriptionSetting>(JsonConvert.DeserializeObject<string>(stored));
        Assert.That(saved.DisableAuctionStartedTime, Is.True);
    }

    private sealed class ModsSocket(SettingsService settings) : MinecraftSocket
    {
        public void SetLifecycle(ModSessionLifesycle lifecycle) => sessionLifesycle = lifecycle;

        public override T GetService<T>() => typeof(T) == typeof(SettingsService)
            ? (T)(object)settings
            : Mock.Of<T>();

        public override void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation)
        {
        }
    }
}
