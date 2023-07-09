using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using System.Diagnostics;
using Coflnet.Sky.ModCommands.Dialogs;
using System.Runtime.CompilerServices;
#nullable enable
namespace Coflnet.Sky.Commands.MC
{
    public interface IMinecraftSocket
    {
        long Id { get; }
        SessionInfo SessionInfo { get; }
        FlipSettings Settings { get; }
        AccountInfo AccountInfo { get; }
        string Version { get; }
        ActivitySource tracer { get; }
        Activity ConSpan { get; }
        FormatProvider formatProvider { get; }
        ModSessionLifesycle sessionLifesycle { get; }
        ConcurrentQueue<LowPricedAuction> LastSent { get; }
        string UserId { get; }

        event Action OnConClose;

        void Close();
        void Dialog(Func<DialogBuilder, DialogBuilder> creation);
        string Error(Exception exception, string? message = null, string? additionalLog = null);
        void ExecuteCommand(string command);
        string FormatPrice(long price);
        LowPricedAuction GetFlip(string uuid);
        string GetFlipMsg(FlipInstance flip);
        Task<string> GetPlayerName(string uuid);
        Task<string> GetPlayerUuid(string name, bool blockError);
        T GetService<T>() where T : class;
        void Log(string message, Microsoft.Extensions.Logging.LogLevel level = Microsoft.Extensions.Logging.LogLevel.Information);
        Activity RemoveMySelf();
        void Send(Response response);
        Task SendBatch(IEnumerable<LowPricedAuction> flips);
        Task<bool> SendFlip(LowPricedAuction flip);
        Task<bool> SendFlip(FlipInstance flip);
        void SendMessage(string text, string? clickAction = null, string? hoverText = null);
        Activity? CreateActivity(string name, Activity? parent = null);
        bool SendMessage(params ChatPart[] parts);
        Task<bool> SendSold(string uuid);
        void SendSound(string soundId, float pitch = 1);
        void SetLifecycleVersion(string version);
        void SheduleTimer(ModSettings? mod = null, Activity? timerSpan = null);
        ConfiguredTaskAwaitable TryAsyncTimes(Func<Task> action, string errorMessage, int times = 3);
        Task<AccountTier> UserAccountTier();
    }
#nullable restore
}
