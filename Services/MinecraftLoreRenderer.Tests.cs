using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

[TestFixture]
public class MinecraftLoreRendererTests
{
    [Test]
    [Explicit("Local test - renders files to /tmp for visual inspection")]
    public async Task TestRenderLore_WithTestString()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<MinecraftLoreRenderer>();
        var renderer = new MinecraftLoreRenderer(logger);

        var testLore = "§cZorro's Cape\n§bbold text";

        // Act
        var result = await renderer.RenderLoreAsync(testLore);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Length, Is.GreaterThan(0));

        // Save to file for visual inspection
        var outputPath = "/tmp/test_lore_output.png";
        using (var fileStream = File.Create(outputPath))
        {
            result.Position = 0;
            await result.CopyToAsync(fileStream);
        }

        TestContext.WriteLine($"Test image saved to: {outputPath}");

        result.Dispose();
    }

    [Test]
    [Explicit("Local test - renders files to /tmp for visual inspection")]
    public async Task TestRenderLore_WithComplexFormatting()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<MinecraftLoreRenderer>();
        var renderer = new MinecraftLoreRenderer(logger);

        var testLore = "§a§lA Great Sword§r\n" +
                      "§7Damage: §c+10\n" +
                      "§7Speed: §e-2\n\n" +
                      "§o§7A very cool sword.\n" +
                      "§k§l|||§r §d§lEPIC ITEM§r §k§l|||";

        // Act
        var result = await renderer.RenderLoreAsync(testLore);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Length, Is.GreaterThan(0));

        // Save to file for visual inspection
        var outputPath = "/tmp/test_lore_complex.png";
        using (var fileStream = File.Create(outputPath))
        {
            result.Position = 0;
            await result.CopyToAsync(fileStream);
        }

        TestContext.WriteLine($"Complex test image saved to: {outputPath}");

        result.Dispose();
    }

    [Test]
    [Explicit("Local test")]
    public async Task TestRenderLore_EmptyString()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<MinecraftLoreRenderer>();
        var renderer = new MinecraftLoreRenderer(logger);

        // Act
        var result = await renderer.RenderLoreAsync("");

        // Assert
        Assert.That(result, Is.Null);
    }
}
