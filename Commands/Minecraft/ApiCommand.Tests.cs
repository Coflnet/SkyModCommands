using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cassandra;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

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
        RequireMockableService();
        using var cluster = Cluster.Builder().AddContactPoint("127.0.0.1").Build();
        var session = CreateSession(cluster);
        var service = CreateService(session);
        service.Setup(value => value.GetUserApiKeys("test-user"))
            .ReturnsAsync(Array.Empty<ApiKey>());
        service.Setup(value => value.GenerateApiKey(
                "test-user",
                "00000000000000000000000000000000",
                "00000000000000000000000000000000",
                "test-player"))
            .ReturnsAsync("new-key");

        await new ApiCommand().Execute(new TestApiSocket(service.Object), "\"\"");

        service.Verify(value => value.GenerateApiKey(
            "test-user",
            "00000000000000000000000000000000",
            "00000000000000000000000000000000",
            "test-player"), Times.Once);
    }

    [Test]
    public async Task Empty_command_does_not_create_another_key_when_an_active_key_exists()
    {
        RequireMockableService();
        using var cluster = Cluster.Builder().AddContactPoint("127.0.0.1").Build();
        var session = CreateSession(cluster);
        var service = CreateService(session);
        service.Setup(value => value.GetUserApiKeys("test-user"))
            .ReturnsAsync(new[]
            {
                new ApiKey
                {
                    Key = "existing-key",
                    UserId = "test-user",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                }
            });

        await new ApiCommand().Execute(new TestApiSocket(service.Object), "\"\"");

        service.Verify(value => value.GetUserApiKeys("test-user"), Times.Exactly(2));
        session.Verify(value => value.PrepareAsync(It.IsAny<string>()), Times.Never);
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

    private static Mock<ApiKeyService> CreateService(Mock<ISession> session) =>
        new(session.Object, NullLogger<ApiKeyService>.Instance) { CallBase = true };

    private static void RequireMockableService()
    {
        if (!typeof(ApiKeyService).GetMethod(nameof(ApiKeyService.GetUserApiKeys))!.IsVirtual)
            Assert.Ignore("Command behavior tests require the service lookup to be mockable.");
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
        }

        public override T GetService<T>() where T : class =>
            apiKeyService as T ?? base.GetService<T>();

        public override bool SendMessage(params ChatPart[] parts) => true;
    }
}
