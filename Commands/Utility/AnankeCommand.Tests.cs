using System;
using System.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class AnankeCommandTests
{
    [Test]
    public void CommandConstructsSuccessfully()
    {
        var command = new AnankeCommand();
        Assert.That(command, Is.Not.Null);
        Assert.That(command.IsPublic, Is.True);
    }

    [Test]
    public void NoDuplicateKeys()
    {
        var command = new AnankeCommand();
        // Access the private costs dictionary via reflection
        var costsField = typeof(AnankeCommand).GetField("costs", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(costsField, Is.Not.Null, "Could not find costs field");
        
        var costs = costsField.GetValue(null) as System.Collections.Generic.Dictionary<string, (double, long)>;
        Assert.That(costs, Is.Not.Null, "Costs dictionary is null");
        
        var keys = costs.Keys.ToList();
        var distinctKeys = keys.Distinct().ToList();
        
        Assert.That(keys.Count, Is.EqualTo(distinctKeys.Count), 
            $"Found duplicate keys in costs dictionary. Total: {keys.Count}, Distinct: {distinctKeys.Count}");
    }

    [Test]
    public void AllFeatherCostsArePositive()
    {
        var command = new AnankeCommand();
        var costsField = typeof(AnankeCommand).GetField("costs", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var costs = costsField.GetValue(null) as System.Collections.Generic.Dictionary<string, (double feathers, long unlockCost)>;
        
        foreach (var kvp in costs)
        {
            Assert.That(kvp.Value.feathers, Is.GreaterThan(0), 
                $"Item {kvp.Key} has non-positive feather cost: {kvp.Value.feathers}");
        }
    }

    [Test]
    public void UnlockCostsAreNonNegative()
    {
        var command = new AnankeCommand();
        var costsField = typeof(AnankeCommand).GetField("costs", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var costs = costsField.GetValue(null) as System.Collections.Generic.Dictionary<string, (double feathers, long unlockCost)>;
        
        foreach (var kvp in costs)
        {
            Assert.That(kvp.Value.unlockCost, Is.GreaterThanOrEqualTo(0), 
                $"Item {kvp.Key} has negative unlock cost: {kvp.Value.unlockCost}");
        }
    }
}
