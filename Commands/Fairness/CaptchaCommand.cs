using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Solve a captcha",
        "You will be asked to solve a captcha if you are afk for too long",
        "You can also use this command to get a new captcha",
        "Example: /cl captcha another",
        "Use /cl captcha vertical to letters below each other",
        "Which helps if you have a mod with different font",
        "Captchas are necesary to prevent bots from using the flipper")]
    public class CaptchaCommand : McCommand
    {
        public override bool IsPublic => true;
        private int debugMultiplier = 1;
        private HashSet<string> formats = new() {
            "vertical", // fallback one letter per line
            "big", // normal
            "optifine", // different characters for optifine
            "short" // different characters for when spaces are shorter
            };
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var info = socket.SessionInfo.captchaInfo;
            var accountInfo = socket.AccountInfo;
            var solution = info.CurrentSolutions;
            info.CurrentSolutions = new List<string>();
            var receivedAt = DateTime.UtcNow;
            var attempt = Convert<string>(arguments);
            info.CaptchaRequests++;
            Console.WriteLine($"Recieved {attempt}");
            if (formats.Contains(attempt))
            {
                accountInfo.CaptchaType = attempt;
                await RequireAnotherSolve(socket, info);
                await socket.sessionLifesycle.AccountInfo.Update();
                Console.WriteLine("Updated captcha type " + attempt);
                return;
            }
            if (attempt == "debug")
            {

                socket.Dialog(db => db
                    .ForEach("awiI🤨:|,:-.#ä+!^°~´` ", (db, c) => db.ForEach("01234567890123456789", (idb, ignore) => idb.Msg(c.ToString())).MsgLine("|"))
                    .LineBreak().ForEach(":;", (db, c) => db.ForEach("012345678901234567890123456789", (idb, ignore) => idb.Msg(c.ToString())).MsgLine("|"))
                    .LineBreak().ForEach("´", (db, c) => db.ForEach("012345678901234567890123", (idb, ignore) => idb.Msg(c.ToString())).MsgLine("|"))
                    .MsgLine("Please screenshot the above and post it to our bug-report channel on discord"));
                return;
            }
            if (attempt.StartsWith("config"))
            {

                var optionsFull = new List<string> { "🤨".First().ToString(), "🤨".First() + "🤨".First().ToString(), "!!", "ii", "#", "°", "█", "┇┇" };
                var optionsPartial = new List<string> { "::", "´´", ",,", "''", "``", "^", "☺", "⋅⋅⋅⋅" };
                var subargs = attempt.Split(' ');
                if (subargs.Length > 2)
                {
                    var part = subargs[2];
                    var accountSettings = socket.sessionLifesycle.AccountInfo.Value;
                    if (part == "full")
                    {
                        accountSettings.CaptchaBoldChar = subargs[3];
                    }
                    if (part == "part")
                    {
                        accountSettings.CaptchaSlimChar = subargs[3];
                    }
                    if (accountSettings.CaptchaBoldChar?.Length > 1 && accountSettings.CaptchaSlimChar?.Length > 1)
                    {
                        accountSettings.CaptchaSpaceCount = 1;
                        // half each of the bold and slim characters
                        accountSettings.CaptchaBoldChar = accountSettings.CaptchaBoldChar.Substring(0, accountSettings.CaptchaBoldChar.Length / 2);
                        accountSettings.CaptchaSlimChar = accountSettings.CaptchaSlimChar.Substring(0, accountSettings.CaptchaSlimChar.Length / 2);
                        Console.WriteLine("Updated captcha config halfed " + accountInfo.CaptchaBoldChar + " " + accountInfo.CaptchaSlimChar);
                    }
                    else
                    {
                        accountSettings.CaptchaSpaceCount = 2;
                    }

                    await socket.sessionLifesycle.AccountInfo.Update();
                    socket.SendMessage(COFLNET + "Updated captcha config");
                    var length = "01234567890123456789";
                    var spaceLength = length;
                    if (accountInfo.CaptchaSpaceCount == 2)
                        spaceLength = length + length;
                    socket.Dialog(db => db.MsgLine("If one of the two yellow lines does not line up click it")
                        .ForEach(length, (db, ignore) => db.Msg(accountInfo.CaptchaBoldChar))
                            .CoflCommand<CaptchaCommand>(McColorCodes.YELLOW + "|\n", "config full", "Does not line up")
                        .ForEach(spaceLength, (db, ignore) => db.Msg(" ")).MsgLine(McColorCodes.GREEN + "|")
                        .ForEach(length, (db, ignore) => db.Msg(accountInfo.CaptchaSlimChar))
                            .CoflCommand<CaptchaCommand>(McColorCodes.YELLOW + "|\n", "config part", "Does not line up")
                        .CoflCommand<CaptchaCommand>("They line up", "", "Request a better formated captcha"));
                    return;
                }
                if (subargs.Length > 1)
                {
                    if (subargs[1] == "full")
                        PrintOptions(socket, optionsFull, "full");
                    if (subargs[1] == "part")
                        PrintOptions(socket, optionsPartial, "part");
                    return;
                }
                PrintOptions(socket, optionsFull, "full");
                PrintOptions(socket, optionsPartial, "part");
                return;
            }
            if (attempt == "another")
            {
                await RequireAnotherSolve(socket, info);
                return;
            }
            if (string.IsNullOrEmpty(attempt))
                socket.Dialog(db => db
                    .MsgLine($"{McColorCodes.BLUE}You requested to get a new captcha.")
                    .MsgLine($"{McColorCodes.OBFUSCATED}OOO{McColorCodes.RESET + McColorCodes.BLUE} This counts as a failed attempt. Have fun."));
            else
            {
                socket.SendMessage(COFLNET + "Checking your response");
                await Task.Delay(500 * debugMultiplier).ConfigureAwait(false);
            }
            if (solution.Contains(attempt))
            {
                info.RequireSolves--;
                if (info.RequireSolves < 0)
                    info.RequireSolves = 0;
                if (info.RequireSolves > 0)
                {
                    socket.SendMessage(COFLNET + McColorCodes.GREEN + "You solved the captcha, but you failed too many previously so please solve another one\n");
                    await RequireAnotherSolve(socket, info).ConfigureAwait(false);
                    return;
                }
                socket.SendMessage(COFLNET + McColorCodes.GREEN + "Thanks for confirming that you are a real user\n");

                info.LastSolve = DateTime.UtcNow;
                socket.sessionLifesycle.AccountInfo.Value.LastCaptchaSolve = DateTime.UtcNow;
                await socket.sessionLifesycle.AccountInfo.Update();
                Activity.Current.Log("solved captcha");
                await Task.Delay(2000).ConfigureAwait(false);
                await socket.sessionLifesycle.DelayHandler.Update(await socket.sessionLifesycle.GetMinecraftAccountUuids(), DateTime.Now);
                socket.SendMessage(COFLNET + McColorCodes.GREEN + "Your afk delay was updated\n");

                return;
            }

            if (!string.IsNullOrEmpty(attempt))
                socket.SendMessage($"{COFLNET}Your answer was {McColorCodes.RED}not correct{DEFAULT_COLOR}, lets try again");
            await RequireAnotherSolve(socket, info);

            info.RequireSolves++;
            info.FaildCount++;
            if (info.FaildCount <= 1 || accountInfo.CaptchaType != "vertical")
                return;
            socket.SendMessage($"{McColorCodes.DARK_GREEN}NOTE:{McColorCodes.YELLOW} "
                + $"Please make sure that the vertical green lines ({McColorCodes.GREEN}|{McColorCodes.YELLOW}) at the end of the captcha line up continuously.\n"
                + $"{McColorCodes.YELLOW}If they don't line up click on {McColorCodes.AQUA}Vertical{McColorCodes.YELLOW} to get a simpler captcha");

            static void PrintOptions(MinecraftSocket socket, List<string> optionsFull, string part)
            {
                var length = "01234567890123456789";
                var prefix = $"/cofl captcha config set {part} ";
                socket.Dialog(db => db.MsgLine(McColorCodes.GRAY + "If none line up please report your texture pack on our discord server")
                    .ForEach(optionsFull, (db, character) => db.ForEach(length, (idb, ignore) => idb.Msg(character)).MsgLine($"{McColorCodes.YELLOW}|", prefix + character, "Click to select")
                        .ForEach(length + length, (idb, ignore) => idb.Msg(" ")).MsgLine($"{McColorCodes.GREEN}|"))
                        .MsgLine("Click on a yellow line that aligns with the green line")
                );
            }
        }

        private async Task RequireAnotherSolve(MinecraftSocket socket, CaptchaInfo info)
        {
            socket.SendMessage(COFLNET + "Generating captcha");
            await Task.Delay(Math.Max(Math.Max(info.RequireSolves, 1), info.CaptchaRequests) * 800 * debugMultiplier).ConfigureAwait(false);
            socket.SendMessage(new CaptchaGenerator().SetupChallenge(socket, info));
            if (info.CaptchaRequests > 10)
            {
                info.CaptchaRequests = 0;
                info.RequireSolves++;
            }
            if (info.RequireSolves > 3)
                info.RequireSolves = 3;
        }
    }
}
