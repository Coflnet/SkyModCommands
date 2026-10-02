using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json.Linq;

namespace Coflnet.Sky.Commands.MC;

public class GetMcNameForCommand : McCommand
{
    private static readonly Regex UuidFormat = new("^[0-9a-fA-F]{32}$", RegexOptions.Compiled);

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        string name = await GetName(socket, arguments);
        socket.Dialog(db => db.Msg("The minecraft name is " + McColorCodes.AQUA + name, "copy:" + name, "Click to copy the name to your clipboard").MsgLine(" _", "suggest: " + name, "Click to suggest the name in chat"));
    }

    public static async Task<string> GetName(MinecraftSocket socket, string arguments)
    {
        var uuid = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(arguments);
        var name = await socket.GetPlayerName(uuid);
        // the stored name can be outdated if the player renamed, double check with mojang
        var currentName = await GetCurrentName(socket, uuid);
        if (currentName != null && !string.Equals(currentName, name, StringComparison.OrdinalIgnoreCase))
        {
            _ = socket.TryAsyncTimes(() => IndexerClient.TriggerNameUpdate(uuid), "updating outdated player name", 1);
            name = currentName;
        }
        if (name == null)
            throw new Core.CoflnetException("name_not_found", "Could not retrieve the account name :(");
        return name;
    }

    private static async Task<string> GetCurrentName(MinecraftSocket socket, string uuid)
    {
        uuid = uuid?.Replace("-", "");
        if (uuid == null || !UuidFormat.IsMatch(uuid))
            return null;
        try
        {
            var client = socket.GetService<IHttpClientFactory>().CreateClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync($"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}", timeout.Token);
            if (!response.IsSuccessStatusCode)
                return null;
            return (string)JObject.Parse(await response.Content.ReadAsStringAsync())["name"];
        }
        catch (Exception)
        {
            // mojang being unavailable should not break the lookup, the stored name is still a good guess
            return null;
        }
    }
}
