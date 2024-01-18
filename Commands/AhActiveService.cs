using System;
using Coflnet.Sky.Commands.Shared;
#nullable enable
namespace Coflnet.Sky.Commands.MC;
public interface IAhActive
{
    bool IsAhDisabledDerpy { get; }
}
public class AhActiveService : IAhActive
{
    public bool IsAhDisabledDerpy {get; private set;}
    CurrentMayorDetailedFlipFilter instance = new CurrentMayorDetailedFlipFilter();

    private FilterStateService filterStateService;

    public AhActiveService(FilterStateService filterStateService)
    {
        this.filterStateService = filterStateService;
    }
    public AhActiveService()
    {
        MinecraftSocket.NextUpdateStart += () =>
        {
            try
            {
                IsAhDisabledDerpy = filterStateService!.State.CurrentMayor == "Derpy";
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "checking if ah is disabled");
            }
        };
    }
#nullable restore
}
