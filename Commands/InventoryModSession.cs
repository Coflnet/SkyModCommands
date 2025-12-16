using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class InventoryModSession : ModSessionLifesycle
    {

        public InventoryModSession(MinecraftSocket socket) : base(socket)
        {

        }

        protected override async Task SubToSettings(string val)
        {
            if (socket.IsClosed)
                return;
            await base.SubToSettings(val);
            var settings = await SelfUpdatingValue<PrivacySettings>.Create(val, "privacySettings", () =>
            {
                return Shared.PrivacySettings.Default;
            });
            if (socket.IsClosed)
            {
                settings.Dispose();
                return;
            }
            socket.sessionLifesycle.PrivacySettings?.Dispose();
            socket.sessionLifesycle.PrivacySettings = settings;
            socket.sessionLifesycle.PrivacySettings.AfterChange -= UpdatePrivacySettings;
            socket.sessionLifesycle.PrivacySettings.AfterChange += UpdatePrivacySettings;
            var pSettings = socket.sessionLifesycle.PrivacySettings.Value;
            if (pSettings.ChatRegex != Shared.PrivacySettings.DefaultChatRegex)
                pSettings.ChatRegex = Shared.PrivacySettings.DefaultChatRegex;

            if (!pSettings.NoMessageBlocking && pSettings.ChatBlockRegex != Shared.PrivacySettings.DefaultChatBlockRegex)
                pSettings.ChatBlockRegex = Shared.PrivacySettings.DefaultChatBlockRegex;
            else if (pSettings.NoMessageBlocking)
                pSettings.ChatBlockRegex = "$^"; //matches nothing
            UpdatePrivacySettings(pSettings);
            //if(isDefault)
            //    await socket.sessionLifesycle.PrivacySettings.Update();
        }

        private void UpdatePrivacySettings(PrivacySettings settings)
        {
            socket.Send(Response.Create("privacySettings", settings));
        }
    }
}
