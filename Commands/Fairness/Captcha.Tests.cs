using System.Linq;
using Coflnet.Sky.Commands.Shared;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC
{
    public class CaptchaTests
    {
        [Test]
        public void Generation()
        {
            var generator = new CaptchaGenerator();
            var session = new SessionInfo();
            var accountInfo = new AccountInfo();
            var socket = new Mock<IMinecraftSocket>();
            socket.SetupGet(s => s.SessionInfo).Returns(session);
            socket.SetupGet(s => s.AccountInfo).Returns(accountInfo);
            var response = generator.SetupChallenge(socket.Object, session.captchaInfo);
            Assert.That(JsonConvert.SerializeObject(response).Contains(session.CaptchaSolutions.First()));
        }   
    }
}
