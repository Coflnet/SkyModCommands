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
        return Task.FromResult<IEnumerable<CreationOption>>([new CreationOption() { Element = new(split[0], split[1]) }]);
    }

    protected override string Format(KeyValuePair<string, string> elem)
    {
        return $"{McColorCodes.AQUA}{elem.Key}{McColorCodes.RESET} => {McColorCodes.YELLOW}{elem.Value}";
    }

    protected override string GetId(KeyValuePair<string, string> elem)
    {
        return elem.Key;
    }

    protected override Task<Dictionary<string, string>> GetList(MinecraftSocket socket)
    {
        return Task.FromResult(socket.Settings.ModSettings?.Hotkeys ?? []);
    }

    protected override async Task Update(MinecraftSocket socket, Dictionary<string, string> newCol)
    {
        socket.Settings.ModSettings.Hotkeys = newCol;
        var converted = newCol.Select(kv => new KeybindRegister() { Name = kv.Value.StartsWith("/") ? kv.Key : kv.Value, DefaultKey = kv.Key }).ToArray();
        socket.Send(Response.Create("registerKeybind", converted));
        await socket.sessionLifesycle.FlipSettings.Update();
    }
}
