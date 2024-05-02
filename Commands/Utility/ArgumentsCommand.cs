using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public abstract class ArgumentsCommand : McCommand
{
    /// <summary>
    /// square brackets [optional option]
    /// angle brackets &lt;required argument>
    /// curly braces {default values}
    /// parenthesis (miscellaneous info)
    /// </summary>
    protected abstract string Usage { get; }
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var error = TryParseArguments(arguments, out var parsed);
        if (error != null)
        {
            SendUsage(socket, error);
            return;
        }
        await Execute(socket, parsed);
    }

    private string TryParseArguments(string arguments, out Arguments parsed)
    {
        var multiWords = Regex.Match(Usage, @"([\w]+) \(multi word\)");
        var argOrder = Usage;
        if (multiWords.Success)
        {
            argOrder = argOrder.Replace(" (multi word)", "");
        }
        var defaultValues = Regex.Matches(argOrder, @"([\w]+)=\{?(\w+)\}?");
        var parts = argOrder.Trim('"').Split(' ').Select(p => p.Split('=').First().Trim('<', '>', '[', ']')).ToArray();
        parsed = new Arguments();
        var argParts = JsonConvert.DeserializeObject<string>(arguments).Split(' ');
        if (argParts.Length != parts.Length && !multiWords.Success && (!defaultValues.FirstOrDefault()?.Success ?? false))
        {
            return "The amount of arguments doesn't match";
        }
        var defaultValueLookup = defaultValues.Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToDictionary(v => v.Item1, v => v.Item2);
        for (int i = 0; i < parts.Length; i++)
        {
            if (argParts.Length <= i)
            {
                if (defaultValueLookup.TryGetValue(parts[i], out var defaultValue))
                {
                    parsed[parts[i]] = defaultValue;
                    continue;
                }
                return "The amount of arguments doesn't match";
            }
            parsed[parts[i]] = argParts[i];
        }
        if (multiWords.Success)
        {
            parsed[multiWords.Groups[1].Value] = string.Join(" ", argParts.Skip(parts.Length - 1));
        }
        return null;
    }

    protected virtual void SendUsage(IMinecraftSocket socket, string error)
    {
        socket.SendMessage(error);
        socket.SendMessage($"Usage: {McColorCodes.AQUA}/cl {Slug} {Usage}");
    }

    protected abstract Task Execute(IMinecraftSocket socket, Arguments args);

    public class Arguments : Dictionary<string, string>
    {
        public new string this[string key]
        {
            get => TryGetValue(key, out var value) ? value : null;
            set => base[key] = value;
        }
    }
}
