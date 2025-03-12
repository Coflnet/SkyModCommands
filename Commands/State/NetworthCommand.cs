using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Get a breakdown of networth",
    "Based on current market prices")]
public class NetworthCommand : ArgumentsCommand
{
    public override bool IsPublic => true;

    protected override string Usage => "<username> [profile=used]";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var profile = args["profile"];
        var username = args["username"];
        var profileApi = socket.GetService<IProfileClient>();
        var pricesApi = socket.GetService<IPricesApi>();
        var accountUuid = await socket.GetPlayerUuid(username, false);
        var profileData = await profileApi.GetProfiles(accountUuid);
        var profileId = profileData.FirstOrDefault(p => p.Key.Equals(profile, System.StringComparison.InvariantCultureIgnoreCase)).Key ?? "used";
        if (profileId == "used")
        {
            profileId = (await profileApi.GetActiveProfileId(accountUuid)).Trim('"');
            Console.WriteLine($"Using active profile {profileId}");
            var mappedName = profileData.FirstOrDefault(p => p.Value.Equals(profileId));
            if (mappedName.Key != null)
            {
                profile = mappedName.Key;
            }
        }
        var after = DateTime.UtcNow.AddMinutes(-15);
        var profileInfo = await profileApi.GetProfile(accountUuid, profileId, after);
        var virtualFull = new Api.Client.Model.Profile()
        {
            Members = new() { { accountUuid, profileInfo } },
        };
        var networth = await pricesApi.ApiNetworthPostAsync(virtualFull);
        var top = networth.Member.First().Value.ValuePerCategory.OrderByDescending(m => m.Value).Take(3);
        socket.Dialog(db => db.MsgLine($"Networth of {McColorCodes.AQUA}{username} {McColorCodes.RESET}on {McColorCodes.AQUA}{profile} {McColorCodes.RESET}is {McColorCodes.GOLD}{socket.FormatPrice(networth.FullValue)}", null, "This is currently based on api fields\nincluding chest value is in the works")
            .ForEach(top, (db, m) => db.MsgLine($"{McColorCodes.DARK_GRAY} - {McColorCodes.RESET}{m.Key} {McColorCodes.GOLD}{socket.FormatPrice(m.Value)}")));
        await Task.Delay(10_000); // soft ratelimit
    }
}
