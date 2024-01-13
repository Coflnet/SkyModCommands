using System;
using Coflnet.Sky.Commands.Shared;
#nullable enable
namespace Coflnet.Sky.Commands.MC
{
    public class AhActiveService
    {
        public bool IsAhDisabledDerpy;
        CurrentMayorDetailedFlipFilter instance = new CurrentMayorDetailedFlipFilter();

        public AhActiveService()
        {
            MinecraftSocket.NextUpdateStart += () =>
            {
                try
                {
                    var service = DiHandler.GetService<FilterStateService>();
                    IsAhDisabledDerpy = service.State.CurrentMayor == "Derpy";
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, "checking if ah is disabled");
                }
            };
        }
    }
#nullable restore
}
