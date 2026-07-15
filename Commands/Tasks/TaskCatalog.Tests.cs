using System;
using System.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class TaskCatalogTests
{
    [Test]
    public void EveryProfitTaskIsRegisteredExactlyOnce()
    {
        var registered = TaskCatalog.Create().Values.ToList();
        var registeredTypes = registered.Select(t => t.GetType()).ToList();
        var allTaskTypes = typeof(ProfitTask).Assembly.GetTypes()
            .Where(t => typeof(ProfitTask).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var missing = allTaskTypes.Except(registeredTypes).Except(TaskCatalog.IntentionallyUnregistered).ToList();
        Assert.That(missing, Is.Empty,
            "Tasks neither registered in TaskCatalog nor listed as IntentionallyUnregistered: "
            + string.Join(", ", missing.Select(t => t.Name)));

        var duplicates = registeredTypes.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key.Name).ToList();
        Assert.That(duplicates, Is.Empty,
            "Tasks registered more than once in TaskCatalog: " + string.Join(", ", duplicates));

        var contradicting = registeredTypes.Intersect(TaskCatalog.IntentionallyUnregistered).Select(t => t.Name).ToList();
        Assert.That(contradicting, Is.Empty,
            "Tasks both registered and listed as IntentionallyUnregistered: " + string.Join(", ", contradicting));
    }

    [Test]
    public void TaskNamesAreUnique()
    {
        var names = TaskCatalog.Create().Values.Distinct().Select(t => t.Name).ToList();
        var duplicates = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.That(duplicates, Is.Empty,
            "Duplicate task names (breaks taskdetails lookup): " + string.Join(", ", duplicates));
    }
}
