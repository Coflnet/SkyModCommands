using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.MC;
using System.Collections.Concurrent;
using Coflnet.Sky.ModCommands.Tutorials;

namespace Coflnet.Sky.ModCommands.Services;

public interface ITutorialService
{
    Task CommandInput(MinecraftSocket socket, string v);
    Task Trigger<T>(IMinecraftSocket socket) where T : TutorialBase;
}

public class TutorialService : ITutorialService
{
    private ConcurrentDictionary<string, TutorialBase> Tutorials = new();
    private ConcurrentDictionary<string, SelfUpdatingValue<HashSet<string>>> ReadTutorials = new();

    public async Task Trigger<T>(IMinecraftSocket socket) where T : TutorialBase
    {
        if(socket.SessionInfo.IsMacroBot)
            return; // don't show tutorials to bots
        var instance = GetInstance<T>();
        var userId = socket.AccountInfo?.UserId;
        if (string.IsNullOrEmpty(userId))
            return;
        var solved = await GetSolved(userId);
        if (solved.Value.Contains(instance.Name))
            return; // already seen

        socket.Dialog(db =>
        {
            instance.Trigger(db, socket);
            return db.CoflCommand<TutorialCommand>(
                $"{McColorCodes.GRAY}[{McColorCodes.GREEN}Got it{McColorCodes.GRAY}]",
                instance.Name,
                $"{McColorCodes.GREEN}Please don't show again,\n{McColorCodes.GOLD} I understood it");
        });
    }

    private async Task<SelfUpdatingValue<HashSet<string>>> GetSolved(string userId)
    {
        if (!ReadTutorials.TryGetValue(userId.ToString(), out var solved))
        {
            solved = await SelfUpdatingValue<HashSet<string>>.Create(userId, "solvedTutorials", () => new());
            ReadTutorials.TryAdd(userId, solved);
        }

        return solved;
    }

    private TutorialBase GetInstance<T>() where T : TutorialBase
    {
        return Tutorials.GetOrAdd(typeof(T).Name, t => Activator.CreateInstance<T>());
    }

    public async Task CommandInput(MinecraftSocket socket, string v)
    {
        var id = socket.AccountInfo?.UserId;
        var solved = await GetSolved(id);
        if (solved.Value.Contains(v))
            return;
        if (!Tutorials.ContainsKey(v))
            return; // doesn't exist, can't be completed
        solved.Value.Add(v);
        await solved.Update();
        socket.Dialog(db => db.Msg("not gonna show again"));
    }
}