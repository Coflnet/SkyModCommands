using System;
using System.Linq;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json.Linq;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>Applies messages SkyApi pushes over <see cref="ModUserChannel"/> to a live mod session.</summary>
public static class ModUserMessageHandler
{
    public const int MaxCommandLength = 120;

    /// <summary>Returns false when the subscription should be dropped (socket closed).</summary>
    internal static bool HandleUserMessage(IMinecraftSocket socket, ModUserChannel.UserMessage msg)
    {
        if (socket.IsClosed)
            return false;
        try
        {
            if (!string.IsNullOrEmpty(msg.MinecraftUuid)
                && !string.Equals(msg.MinecraftUuid, socket.SessionInfo?.McUuid, StringComparison.OrdinalIgnoreCase))
                return true;
            switch (msg.Kind)
            {
                case ModUserChannel.ExecuteKind:
                    if (!string.IsNullOrWhiteSpace(msg.Command) && msg.Command.Length <= MaxCommandLength)
                        socket.ExecuteCommand(msg.Command);
                    break;
                case ModUserChannel.DisplayKind:
                    if (!BazaarOrderDisplay.Supports(socket) || msg.Display == null)
                        break;
                    socket.Send(Response.Create("infoDisplay", msg.Display.ToObject<InfoDisplay>()));
                    break;
                case ModUserChannel.ExpectKind:
                    if (!string.IsNullOrWhiteSpace(msg.Command))
                        socket.Send(Response.Create("expectCommand", new { command = msg.Command, ttl = msg.Ttl ?? 60 }));
                    break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"failed to handle user message {msg?.Kind}: {e}");
        }
        return true;
    }
}
