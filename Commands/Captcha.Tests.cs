using System.Linq;
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
            var socket = new Mock<IMinecraftSocket>();
            socket.SetupGet(s => s.SessionInfo).Returns(session);
            var response = generator.SetupChallenge(socket.Object, session);
            Assert.IsTrue(JsonConvert.SerializeObject(response).Contains(session.CaptchaSolutions.First()));
        }   
    }
}
