namespace Coflnet.Sky.Commands.MC.Tasks;

public class TaskResult
{
    public int ProfitPerHour { get; set; }
    public string Message { get; set; } = "No detailed instructions available.";
    public string Details { get; set; }
    public string OnClick { get; set; }
    /// <summary>
    /// Indicates if the task is mostly passive, meaning it can be large in parallel to others (requiring mostly waiting)
    /// </summary>
    public bool MostlyPassive { get; set; }
}
