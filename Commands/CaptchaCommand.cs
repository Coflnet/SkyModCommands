using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class CaptchaCommand : McCommand
    {
        int debugMultiplier = 01;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var info = socket.SessionInfo.captchaInfo;
            var solution = info.CurrentSolutions;
            info.CurrentSolutions = new List<string>();
            var receivedAt = DateTime.UtcNow;
            var attempt = arguments.Trim('"');
            if (attempt == "another")
            {
                await RequireAnotherSolve(socket, info);
                return;
            }
            if (attempt == "small")
                info.ChatWidth = 19;
            else if (attempt == "big")
                info.ChatWidth = 55;
            else if (string.IsNullOrEmpty(attempt))
                socket.SendMessage(COFLNET + McColorCodes.BLUE + "You requested to get a new captcha. Have fun.");
            else
            {
                socket.SendMessage(COFLNET + "Checking your response");
                await Task.Delay(1000 * debugMultiplier).ConfigureAwait(false);
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
                socket.tracer.ActiveSpan.Log("solved captcha");
                await Task.Delay(2000).ConfigureAwait(false);
                socket.SendMessage(COFLNET + McColorCodes.GREEN + "Your afk delay will be removed for the next update\n");

                return;
            }

            if (!string.IsNullOrEmpty(attempt))
                socket.SendMessage($"{COFLNET}Your answer was {McColorCodes.RED}not correct{DEFAULT_COLOR}, lets try again");
            await RequireAnotherSolve(socket, info);

            info.RequireSolves++;
        }

        private async Task RequireAnotherSolve(MinecraftSocket socket, CaptchaInfo info)
        {
            socket.SendMessage(COFLNET + "Generating captcha");
            await Task.Delay(Math.Max(info.RequireSolves, 1) * 1000 * debugMultiplier).ConfigureAwait(false);
            socket.SendMessage(new CaptchaGenerator().SetupChallenge(socket, info));
        }
    }
}
