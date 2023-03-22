using Coflnet.Sky.Commands.MC;
using System.Collections.Generic;

namespace Coflnet.Sky.ModCommands.Services;

public class ModeratorService
{
    List<string> MinecraftUuids = new() {
        "aa2a2a01f3c545829358f7db0d799c08", // mrfuzz
        "99ab148c61b146ed9b5d07df7b46984c", // coyu
        "cdb572dfafed43c789ae5c4c009b7019", // matis
        "384a029294fc445e863f2c42fe9709cb", // ekwav
        "b2523d5215874abfa314a7a06c976830", // Hihi735 Fan
        "c0dafc539b664229aea0695bd9acea2c", // Livid
        "cc4fe6e3a3b24e998d36680f85f681b0", // Dylan
        };
    public bool IsModerator(MinecraftSocket socket)
    {
        return MinecraftUuids.Contains(socket.SessionInfo.McUuid) && socket.SessionInfo.VerifiedMc;
    }
}

