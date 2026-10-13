using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class BzOrderCommandTests
{
    [TestCase("\"32 Enchanted Diamond\"")]
    [TestCase("\"search 32 Enchanted Diamond\"")]
    public async Task ExecutesBzSearch(string args)
    {
        var socket = new CaptureSocket();
        await new BzOrderCommand().Execute(socket, args);
        Assert.That(socket.Executed, Is.EqualTo(new[] { "/bz Enchanted Diamond" }));
        Assert.That(socket.Dialogs, Is.Empty);
    }

    [Test]
    public async Task MissingAmountShowsUsage()
    {
        var socket = new CaptureSocket();
        await new BzOrderCommand().Execute(socket, "\"Enchanted\"");
        Assert.That(socket.Executed, Is.Empty);
        Assert.That(socket.Dialogs, Has.Some.Contains("Usage: /cofl bzorder [search] <amount> <item>"));
    }

    private class CaptureSocket : MinecraftSocket
    {
        public List<string> Executed { get; } = new();
        public List<string> Dialogs { get; } = new();

        public override void ExecuteCommand(string command) => Executed.Add(command);

        public override void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation)
        {
            Dialogs.AddRange(creation(new SocketDialogBuilder(this)).Build().Select(p => p.text));
        }
    }
}
