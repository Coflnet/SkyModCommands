using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Prints help for the mod", "Usage: /cofl help [topic]")]
public class HelpCommand : McCommand
{
    public override bool IsPublic => true;
    private Dictionary<string, (string, Action<MinecraftSocket, string>)> Actions = new();
    private Dictionary<string, string> ShortMap = new();
    public HelpCommand()
    {
        Add("login", PrintLoginHelp, "help with login");
        Add("verify", VerifyHelp, "verifying your account");
        Add("commands", PrintCommandHelp, "command list and explanation", "c");

        void Add(string full, Action<MinecraftSocket, string> action, string primer, string shortName = null)
        {
            Actions.Add(full, (primer, action));
            if (shortName == null)
                return;
            Actions.Add(shortName, (primer, action));
            ShortMap.Add(full, shortName);
        }
    }
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        arguments = arguments.Trim('"');

        if (Actions.TryGetValue(arguments.Split(' ').FirstOrDefault(), out var action))
            action.Item2(socket, arguments);
        else
            socket.Dialog(db => db.Break.MsgLine("Please select the topic you need help for:")
                .ForEach(Actions.Where(k => k.Key.Length > 2), (db, a) =>
                    db.MsgLine($" - {McColorCodes.AQUA}{a.Key}{McColorCodes.GRAY} {(ShortMap.TryGetValue(a.Key, out var val) ? $"({val}) " : "")}- {a.Value.Item1}",
                        "/cofl help " + a.Key, "click to get help"))
                .MsgLine($"{McColorCodes.DARK_GRAY} You can also use /cofl help <topic> to get help for a specific topic, or /cl h <topic> for short"));
        return Task.CompletedTask;
    }

    private static void VerifyHelp(MinecraftSocket socket, string arguments)
    {
        socket.Dialog(db => db.MsgLine(McColorCodes.GREEN + "Verifying your account makes sure you are in control of it. That is to check that you are using it in online mode and aren't an impersonator.")
                            .Msg("For this you have to bid a specific amount of coins on any auction on the auction house.")
                            .MsgLine("This allows us to check your bid amount via the API and verify that you have control of the Minecraft account.")
                            .MsgLine("The alternative was to use the Minecraft login system. That would require the mod to create a session login which would get users worried about their session id being stolen.")
                            .MsgLine("Verification is required to make sure that you have ownership of the account. You get 1 free day of premium for verifying your account.")
                            .MsgLine($"You can check your verification status with {McColorCodes.AQUA}/cofl verify"));
    }

    private static void PrintLoginHelp(MinecraftSocket socket, string arguments)
    {
        socket.Dialog(db => db.MsgLine("The mod asks you to login to save and restore your settings.")
                            .MsgLine("The login link connects the Minecraft account you are currently running to whatever email you choose to login with.")
                            .MsgLine("To utilize more features, you may need to verify your Minecraft account")
                            .MsgLine("To logout use the command /cofl logout", "/cofl logout", $"{McColorCodes.GRAY}logs you out on all devices\nclick to logout")
                            .CoflCommand<HelpCommand>(McColorCodes.AQUA + "more about verifying", "verify", "prints more help"));
    }

    private static void PrintCommandHelp(MinecraftSocket socket, string arguments)
    {
        var pageSize = 10;
        if (int.TryParse(arguments.Split(' ').LastOrDefault(), out var page))
            page--;
        var list = MinecraftSocket.Commands.Where(c => c.Value.IsPublic).ToList();
        var withDescription = list.Select(c =>
        {
            var description = c.Value.GetType().GetCustomAttributes(typeof(CommandDescriptionAttribute), true).FirstOrDefault() as CommandDescriptionAttribute;
            return (c.Key, Command: c.Value, description: description?.Description ?? "no help yet");
        }).GroupBy(c => c.Command);
        var allUpdate = Response.Create("commandUpdate", withDescription.ToDictionary(g => g.Key.Slug,
            g => g.Select(i => i.description).First()
        ));
        allUpdate.type = "commandUpdate";
        if (socket.Version == "1.7.6" || socket.Version == "1.7.5")
        {
            var colorPattern = new System.Text.RegularExpressions.Regex(@"§[0-9A-Fa-f]", System.Text.RegularExpressions.RegexOptions.Compiled);
            allUpdate.data = colorPattern.Replace(allUpdate.data, "");
        }
        socket.Send(allUpdate);
        var toDisplay = withDescription.Skip(page * pageSize).Take(pageSize);
        var pageToNavigateTo = page + 2;
        if (pageToNavigateTo > withDescription.Count() / pageSize + 1)
            pageToNavigateTo = 1;
        socket.Dialog(d => d.CoflCommand<HelpCommand>($"AvailableCommands are (page {(page + 1)}/{withDescription.Count() / pageSize + 1}):\n", "c " + pageToNavigateTo, "click to get next page")
            .ForEach(toDisplay, (db, c) =>
                db.If(() => c.Count() == 1,
                    db => FormatLine(c, $" {McColorCodes.AQUA}{c.First().Key}{McColorCodes.GRAY} -", db),
                    db => FormatLine(c, $" {McColorCodes.AQUA}{c.First().Key}{McColorCodes.GRAY} ({c.Last().Key}) -", db)
            )));

        static ModCommands.Dialogs.DialogBuilder FormatLine(IGrouping<McCommand, (string Key, McCommand Command, string description)> c, string startText, ModCommands.Dialogs.DialogBuilder db)
        {
            var descriptionParts = c.First().description.Split('\n');
            return db.Msg(startText, "/cofl " + c.First().Key, "click to execute")
                    .MsgLine($" {descriptionParts.First()}{(descriptionParts.Count() > 1 ? "..." : "")}", "/cofl " + c.First().Key, c.First().description);
        }
    }
}

public class CommandDescriptionAttribute : Attribute
{
    public string Description { get; set; }
    public CommandDescriptionAttribute(params string[] description)
    {
        Description = string.Join('\n', description);
    }
}