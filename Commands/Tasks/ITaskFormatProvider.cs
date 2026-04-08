using System;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Provides formatting methods for prices and times, decoupled from MinecraftSocket.
/// </summary>
public interface ITaskFormatProvider
{
    string FormatPrice(double price);
    string FormatTime(TimeSpan time);
}

/// <summary>
/// Default implementation wrapping MinecraftSocket formatting.
/// </summary>
public class MinecraftSocketFormatProvider : ITaskFormatProvider
{
    private readonly MinecraftSocket _socket;

    public MinecraftSocketFormatProvider(MinecraftSocket socket)
    {
        _socket = socket;
    }

    public string FormatPrice(double price) => _socket.FormatPrice(price);
    public string FormatTime(TimeSpan time) => _socket.formatProvider.FormatTime(time);
}

/// <summary>
/// Simple format provider for use in tests, no socket dependency.
/// </summary>
public class SimpleTaskFormatProvider : ITaskFormatProvider
{
    public string FormatPrice(double price) => FormatProvider.FormatPriceShort(price);
    public string FormatTime(TimeSpan time) => FormatProvider.FormatTimeGlobal(time);
}
