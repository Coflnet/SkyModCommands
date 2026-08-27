using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cassandra;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Coflnet.Sky.Commands.MC;

public class ApiCommandTests
{
    [Test]
    public void User_key_lookup_uses_the_secondary_index_without_allowing_filtering()
    {
        var statements = new List<string>();
        using var cluster = Cluster.Builder().AddContactPoint("127.0.0.1").Build();
        var session = CreateSession(cluster);
        session.Setup(value => value.PrepareAsync(It.IsAny<string>()))
            .Callback<string>(statement => statements.Add(statement))
            .ThrowsAsync(new InvalidOperationException("Stop after capturing the attempted operation."));
        var service = new ApiKeyService(session.Object, NullLogger<ApiKeyService>.Instance);

        Assert.ThrowsAsync<InvalidOperationException>(() => service.GetUserApiKeys("test-user"));

        Assert.That(statements, Has.Count.EqualTo(1));
        Assert.That(statements[0], Does.EndWith(" WHERE user_id = ?"));
    }

    [Test]
    public void Existing_table_still_ensures_user_id_index()
    {
        var statements = new List<string>();
        using var cluster = Cluster.Builder().AddContactPoint("127.0.0.1").Build();
        var session = CreateSessionWithExistingTable(cluster, statements);

        _ = new ApiKeyService(session.Object, NullLogger<ApiKeyService>.Instance);

        Assert.That(statements, Does.Contain("CREATE INDEX IF NOT EXISTS ON api_keys (user_id)"));
    }

    [Test]
    public async Task Empty_command_creates_a_key_when_none_exists()
    {
        var statements = new List<string>();
        using var cluster = Cluster.Builder().AddContactPoint("127.0.0.1").Build();
        var session = CreateSessionWithExistingTable(cluster, statements);
        ConfigureApiKeyOperations(session, statements);
        var service = new ApiKeyService(session.Object, NullLogger<ApiKeyService>.Instance);

        await new ApiCommand().Execute(new TestApiSocket(service), "\"\"");

        Assert.That(statements, Has.Some.StartsWith("INSERT INTO api_keys"),
            $"Executed statements: {string.Join(" | ", statements)}");
    }

    [Test]
    public void Api_key_service_methods_are_not_virtual()
    {
        Assert.That(typeof(ApiKeyService).GetMethod(nameof(ApiKeyService.GenerateApiKey))!.IsVirtual, Is.False);
        Assert.That(typeof(ApiKeyService).GetMethod(nameof(ApiKeyService.GetUserApiKeys))!.IsVirtual, Is.False);
    }

    private static Mock<ISession> CreateSession(ICluster cluster)
    {
        var session = new Mock<ISession>();
        session.SetupGet(value => value.Cluster).Returns(cluster);
        session.SetupGet(value => value.Keyspace).Returns(string.Empty);
        session.Setup(value => value.Execute(It.IsAny<string>())).Returns((RowSet)null);
        return session;
    }

    private static Mock<ISession> CreateSessionWithExistingTable(ICluster cluster, List<string> statements)
    {
        var session = CreateSession(cluster);
        session.SetupGet(value => value.Keyspace).Returns("test_keyspace");

        var prepared = new Mock<PreparedStatement>();
        prepared.Setup(value => value.Bind(It.IsAny<object[]>())).Returns(new BoundStatement());
        session.Setup(value => value.Prepare(It.IsAny<string>())).Returns(prepared.Object);

        var existingTable = new Mock<RowSet>();
        existingTable.Setup(value => value.GetEnumerator())
            .Returns(new[] { new Mock<Row>().Object }.AsEnumerable().GetEnumerator());
        session.Setup(value => value.Execute(It.IsAny<IStatement>())).Returns(existingTable.Object);
        session.Setup(value => value.Execute(It.IsAny<string>()))
            .Callback<string>(statement => statements.Add(statement))
            .Returns((RowSet)null);
        return session;
    }

    private static void ConfigureApiKeyOperations(Mock<ISession> session, List<string> statements)
    {
        var prepared = new Mock<PreparedStatement>();
        prepared.Setup(value => value.Bind(It.IsAny<object[]>())).Returns(new BoundStatement());
        session.Setup(value => value.PrepareAsync(It.IsAny<string>()))
            .Callback<string>(statement =>
            {
                if (statement.StartsWith("SELECT") &&
                    !statements.Contains("CREATE INDEX IF NOT EXISTS ON api_keys (user_id)"))
                    throw new InvalidOperationException("The user_id index does not exist.");
                statements.Add(statement);
            })
            .ReturnsAsync(prepared.Object);

        var emptyResult = new Mock<RowSet>();
        emptyResult.SetupGet(value => value.Info).Returns(new Mock<ExecutionInfo>().Object);
        emptyResult.Setup(value => value.GetEnumerator())
            .Returns(Enumerable.Empty<Row>().GetEnumerator());
        session.Setup(value => value.ExecuteAsync(It.IsAny<IStatement>(), It.IsAny<string>()))
            .ReturnsAsync(emptyResult.Object);
    }

    private sealed class TestApiSocket : MinecraftSocket
    {
        private readonly ApiKeyService apiKeyService;

        public TestApiSocket(ApiKeyService apiKeyService)
        {
            this.apiKeyService = apiKeyService;
            SessionInfo.McUuid = "00000000000000000000000000000000";
            SessionInfo.ProfileId = "00000000000000000000000000000000";
            SessionInfo.McName = "test-player";
            typeof(MinecraftSocket).GetProperty(nameof(Version))!.SetValue(this, "1.7.3");

            sessionLifesycle = (ModSessionLifesycle)RuntimeHelpers.GetUninitializedObject(
                typeof(ModSessionLifesycle));
            sessionLifesycle.UserId = SelfUpdatingValue<string>.CreateNoUpdate("test-user");

            InitializeWebSocket();
        }

        private void InitializeWebSocket()
        {
            var webSocket = new WebSocket("ws://localhost");
            SetPrivateField(typeof(WebSocket), webSocket, "_readyState", WebSocketState.Open);
            SetPrivateField(typeof(WebSocket), webSocket, "_stream", new MemoryStream());
            SetPrivateField(typeof(WebSocketBehavior), this, "_websocket", webSocket);
        }

        private static void SetPrivateField(Type declaringType, object instance, string name, object value) =>
            declaringType.GetField(name, System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.SetValue(instance, value);

        public override T GetService<T>() where T : class =>
            apiKeyService as T ?? base.GetService<T>();

        public override bool SendMessage(params ChatPart[] parts) => true;
    }
}
