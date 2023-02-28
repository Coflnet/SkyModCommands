using Coflnet.Sky.Commands.MC;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.ModCommands.Services;

public class ModeratorService
{
    List<string> MinecraftUuids = new() {
        "aa2a2a01f3c545829358f7db0d799c08", // mrfuzz
        "99ab148c61b146ed9b5d07df7b46984c", // coyu
        "cdb572dfafed43c789ae5c4c009b7019", // matis
        "384a029294fc445e863f2c42fe9709cb", // ekwav
        //"b2523d5215874abfa314a7a06c976830", // Hihi735 Fan (breached)
        "c0dafc539b664229aea0695bd9acea2c", // Livid
        "34e8ac9671194cc594f0cf68b9c3966c", // Diamond
        };
    public bool IsModerator(MinecraftSocket socket)
    {
        return socket.AccountInfo.McIds.Any(id => MinecraftUuids.Contains(id))
        || MinecraftUuids.Contains(socket.SessionInfo.McUuid) && socket.SessionInfo.VerifiedMc;
    }
}

