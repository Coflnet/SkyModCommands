using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Manage your reminders", 
        "Reminders are messages that will be sent to you after a certain time",
        "to add use /cofl reminder add 1h30m <message>",
        "to remove use /cofl reminder remove <message>")]
    public class ReminderCommand : ListCommand<Reminder, List<Reminder>>
    {
        public override bool IsPublic => true;
        protected override Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
        {
            var args = val.Split(' ');
            if (args.Length < 2)
                throw new Core.CoflnetException("inalid_format", "Usage: /cofl reminder add 1h30m <message>");
            TimeSpan ts = ParseTime(args[0]);

            var reminder = new Reminder(val.Substring(val.IndexOf(' ') + 1), DateTime.UtcNow + ts - TimeSpan.FromSeconds(20));
            return Task.FromResult(new List<CreationOption>(){
                new CreationOption(){
                    Element = reminder
                }
            }.AsEnumerable());
        }

        public static TimeSpan ParseTime(string args)
        {
            string[] formats = { @"m\m", @"h\hm\m" };
            TimeSpan ts;
            if (!TimeSpan.TryParseExact(args, formats, null, out ts))
            {
                throw new Core.CoflnetException("invalid_format", "Time format has to be '1h2m' or '1m'");
            }

            return ts;
        }

        protected override string Format(Reminder elem)
        {
            return McColorCodes.WHITE + elem.Text + McColorCodes.GRAY + " triggers in " + McColorCodes.YELLOW + (elem.TriggerTime - DateTime.UtcNow).ToString(@"h\h mm\m");
        }

        protected override string GetId(Reminder elem)
        {
            return elem.Text;
        }

        protected override Task<List<Reminder>> GetList(MinecraftSocket socket)
        {
            var settings = GetSettings(socket);
            return Task.FromResult(settings.Value.Reminders);
        }

        protected override async Task Update(MinecraftSocket socket, List<Reminder> newCol)
        {
            var list = GetSettings(socket);
            list.Value.Reminders = newCol;
            await list.Update(list.Value);
        }

        protected SelfUpdatingValue<AccountSettings> GetSettings(MinecraftSocket socket)
        {
            var settings = socket.sessionLifesycle.AccountSettings;
            if (settings.Value == null)
                throw new Core.CoflnetException("login", "Login is required to use this command");
            if (settings.Value.Reminders == null)
                settings.Value.Reminders = new();
            return settings;
        }

        protected override DialogBuilder FormatForList(DialogBuilder d, Reminder e)
        {
            var formatted = Format(e);
            return d.Msg(formatted)
                .CoflCommand<AddReminderTimeCommand>(McColorCodes.RED + " [+5m]", GenerateContext(e, TimeSpan.FromMinutes(5)), "Add 5 minutes")
                .CoflCommand<AddReminderTimeCommand>(McColorCodes.GREEN + "[+1h]", GenerateContext(e, TimeSpan.FromHours(1)), "Add 1 hour");
        }

        private static string GenerateContext(Reminder e, TimeSpan timespan)
        {
            return JsonConvert.SerializeObject(new AddReminderTimeCommand.Extension(e.Text, timespan));
        }
    }
}