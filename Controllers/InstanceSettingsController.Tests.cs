using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Controllers;

public class InstanceSettingsControllerTests
{
    [Test]
    public async Task UnknownIdReturnsNoContent()
    {
        var service = new Mock<IInstanceSettingsService>();
        var controller = Create(service);

        var result = await controller.GetDescription("a/b+c=");

        Assert.That(result, Is.TypeOf<NoContentResult>());
        service.Verify(s => s.GetDescriptionStrict("a/b+c="), Times.Once);
    }

    [Test]
    public async Task ReturnsOnlyTheDescriptionJson()
    {
        var description = new DescriptionSetting { DisableInfoIn = new HashSet<string> { "bazaar" } };
        var service = new Mock<IInstanceSettingsService>();
        service.Setup(s => s.GetDescriptionStrict("id")).ReturnsAsync(description);

        var result = await Create(service).GetDescription("id") as ContentResult;

        Assert.That(result.ContentType, Is.EqualTo("application/json"));
        Assert.That(JsonConvert.DeserializeObject<DescriptionSetting>(result.Content).DisableInfoIn, Is.EquivalentTo(new[] { "bazaar" }));
        service.Verify(s => s.Get(It.IsAny<string>()), Times.Never, "flip settings are never exposed");
    }

    [Test]
    public async Task FailedReadReturnsServiceUnavailable()
    {
        var service = new Mock<IInstanceSettingsService>();
        service.Setup(s => s.GetDescriptionStrict("id")).ThrowsAsync(new System.InvalidOperationException("db down"));

        var result = await Create(service).GetDescription("id") as StatusCodeResult;

        Assert.That(result?.StatusCode, Is.EqualTo(503), "a failure must not look like 'kept no settings'");
    }

    private static InstanceSettingsController Create(Mock<IInstanceSettingsService> service)
        => new(service.Object, NullLogger<InstanceSettingsController>.Instance);
}
