using System.Threading.Channels;

namespace Coflnet.Sky.ModCommands.Services.RealizedProfit;

/// <summary>
/// Hand-off between disconnecting sessions and <see cref="RealizedProfitBackgroundService"/>.
/// Bounded and dropping the oldest entry so a reconnect storm can never grow memory or block a dispose.
/// </summary>
public class RealizedProfitQueue
{
    public const int Capacity = 2000;
    private readonly Channel<RealizedProfitSnapshot> channel = Channel.CreateBounded<RealizedProfitSnapshot>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public bool TryEnqueue(RealizedProfitSnapshot snapshot) => channel.Writer.TryWrite(snapshot);

    public ChannelReader<RealizedProfitSnapshot> Reader => channel.Reader;
}
