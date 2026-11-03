using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
#nullable enable
namespace Coflnet.Sky.Commands.MC
{
    public class NoLoginException : CoflnetException
    {
        public NoLoginException() : base(ErrorSlugs.NoLogin, $"We could not determine your user account. Please make sure to login and try again. {McColorCodes.AQUA}/cofl verify{McColorCodes.GRAY} will help you with that.")
        {
        }
    }
#nullable restore
}
