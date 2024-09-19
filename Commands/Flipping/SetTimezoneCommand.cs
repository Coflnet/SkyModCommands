using System;
using System.Threading.Tasks;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC;
[CommandDescription("Set the timezone offset for the current user")]
public class SetTimezoneCommand : ArgumentsCommand
{
    protected override string Usage => "<offset>";
    public override bool IsPublic => true;

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        if (!int.TryParse(args["offset"], out var offset))
            throw new CoflnetException("invalid_arguments", "The offset has to be an integer (hours) eg -6 for CST");
        var now = System.DateTime.UtcNow.AddHours(offset);
        socket.AccountInfo.TimeZoneOffset = offset;
        await socket.sessionLifesycle.AccountInfo.Update();
        string formatted = Format(socket, now);
        socket.SendMessage($"Set Current time: {formatted}");
    }

    private static string Format(IMinecraftSocket socket, DateTime now)
    {
        var locale = socket.AccountInfo.Locale;
        if(string.IsNullOrEmpty(locale))
        {
            locale = "en-US";
        }
        var providerForLocale = System.Globalization.CultureInfo.GetCultureInfo(locale);
        var formatted = now.ToString("g", providerForLocale);
        return formatted;
    }
}
