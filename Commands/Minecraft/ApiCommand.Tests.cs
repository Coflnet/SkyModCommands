using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class ApiCommandTests
{
    [TestCase("2.0.0-pre1")]
    [TestCase("1.7.3")]
    public void GenerateAcceptsCurrentVersionWithSuffix(string version)
    {
        var socket = CreateSocket(version);

        Assert.DoesNotThrowAsync(() => new ApiCommand().Execute(socket, "\"generate\""));

        // only reached after the version check passed, no profile id is known in this session
        Assert.That(socket.DialogMessages, Has.Some.Contains("requires chat collection"));
    }

    private static CaptureDialogSocket CreateSocket(string version)
    {
        var socket = new CaptureDialogSocket();
        typeof(MinecraftSocket).GetProperty(nameof(MinecraftSocket.Version)).SetValue(socket, version);
        socket.SessionInfo.McUuid = "11112222333344445555666677778888";
        socket.SetLifecycle(new ModSessionLifesycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate("5"),
            PrivacySettings = SelfUpdatingValue<PrivacySettings>.CreateNoUpdate(new PrivacySettings())
        });
        return socket;
    }

    private class CaptureDialogSocket : MinecraftSocket
    {
        // the constructor needs a live cassandra session, the service is not reached without a profile id
        private readonly ApiKeyService apiKeyService = (ApiKeyService)RuntimeHelpers.GetUninitializedObject(typeof(ApiKeyService));
        public List<string> DialogMessages { get; } = new();

        public void SetLifecycle(ModSessionLifesycle lifecycle)
        {
            sessionLifesycle = lifecycle;
        }

        public override T GetService<T>()
        {
            return apiKeyService as T ?? Mock.Of<T>();
        }

        public override void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation)
        {
            var dialog = creation(new SocketDialogBuilder(this)).Build();
            DialogMessages.AddRange(dialog.Select(part => part.text));
        }
    }
}
