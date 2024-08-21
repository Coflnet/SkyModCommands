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

public class UserTutorialState
{
    public SelfUpdatingValue<HashSet<string>> SolvedTutorials;
    public DateTime DoNotShowUntil;
}

public class TutorialService : ITutorialService
{
    private ConcurrentDictionary<string, TutorialBase> Tutorials = new();
    private ConcurrentDictionary<string, UserTutorialState> ReadTutorials = new();

    public async Task Trigger<T>(IMinecraftSocket socket) where T : TutorialBase
    {
        if(socket.SessionInfo.IsMacroBot)
            return; // don't show tutorials to bots
        var instance = GetInstance<T>();
        var userId = socket.AccountInfo?.UserId;
        if (string.IsNullOrEmpty(userId))
            return;
        var state = await GetSolved(userId);
        if (state.SolvedTutorials.Value.Contains(instance.Name) || state.DoNotShowUntil > DateTime.UtcNow)
            return; // already seen

        socket.Dialog(db =>
        {
            instance.Trigger(db, socket);
            return db.CoflCommand<TutorialCommand>(
                $"{McColorCodes.GRAY}[{McColorCodes.GREEN}Got it{McColorCodes.GRAY}]",
                instance.Name,
                $"{McColorCodes.GREEN}Please don't show again,\n{McColorCodes.GOLD} I understood it");
        });
        // to not overwhelm the user tutorials are capped at 1 every 3 minutes
        state.DoNotShowUntil = DateTime.UtcNow.AddMinutes(3);
    }

    private async Task<UserTutorialState> GetSolved(string userId)
    {
        if (!ReadTutorials.TryGetValue(userId.ToString(), out var solved))
        {
            var solvedList = await SelfUpdatingValue<HashSet<string>>.Create(userId, "solvedTutorials", () => new());
            ReadTutorials.TryAdd(userId, new (){SolvedTutorials = solvedList});
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
        var state = await GetSolved(id);
        if (state.SolvedTutorials.Value.Contains(v))
            return;
        if (!Tutorials.ContainsKey(v))
            return; // doesn't exist, can't be completed
        state.SolvedTutorials.Value.Add(v);
        await state.SolvedTutorials.Update();
        socket.Dialog(db => db.Msg("not gonna show again"));
    }
}