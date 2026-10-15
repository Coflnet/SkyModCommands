using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class TopUpCommandTests
{
    [TestCase("s_cc_1800")]
    [TestCase("\"s_cc_1800\"")]
    public async Task StripeWithoutCountryOffersClickableCountrySelection(string arguments)
    {
        var socket = new CountrySelectionSocket();

        await new TopUpCommand().Execute(socket, arguments);

        Assert.That(socket.Messages.Any(part => part.text.Contains("Select your country")), Is.True);
        Assert.That(socket.Messages.Select(part => part.onClick), Does.Contain("/cofl topup s_cc_1800 DE"));
        Assert.That(socket.Messages.Any(part => part.text.Contains("Contacting payment provider")), Is.False);
    }

    [TestCase("CA")]
    [TestCase("AU")]
    public async Task StripeOffersCountriesBeyondCoinGateList(string country)
    {
        var socket = new CountrySelectionSocket();

        await new TopUpCommand().Execute(socket, "s_cc_1800");

        Assert.That(socket.Messages.Select(part => part.onClick), Does.Contain($"/cofl topup s_cc_1800 {country}"));
    }

    [TestCase("s_cc_1800", true)]
    [TestCase("c_cc_1800", false)]
    [TestCase("c_cc_5400", true)]
    public async Task UsMinimumAppliesOnlyToCryptoCountrySelection(string productId, bool offersUnitedStates)
    {
        var socket = new CountrySelectionSocket();

        await new TopUpCommand().Execute(socket, productId);

        Assert.That(socket.Messages.Any(part => part.onClick == $"/cofl topup {productId} US"), Is.EqualTo(offersUnitedStates));
        Assert.That(socket.Messages.Any(part => part.text.Contains("crypto payments require at least")), Is.EqualTo(!offersUnitedStates));
        Assert.That(socket.Messages.Select(part => part.onClick), Does.Contain($"/cofl topup {productId} DE"));
    }

    private sealed class CountrySelectionSocket : MinecraftSocket
    {
        public List<ChatPart> Messages { get; } = new();

        // No payment services or connection: selecting a country must finish before contacting a provider.
        public override T GetService<T>() => null;

        public override bool SendMessage(params ChatPart[] parts)
        {
            Messages.AddRange(parts);
            return true;
        }
    }
}
