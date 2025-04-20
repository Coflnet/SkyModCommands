using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.ModCommands.Services.Vps;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class VpsCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        await socket.ReguirePremPlus();
        var service = socket.GetService<VpsInstanceManager>();
        var args = Convert<string>(arguments).Split(' ');
        switch (args[0].ToLower())
        {
            case "create":
                await Create(socket, service, args);
                return;
            case "turnoff":
                await TurnOff(socket, service, args);
                return;
            case "turnon":
                await TurnOn(socket, service, args);
                return;
            case "log":
                await GetLog(socket, service, args);
                return;
            case "reassign":
                await Reassign(socket, service, args);
                return;
            case "extend":
                await Extend(socket, service, args);
                return;
            case "set":
                await UpdateSettings(socket, service, args);
                return;
            case "delete":
                await Delete(socket, service, args);
                return;
        }
        var instances = await service.GetVpsForUser(socket.UserId);
        foreach (var i in instances)
        {
            socket.Dialog(db => db.Msg($"- {i.Id.ToString().TakeLast(3).Aggregate("", (s, c) => s + c)} {i.AppKind} {i.PaidUntil}").CoflCommandButton<VpsCommand>("Extend", $"extend {i.Id}", "extend the server"));
        }
        if (instances.Count == 0)
        {
            socket.Dialog(db => db.MsgLine($"You don't have any instances so far, use {McColorCodes.AQUA}/cofl vps create tpm+{McColorCodes.RESET} to create one"));
        }
        else
        {
            socket.Dialog(db => db.MsgLine($"Usage: {McColorCodes.AQUA}/cofl vps <turnOn|turnOff|log|reassign|extend>")
                .MsgLine($"To update settings use {McColorCodes.AQUA}/cofl vps set <vpsId> <key> <value>", null, "Separate settings with multiple options with comma (,)"));
        }
    }

    private async Task Delete(MinecraftSocket socket, VpsInstanceManager service, string[] args)
    {
        var instance = await GetTargetVps(socket, service, args);
        await service.DeleteVps(instance);
        socket.Dialog(db => db.MsgLine($"Deleted {instance.Id}"));
    }

    private async Task Extend(MinecraftSocket socket, VpsInstanceManager service, string[] args)
    {
        var instance = await GetTargetVps(socket, service, args);
        instance.PaidUntil = DateTime.UtcNow.AddDays(1);
        await service.UpdateInstance(instance);
    }

    private async Task Reassign(MinecraftSocket socket, VpsInstanceManager service, string[] args)
    {
        var instance = await GetTargetVps(socket, service, args);
        await service.ReassignVps(instance);
        socket.Dialog(db => db.MsgLine($"Reassigned to host {instance.HostMachineIp.Split('.').Last()}"));
    }

    private async Task GetLog(MinecraftSocket socket, VpsInstanceManager service, string[] args)
    {
        Instance instance = await GetTargetVps(socket, service, args);
        if (args.Last() == "follow" || args.Last() == "f")
        {
            var myStreamid = Guid.NewGuid().ToString();
            socket.Dialog(db => db.MsgLine($"As long as this connection is open it will print VPS logs for {instance.Id} ").CoflCommandButton<VpsCommand>("Stop following", $"log {instance.Id}", "stop following"));
            socket.SessionInfo.ActiveStream = myStreamid;
            var start = DateTimeOffset.UtcNow.AddHours(-24);
            var end = DateTimeOffset.UtcNow;
            while (!socket.IsClosed && myStreamid == socket.SessionInfo.ActiveStream)
            {
                var followLog = (await service.GetVpsLog(instance, start, end)).ToList();
                if (followLog.Count == 0)
                {
                    await Task.Delay(5000);
                    continue;
                }
                socket.Dialog(db => db.RemovePrefix().ForEach(followLog.AsEnumerable().Reverse(), (db, line) =>
                {
                    db.Msg($"{McColorCodes.GOLD}VPS{McColorCodes.RESET} ");
                    WriteLine(db, line);
                }));
                await Task.Delay(5000);
                start = end;
                end = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5);
            }
        }
        if (socket.SessionInfo.ActiveStream != null)
        {
            socket.SessionInfo.ActiveStream = null;
            socket.Dialog(db => db.MsgLine($"Stopped following {instance.Id}"));
            return;
        }
        var log = await service.GetVpsLog(instance, DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow);
        socket.Dialog(db => db.ForEach(log.Reverse(), (db, line) => WriteLine(db, line)));

        static ModCommands.Dialogs.DialogBuilder WriteLine(ModCommands.Dialogs.DialogBuilder db, string line)
        {
            var url = line.Split(' ').FirstOrDefault(l => l.StartsWith("http"));
            if (url != null)
            {
                return db.MsgLine(line.Replace(url, $"{McColorCodes.AQUA}{url}{McColorCodes.RESET}"), url, "open url");
            }
            return db.MsgLine(line);
        }
    }

    private async Task TurnOn(MinecraftSocket socket, VpsInstanceManager service, string[] args)
    {
        if (args.Length < 2)
        {
            socket.Dialog(db => db.MsgLine($"Usage: {McColorCodes.AQUA}/cofl vps turnOn [vpsId]"));
            return;
        }
        Instance instance = await GetTargetVps(socket, service, args);
        await service.TurnOnVps(instance);
        socket.Dialog(db => db.MsgLine($"Turned on {instance.Id}"));
    }

    private async Task TurnOff(MinecraftSocket socket, VpsInstanceManager service, string[] args)
    {
        if (args.Length < 2)
        {
            socket.Dialog(db => db.MsgLine($"Usage: {McColorCodes.AQUA}/cofl vps turnOff [vpsId]"));
            return;
        }
        Instance instance = await GetTargetVps(socket, service, args);
        await service.TurnOffVps(instance);
        socket.Dialog(db => db.MsgLine($"Turned off {instance.Id}"));
    }

    private static async Task<Instance> GetTargetVps(MinecraftSocket socket, VpsInstanceManager service, string[] args)
    {
        var instances = await service.GetVpsForUser(socket.UserId);
        if (args.Length == 1)
            return instances.FirstOrDefault();
        var vpsId = args[1];
        var instance = instances.FirstOrDefault(i => i.Id.ToString().EndsWith(vpsId));
        if (instance == null)
        {
            throw new CoflnetException("not_found", $"The instance with id {vpsId} was not found");
        }

        return instance;
    }

    private async Task UpdateSettings(MinecraftSocket socket, VpsInstanceManager service, string[] args)
    {
        if (args.Length < 3)
        {
            socket.Dialog(db => db.MsgLine($"Usage: {McColorCodes.AQUA}/cofl vps set [vpsId] <key> <value>"));
            return;
        }
        var vpsId = args[1];
        var key = args[2];
        var value = string.Join(" ", args.Skip(3));
        var instances = await service.GetVpsForUser(socket.UserId);
        var instance = instances.FirstOrDefault(i => i.Id.ToString().EndsWith(vpsId));
        await service.UpdateSetting(socket.UserId, key, value, instance);
        socket.Dialog(db => db.MsgLine($"Updated {McColorCodes.AQUA}{key}{McColorCodes.RESET} to {McColorCodes.AQUA}{value}{McColorCodes.RESET} on {McColorCodes.AQUA}{vpsId}"));
    }

    private static async Task Create(MinecraftSocket socket, VpsInstanceManager service, string[] args)
    {
        if (args.Length < 2 || args[1] != "tpm+")
        {
            socket.Dialog(db => db.MsgLine($"Usage: {McColorCodes.AQUA}/vps create tpm+"));
            return;
        }
        var instance = new Instance
        {
            OwnerId = socket.UserId,
            AppKind = args[1],
            CreatedAt = DateTime.UtcNow,
            PaidUntil = DateTime.UtcNow.AddDays(1),
        };
        var secret = Guid.NewGuid().ToString();
        (_, var hashed) = socket.GetService<IdConverter>().ComputeConnectionId(socket.SessionInfo.McName, secret);
        await service.AddVps(instance, new()
        {
            SessionId = secret,
            UserName = socket.SessionInfo.McName,
        });
        socket.AccountInfo.ConIds.Add(hashed); // auth that id
        await socket.sessionLifesycle.AccountInfo.Update();
    }
}
