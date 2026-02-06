#nullable enable
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Binds a command/feature to a hotkey", "keybind <key> <command>")]
public class KeyBindCommand : ListCommand<KeyValuePair<string, string>, Dictionary<string, string>>
{
    protected override Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
    {
        var split = val.Split(' ', 2);
        if (split.Length != 2)
        {
            throw new CoflnetException("invalid_args", "Invalid arguments. Usage: keybind <key> <command>\nuse openitemurl as command to open the held item's url");
        }
        if (split[0].Length != 1)
        {
            throw new CoflnetException("invalid_key", "Invalid key. Key must be a single character.");
        }
        var kv = new KeyValuePair<string, string>(split[0], split[1]);
        return Task.FromResult<IEnumerable<CreationOption>>(new[] { new CreationOption() { Element = kv } });
    }

    protected override Task Help(MinecraftSocket socket, string subArgs)
    {
        base.Help(socket, subArgs);
        socket.Dialog(db => db.RemovePrefix().MsgLine($"{McColorCodes.GOLD}Available options for hotkeys add:{McColorCodes.RESET}")
        .MsgLine($"{McColorCodes.AQUA}- openitemurl{McColorCodes.RESET}: Opens the url of the currently held item", "suggest:/cofl keybind add <key> openitemurl", $"Click to bind opening the item url to a key")
        .MsgLine($"{McColorCodes.AQUA}- openitemmarket{McColorCodes.RESET}: Opens the ah/bazaar of currently held item", "suggest:/cofl keybind add <key> openitemmarket", $"Click to bind opening the item market page to a key")
        .MsgLine($"{McColorCodes.AQUA}- craftbreakdown{McColorCodes.RESET}: Shows the crafting breakdown of the currently held item", "suggest:/cofl keybind add <key> craftbreakdown", $"Click to bind showing the crafting breakdown to a key")
        .MsgLine($"{McColorCodes.DARK_GRAY}You can also bind any regular command, just make sure to include the / if it's a command{McColorCodes.RESET}"));
        return Task.CompletedTask;
    }

    protected override string Format(KeyValuePair<string, string> elem)
    {
        return $"{McColorCodes.AQUA}{elem.Key}{McColorCodes.RESET} => {McColorCodes.YELLOW}{elem.Value}{McColorCodes.RESET}";
    }

    protected override string GetId(KeyValuePair<string, string> elem)
    {
        return elem.Key;
    }

    protected override Task<Dictionary<string, string>> GetList(MinecraftSocket socket)
    {
        return Task.FromResult(socket.Settings.ModSettings?.Hotkeys ?? new Dictionary<string, string>());
    }

    protected override async Task Update(MinecraftSocket socket, Dictionary<string, string> newCol)
    {
        socket.Settings.ModSettings.Hotkeys = newCol;
        var converted = newCol.Select(kv => new KeybindRegister() { Name = kv.Value.StartsWith("/") ? kv.Key : kv.Value, DefaultKey = kv.Key }).ToArray();
        socket.Send(Response.Create("registerKeybind", converted));
        await socket.sessionLifesycle.FlipSettings.Update();
    }
}
