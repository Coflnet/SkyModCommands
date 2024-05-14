using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class ArgumentsCommandTests
{
    [Test]
    public async Task ParseOptionalSpace()
    {
        var command = new TestCommand();
        command.TestUsage = "<id> [price=0] [text (multi word)]";
        await command.Execute(null, "\"arg1 3 arg2 abc\"");
        var args = command.TestArgs;
        Assert.That(args["id"], Is.EqualTo("arg1"));
        Assert.That(args["text"], Is.EqualTo("arg2 abc"));
        Assert.That(args["price"], Is.EqualTo("3"));
        await command.Execute(null, "\"arg1\"");
        args = command.TestArgs;
        Assert.That(args["id"], Is.EqualTo("arg1"));
        Assert.That(args["text"], Is.EqualTo(""));
        Assert.That(args["price"], Is.EqualTo("0"));
    }

    [Test]
    public async Task Parse4Arguments()
    {
        var command = new TestCommand();
        command.TestUsage = "<id> <text> <number> <bool>";
        await command.Execute(null, "\"arg1 arg2 123 true\"");
        var args = command.TestArgs;
        Assert.That(args["id"], Is.EqualTo("arg1"));
        Assert.That(args["text"], Is.EqualTo("arg2"));
        Assert.That(args["number"], Is.EqualTo("123"));
        Assert.That(args["bool"], Is.EqualTo("true"));
    }

    [Test]
    public async Task ParseOptionalDefaultValue()
    {
        var command = new TestCommand();
        command.TestUsage = "<id> [days={7}]";
        await command.Execute(null, "\"arg1\"");
        var args = command.TestArgs;
        Assert.That(args["id"], Is.EqualTo("arg1"));
        Assert.That(args["days"], Is.EqualTo("7"));
    }

    public class TestCommand : ArgumentsCommand
    {
        public string TestUsage { get; set; }
        public Arguments TestArgs { get; set; }
        protected override string Usage => TestUsage;

        protected override Task Execute(IMinecraftSocket socket, Arguments args)
        {
            TestArgs = args;
            return Task.CompletedTask;
        }

        protected override void SendUsage(IMinecraftSocket socket, string error)
        {
            throw new UsageException(error);
        }
    }

    public class UsageException : Exception
    {
        public UsageException(string message) : base(message)
        {
        }
    }
}
