using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class CaptchaCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SendMessage(COFLNET + "Checking your response");
            var sessionInfo = socket.SessionInfo;
            var solution = sessionInfo.CaptchaSolution;
            await Task.Delay(2000);
            var attempt = arguments.Trim('"');
            if (solution == attempt)
            {
                sessionInfo.CaptchaFailedTimes /= 2;
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
                await Task.Delay(2000);
                socket.SendMessage(COFLNET + McColorCodes.GREEN + "Your afk delay will be removed for the next update\n");

                return;
            }

            socket.SendMessage(COFLNET + "Your answer was not correct, lets try again");
            socket.SendMessage(new CaptchaGenerator().SetupChallenge(socket, sessionInfo));

            sessionInfo.CaptchaFailedTimes++;

            return;
        }
    }
}
