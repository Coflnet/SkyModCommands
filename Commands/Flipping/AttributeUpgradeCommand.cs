using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Lists the cheapest upgrade path for some attribute", "attributeupgrade <item_name> <attrib2> {start_level} {end_level}")]
public class AttributeUpgradeCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        socket.Dialog(db => db.MsgLine($"The old attributes were removed, you may want to try {McColorCodes.AQUA}/cl cheapattrib {McColorCodes.RESET}instead"));
    }
}
