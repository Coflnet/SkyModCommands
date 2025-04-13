using Coflnet.Sky.Commands.MC;
using System.Collections.Generic;

namespace Coflnet.Sky.ModCommands.Services;

public class ModeratorService
{
    private List<string> MinecraftUuids = new() {
        "aa2a2a01f3c545829358f7db0d799c08", // mrfuzz
        "99ab148c61b146ed9b5d07df7b46984c", // coyu
        "cdb572dfafed43c789ae5c4c009b7019", // matis
        "384a029294fc445e863f2c42fe9709cb", // ekwav
        "b2523d5215874abfa314a7a06c976830", // Hihi735 Fan
        "5ce7bdadba6943a3bfbf91c93ef2bbdf", // Livid
        "cc4fe6e3a3b24e998d36680f85f681b0", // Dylan
        "839271a6a485403492fb96f98ff620c1", // SkilledBear
        "6da36b38ef0149f1bfd00873d7ce5210", // Aistoze     
        "e7246661de77474f94627fabf9880f60", // IcyHenryT  
        "cfc37fbedfab4498893ea7799deedde5", // Flooored
        "dcc434c06bf9463188a1c5ca09c3431d", // Thompie
        "89326ee470ca4b98af8722b540e9db5e", // Trexito
    };
    public bool IsModerator(IMinecraftSocket socket)
    {
        return MinecraftUuids.Contains(socket.SessionInfo.McUuid) && socket.SessionInfo.VerifiedMc;
    }
}

