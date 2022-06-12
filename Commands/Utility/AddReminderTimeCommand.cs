using System;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class AddReminderTimeCommand : ReminderCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var time = Convert<Extension>(arguments);
            var toModify = await Find(socket, time.Text);
            toModify.First().TriggerTime += time.Time;
            await GetSettings(socket).Update();
            socket.SendMessage(McColorCodes.GREEN + "Added " + McColorCodes.YELLOW + time.Time.ToString(@"hh\:mm\:ss") + McColorCodes.GREEN + " to " + McColorCodes.WHITE + time.Text);
        }

        public class Extension
        {
            public string Text;
            public TimeSpan Time;

            public Extension()
            {
            }

            public Extension(string text, TimeSpan time)
            {
                Text = text;
                Time = time;
            }
        }
    }
}