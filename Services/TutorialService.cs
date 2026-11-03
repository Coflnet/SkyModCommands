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
    /// <summary>
    /// Releases the tutorial state held for the connection, call when it closes
    /// </summary>
    void Release(IMinecraftSocket socket);
}

public class UserTutorialState
{
    public SelfUpdatingValue<HashSet<string>> SolvedTutorials;
    public DateTime DoNotShowUntil;
}

public class TutorialService : ITutorialService
{
    private ConcurrentDictionary<string, TutorialBase> Tutorials = new();
    private readonly object stateLock = new();
    private readonly Dictionary<string, UserEntry> users = new();
    private readonly Dictionary<object, HashSet<string>> userIdsByOwner = new();
    /// <summary>Owners that were released, weak so closed connections can be collected</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> releasedOwners = new();
    private readonly Func<string, Task<SelfUpdatingValue<HashSet<string>>>> createSolved;

    /// <summary>Tutorial state of one user, shared by all connections of that user</summary>
    private sealed class UserEntry
    {
        public Lazy<Task<UserTutorialState>> State;
        public readonly HashSet<object> Owners = new();
    }

    public TutorialService() : this(userId => SelfUpdatingValue<HashSet<string>>.Create(userId, "solvedTutorials", () => new()))
    {
    }

    internal TutorialService(Func<string, Task<SelfUpdatingValue<HashSet<string>>>> createSolved)
    {
        this.createSolved = createSolved;
    }

    public async Task Trigger<T>(IMinecraftSocket socket) where T : TutorialBase
    {
        if(socket.SessionInfo.IsMacroBot && !typeof(MacroBotTutorial).IsAssignableFrom(typeof(T)))
            return; // don't show tutorials to bots (only MacroBotTutorial subtypes are shown)
        var instance = GetInstance<T>();
        var userId = socket.AccountInfo?.UserId;
        if (string.IsNullOrEmpty(userId))
            return;
        var state = await GetSolved(socket, userId);
        if ((state?.SolvedTutorials?.Value == default) || state.SolvedTutorials.Value.Contains(instance.Name) || state.DoNotShowUntil > DateTime.UtcNow)
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

    /// <summary>
    /// Gets the shared state of the user and remembers that <paramref name="owner"/> (a connection) uses it,
    /// the state is disposed once every owner called <see cref="Release"/>
    /// </summary>
    internal async Task<UserTutorialState> GetSolved(object owner, string userId)
    {
        UserEntry entry;
        Task<UserTutorialState> loading;
        lock (stateLock)
        {
            if (IsReleased(owner))
                return null; // nobody would release the state again
            if (!users.TryGetValue(userId, out entry))
                users[userId] = entry = NewEntry(userId);
            entry.Owners.Add(owner);
            if (!userIdsByOwner.TryGetValue(owner, out var ids))
                userIdsByOwner[owner] = ids = new();
            ids.Add(userId);
            // only starts the load, doing it inside the lock keeps Release from seeing an entry without a value
            loading = entry.State.Value;
        }
        try
        {
            return await loading.ConfigureAwait(false);
        }
        catch
        {
            RemoveFailedEntry(userId, entry);
            throw;
        }
    }

    private bool IsReleased(object owner)
    {
        return releasedOwners.TryGetValue(owner, out _) || owner is IMinecraftSocket { IsClosed: true };
    }

    private UserEntry NewEntry(string userId)
    {
        return new UserEntry { State = new(() => LoadState(userId)) };
    }

    private async Task<UserTutorialState> LoadState(string userId)
    {
        var solvedList = await createSolved(userId).ConfigureAwait(false);
        return new() { SolvedTutorials = solvedList };
    }

    private void RemoveFailedEntry(string userId, UserEntry failed)
    {
        lock (stateLock)
        {
            if (users.TryGetValue(userId, out var current) && current == failed)
                users.Remove(userId);
        }
    }

    /// <summary>
    /// Called when a connection closes, disposes the state of users without remaining connections
    /// </summary>
    public void Release(IMinecraftSocket socket)
    {
        if (socket == null)
            return;
        List<UserEntry> orphaned = new();
        lock (stateLock)
        {
            releasedOwners.AddOrUpdate(socket, socket);
            if (!userIdsByOwner.Remove(socket, out var ids))
                return;
            foreach (var id in ids)
                if (users.TryGetValue(id, out var entry) && RemoveOwner(entry, socket))
                {
                    users.Remove(id);
                    orphaned.Add(entry);
                }
        }
        orphaned.ForEach(DisposeEntry);
    }

    private static bool RemoveOwner(UserEntry entry, object owner)
    {
        entry.Owners.Remove(owner);
        return entry.Owners.Count == 0;
    }

    private static void DisposeEntry(UserEntry entry)
    {
        if (!entry.State.IsValueCreated)
            return;
        entry.State.Value.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                t.Result.SolvedTutorials?.Dispose();
        }, TaskScheduler.Default);
    }

    private TutorialBase GetInstance<T>() where T : TutorialBase
    {
        return Tutorials.GetOrAdd(typeof(T).Name, t => Activator.CreateInstance<T>());
    }

    public async Task CommandInput(MinecraftSocket socket, string v)
    {
        var id = socket.AccountInfo?.UserId;
        if (string.IsNullOrEmpty(id))
            return;
        var state = await GetSolved(socket, id);
        if (state?.SolvedTutorials?.Value == null || state.SolvedTutorials.Value.Contains(v))
            return;
        if (!Tutorials.ContainsKey(v))
            return; // doesn't exist, can't be completed
        state.SolvedTutorials.Value.Add(v);
        await state.SolvedTutorials.Update();
        socket.Dialog(db => db.Msg("not gonna show again"));
    }
}