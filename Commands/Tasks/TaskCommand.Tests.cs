using AwesomeAssertions;
using Coflnet.Sky.PlayerState.Client.Model;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Covers TaskCommand.PrepareTaskResult itself (the command layer's own rewrite of a task's
/// OnClick to the taskdetails drill-down) - the underlying task definitions are covered by
/// SkyUserState's own MethodDetection.Tests.cs, which this used to duplicate.
/// </summary>
public class TaskCommandTests
{
    [Test]
    public void PrepareTaskResult_RewritesClickToDetails_AndPreservesPrimaryAction()
    {
        var result = TaskCommand.PrepareTaskResult(new TaskResult
        {
            Name = "Forge",
            OnClick = "/warp forge"
        });

        result.OnClick.Should().Be("/cofl taskdetails Forge");
        result.PrimaryAction.Should().Be("/warp forge");
    }
}
