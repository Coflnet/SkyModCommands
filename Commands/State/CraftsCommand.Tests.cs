using System.Linq;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class CraftsCommandTests : CraftsCommand
{
    [TestCase("craft", true)]
    [TestCase(null, false)]
    public void HighlightsSavingsOnlyForSelectedSubcrafts(string type, bool highlighted)
    {
        var db = DialogBuilder.New;
        Format(new MinecraftSocket(), db, new ProfitableCraft
        {
            ItemId = "TEST",
            ItemName = "Test",
            Ingredients = new() { new() { ItemId = "DIAMOND", Count = 2, Cost = 400, BuyOrderCost = 800, Type = type } }
        });

        var part = db.Build().Single(p => p.onClick == "/cofl recipe TEST");
        Assert.That(part.text.Contains("Subcraft saves 400"), Is.EqualTo(highlighted));
    }
}
