using System;

namespace Coflnet.Sky.ModCommands.Models;

public class PlayerActivity
{
    public string PlayerId { get; set; }
    public string MethodName { get; set; }
    public DateTime StartedAt { get; set; }
    public string Location { get; set; }
}
