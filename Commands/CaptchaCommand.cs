using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class CaptchaCommand : McCommand
    {
        int debugMultiplier = 1;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var sessionInfo = socket.SessionInfo;
            var solution = sessionInfo.CaptchaSolutions;
            sessionInfo.CaptchaSolutions = new List<string>();
            var attempt = arguments.Trim('"');
            if (string.IsNullOrEmpty(attempt))
                socket.SendMessage(COFLNET + McColorCodes.BLUE + "You requested to get a new captcha. Have fun.");
            else
                socket.SendMessage(COFLNET + "Checking your response");
            await Task.Delay(2000 * debugMultiplier).ConfigureAwait(false);
            if (solution.Contains(attempt))
            {
                sessionInfo.CaptchaFailedTimes--;
                if (sessionInfo.CaptchaFailedTimes < 0)
                    sessionInfo.CaptchaFailedTimes = 0;
                if (sessionInfo.CaptchaFailedTimes > 0)
                {
                    socket.SendMessage(COFLNET + McColorCodes.GREEN + "You solved the captcha, but you failed too many previously so please solve another one\n");
                    socket.SendMessage(new CaptchaGenerator().SetupChallenge(socket, sessionInfo));
                    return;
                }
                socket.SendMessage(COFLNET + McColorCodes.GREEN + "Thanks for confirming that you are a real user\n");

                sessionInfo.LastCaptchaSolve = DateTime.UtcNow;
                socket.sessionLifesycle.AccountInfo.Value.LastCaptchaSolve = DateTime.UtcNow;
                await socket.sessionLifesycle.AccountInfo.Update();
                socket.tracer.ActiveSpan.Log("solved captcha");
                await Task.Delay(2000).ConfigureAwait(false);
                socket.SendMessage(COFLNET + McColorCodes.GREEN + "Your afk delay will be removed for the next update\n");

                return;
            }
            await Task.Delay(sessionInfo.CaptchaFailedTimes * 1000 * debugMultiplier).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(attempt))
                socket.SendMessage(COFLNET + "Your answer was not correct, lets try again");
            socket.SendMessage(new CaptchaGenerator().SetupChallenge(socket, sessionInfo));

            sessionInfo.CaptchaFailedTimes++;
        }
    }
}
