using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Core.Tracing;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class NECCommand : ArgumentsCommand
{
    protected override string Usage => "<email>";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var email = args["email"].ToLower();
        if (email.Length == 0)
        {
            socket.SendMessage($"Usage: {McColorCodes.AQUA}/cofl nec <email>{McColorCodes.GRAY}. The email is the one you used to sign up for NEC.");
            return;
        }
        var necUser = await socket.GetService<NecImportService>().GetUser(socket.SessionInfo.McUuid);
        Activity.Current.Log(JsonConvert.SerializeObject(necUser));
        var count = 2;
        if (necUser == null || necUser.Email?.ToLower() != email && necUser.Email != null)
        {
            using var context = new HypixelContext();
            var userId = int.Parse(socket.UserId);
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == email && userId == u.Id);
            if (user == null)
            {
                socket.SendMessage($"No user with email {email} and ign {socket.SessionInfo.McName} found in the NEC database. Please check your email or ask support for help.");
                await Task.Delay(3000);
                return;
            }
            necUser = await socket.GetService<NecImportService>().GetUserByEmail(email);
            if (necUser == null)
            {
                socket.SendMessage($"No user with email {email} and ign {socket.SessionInfo.McName} found in the NEC database. Please check your email or ask support for help.");
                await Task.Delay(3000);
                return;
            }
            else if (!string.IsNullOrEmpty(necUser.ClaimedOnAccount))
            {
                socket.SendMessage($"Since you didn't claim the nec welcome bonus on the correct account its halfed");
                count = 1;
            }
            Activity.Current.Log(JsonConvert.SerializeObject(necUser));
        }
        if (!string.IsNullOrEmpty(necUser.ClaimedOnAccount))
        {
            socket.SendMessage($"This account has already claimed the cofl nec tier at {McColorCodes.AQUA}{necUser.ClaimedAt}\n{McColorCodes.GRAY}It can only be claimed once.");
            return;
        }
        socket.Dialog(db => db.MsgLine("Welcome to SkyCofl!\nWe are unlocking the basic tier for you, please give us a second"));
        try
        {
            if (DateTime.UtcNow > new DateTime(2025, 8, 1))
            {
                count = 1;
            }
            else if (DateTime.UtcNow > new DateTime(2026, 2, 1))
            {
                throw new CoflnetException("to_late", "NEC migrating is no longer available, The offer expired on 2026-02-01");
            }
            var topup = socket.GetService<TopUpApi>();
            await topup.TopUpCustomPostAsync(socket.UserId, new()
            {
                ProductId = "compensation",
                Amount = 1800 * count,
                Reference = "nec-" + necUser.Uuid
            });
            var userApi = socket.GetService<UserApi>();
            await userApi.UserUserIdServicePurchaseProductSlugPostAsync(socket.UserId, "starter_premium", "nec-" + necUser.Uuid, count);
        }
        catch (Exception e)
        {
            socket.GetService<ILogger<NECCommand>>().LogError(e, "Error while unlocking NEC tier");
            socket.Dialog(db => db.MsgLine("An error occurred while unlocking the NEC tier. Please contact support."));
            throw;
        }
        necUser.ClaimedOnAccount = socket.UserId;
        necUser.ClaimedAt = DateTime.UtcNow;
        await socket.GetService<NecImportService>().AddUser(necUser);
        socket.Dialog(db => db.MsgLine($"{McColorCodes.GOLD}Alright, every set!")
            .MsgLine("You now have access to the NEC tier both in game with the mod and on the website.")
            .MsgLine("Enjoy the extended features and have a nice day!")
            .MsgLine($"{McColorCodes.GRAY}If you encounter an issue or have suggestions please let us know on discord."));

        await Task.Delay(5000);
        await socket.sessionLifesycle.TierManager.RefreshTier();
    }
}


public class NecImportService
{
    Table<NecUser> necUsers;
    private ILogger<NecImportService> logger;

    public NecImportService(ISession session, ILogger<NecImportService> logger)
    {
        necUsers = new Table<NecUser>(session, new MappingConfiguration().Define(
            new Map<NecUser>()
                .PartitionKey(u => u.Uuid)
        ));
        necUsers.CreateIfNotExists();
        this.logger = logger;
    }

    public async Task AddUser(NecUser user)
    {
        await necUsers.Insert(user).ExecuteAsync();
        logger.LogInformation($"Added user {user.Uuid}");
    }

    public async Task<NecUser> GetUser(string uuid)
    {
        return await necUsers.FirstOrDefault(u => u.Uuid == uuid).ExecuteAsync();
    }

    internal async Task<NecUser> GetUserByEmail(string email)
    {
        var all = await necUsers.Where(u => u.Email == email).AllowFiltering().ExecuteAsync();
        return all.FirstOrDefault();
    }

    public class NecUser
    {
        public string Email;
        public string Uuid;
        public string Key;
        public string ClaimedOnAccount;
        public DateTime ClaimedAt;
    }
}