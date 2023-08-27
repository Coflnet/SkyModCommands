using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public static class Extensions
{
    public static bool IsWhitelisted(this FlipInstance flip)
    {
        return flip.Context.TryGetValue("match", out var type) && type.StartsWith("whitelist");
    }
}