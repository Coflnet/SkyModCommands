using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Regression coverage for the player id TaskCommand/TaskDetailsCommand send to SkyPlayerState's
/// task endpoints. SkyPlayerState's player state (skills, HOTM, purse, claimed task) is keyed by
/// Minecraft NAME, not uuid (see SkyPlayerState.Services.Tasks.TaskExecutionService); these two
/// commands used to send socket.SessionInfo.McUuid, which silently returned results built from an
/// empty state instead of throwing, so the bug only showed up as "wrong/missing numbers" in game.
/// </summary>
public class TaskCommandSessionIdTests
{
    private const string PlayerName = "Ekwav";
    private const string PlayerUuid = "11112222333344445555666677778888";

    private sealed class TestSocket : MinecraftSocket
    {
        private readonly Dictionary<Type, object> services = new();
        public override bool IsClosed => false;
        public void AddService<T>(T service) where T : class => services[typeof(T)] = service;
        public override T GetService<T>() => services.TryGetValue(typeof(T), out var service) ? (T)service : Mock.Of<T>();
        // Rendering the actual dialog needs no infra for this test - only which id was requested does.
        public override void Dialog(Func<ModCommands.Dialogs.SocketDialogBuilder, ModCommands.Dialogs.DialogBuilder> creation) { }
    }

    private static (TestSocket socket, Mock<ITaskApi> taskApi) MakeSocket()
    {
        var socket = new TestSocket();
        socket.SessionInfo.McName = PlayerName;
        socket.SessionInfo.McUuid = PlayerUuid;
        var taskApi = new Mock<ITaskApi>();
        socket.AddService(new TaskService(taskApi.Object, NullLogger<TaskService>.Instance));
        return (socket, taskApi);
    }

    [Test]
    public async Task TaskCommand_GetElements_RequestsResultsByName_NotUuid()
    {
        var (socket, taskApi) = MakeSocket();
        taskApi.Setup(a => a.TaskPlayerIdResultsGetAsync(PlayerName, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskResult> { new() { Name = "Forge", ProfitPerHour = 1000 } });

        var getElements = typeof(TaskCommand).GetMethod("GetElements", BindingFlags.NonPublic | BindingFlags.Instance);
        var results = await (Task<IEnumerable<TaskResult>>)getElements.Invoke(new TaskCommand(), new object[] { socket, "" });

        results.Should().ContainSingle();
        taskApi.Verify(a => a.TaskPlayerIdResultsGetAsync(PlayerName, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        taskApi.Verify(a => a.TaskPlayerIdResultsGetAsync(PlayerUuid, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never,
            "player state is keyed by name, not uuid - sending the uuid used to silently return an empty state");
    }

    [Test]
    public async Task TaskDetailsCommand_Execute_RequestsResultByName_NotUuid()
    {
        var (socket, taskApi) = MakeSocket();
        taskApi.Setup(a => a.TaskPlayerIdResultsTaskNameGetAsync(PlayerName, "Forge", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskResult { Name = "Forge", ProfitPerHour = 1000 });

        await new TaskDetailsCommand().Execute(socket, "\"Forge\"");

        taskApi.Verify(a => a.TaskPlayerIdResultsTaskNameGetAsync(PlayerName, "Forge", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        taskApi.Verify(a => a.TaskPlayerIdResultsTaskNameGetAsync(PlayerUuid, "Forge", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never,
            "player state is keyed by name, not uuid - sending the uuid used to silently return an empty state");
    }
}
