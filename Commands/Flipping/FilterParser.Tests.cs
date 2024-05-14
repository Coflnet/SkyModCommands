using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Items.Client.Api;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;
public class FilterParserTests
{
    [Test]
    public async Task Quoted()
    {
        var parser = new FilterParser();
        var filters = new Dictionary<string, string>();
        var apiMock = new Mock<IItemsApi>();
        apiMock.Setup(api => api.ItemItemTagModifiersAllGetAsync("*", 0, default)).ReturnsAsync(new Dictionary<string, List<string>>());
        DiHandler.OverrideService<IItemsApi, IItemsApi>(apiMock.Object);
        DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine());
        var remaining = await parser.ParseFiltersAsync(null, "itemNameContains=\"test string\"", filters, new List<string> { "ItemNameContains" });
        Assert.That(remaining, Is.Empty);
        Assert.That(filters["ItemNameContains"], Is.EqualTo("test string"));

    }
}
