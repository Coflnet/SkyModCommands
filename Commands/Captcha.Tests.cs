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
            var response = generator.SetupChallenge(session);
            Assert.IsTrue(JsonConvert.SerializeObject(response).Contains(session.CaptchaSolution));
        }   
    }
}
