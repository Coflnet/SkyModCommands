using Coflnet.Sky.Core;
#nullable enable
namespace Coflnet.Sky.Commands.MC
{
    public class NoLoginException : CoflnetException
    {
        public NoLoginException() : base("no_login", "We could not determine your user account. Please make sure to login and try again.")
        {
        }
    }
#nullable restore
}
