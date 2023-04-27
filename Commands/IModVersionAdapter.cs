using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public interface IModVersionAdapter
    {
        Task<bool> SendFlip(FlipInstance flip);
        void SendSound(string name, float pitch = 1);
        void SendMessage(params ChatPart[] parts);
        void SendLoginPrompt(string v);
        void OnAuthorize(AccountInfo accountInfo);
    }
}