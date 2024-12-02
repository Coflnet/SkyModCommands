using System;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Tutorials;

namespace Coflnet.Sky.Commands.MC;

public class AdventCommand : McCommand
{
    public override bool IsPublic => DateTime.UtcNow.Month >= 11;

    private static bool InitRan = false;
    Table<AdventAnswer> answers;

    public AdventCommand()
    {
    }

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (!InitRan)
        {
            await RunInit();
        }
        var currentDay = DateTime.UtcNow.Day;
        var registeredAnswer = await answers.Where(a => a.UserId == socket.UserId && a.Day == currentDay).FirstOrDefault().ExecuteAsync();
        if (registeredAnswer != null)
        {
            socket.Dialog(db => db.MsgLine("You already answered todays question with " + registeredAnswer.Answer));
            if (registeredAnswer.Correct)
            {
                // redo in case it didn't work
                try
                {
                    await HandOutReward(socket, currentDay);
                }
                catch (Exception)
                {
                    // swollow dupplicate exception
                }
            }
            return;
        }
        if (DateTime.UtcNow.Month != 12)
        {
            socket.Dialog(db => db.MsgLine("The adventcalendar is only available in December, try on December 1st"));
            return;
        }
        if (currentDay > questions.Length)
        {
            socket.Dialog(db => db.MsgLine("Advent is over, Merry Christmas!", null, "Come back next year!"));
            return;
        }
        var today = questions[currentDay - 1];
        var correctAnswer = today.CorrectAnswer;
        var wrongAnswers = today.WrongAnswers;
        var answerChosen = Convert<string>(arguments);
        if (!string.IsNullOrWhiteSpace(answerChosen) && (correctAnswer == answerChosen || wrongAnswers.Contains(answerChosen)))
        {
            var correct = correctAnswer == answerChosen;
            await answers.Insert(new AdventAnswer { UserId = socket.UserId, Day = currentDay, Answer = answerChosen, Correct = correct }).ExecuteAsync();
            if (!correct)
            {
                socket.Dialog(db => db.MsgLine("Wrong answer! The correct answer was " + correctAnswer));
                return;
            }
            socket.Dialog(db => db.MsgLine("Correct!"));
            await HandOutReward(socket, currentDay);
            return;
        }
        var allAnswers = wrongAnswers.Append(correctAnswer).OrderBy(a => Guid.NewGuid()).ToArray();
        socket.Dialog(db => db.MsgLine(today.QuestionText).ForEach(allAnswers, (db, a, i) => db.CoflCommand<AdventCommand>($" {McColorCodes.GRAY}{i + 1} {McColorCodes.AQUA}{a}\n", a, $"Click to answer\n{a}"))
            .Break.ForEach(allAnswers, (db, a, i) => db.CoflCommand<AdventCommand>($"{McColorCodes.GRAY}Take {McColorCodes.AQUA}{i + 1} ", a, $"Click to answer\n{McColorCodes.AQUA}{a}\nand not risk chat scrolling")));
        if (currentDay <= 2)
        {
            socket.Dialog(db => db.MsgLine($"Before you answer! Would you be interested in {McColorCodes.AQUA}10x {McColorCodes.RESET}the reward for {McColorCodes.GOLD}1k CoflCoins{McColorCodes.RESET}? Thats {McColorCodes.GOLD}50 {McColorCodes.RESET}instead of {McColorCodes.AQUA}5{McColorCodes.RESET} each day")
                .CoflCommand<PurchaseCommand>("Yes", "advent-calendar", "Click to buy 10x reward for 10x price advent calendar"));
        }
        await socket.TriggerTutorial<AdventCalendarTutorial>();
    }

    private static async Task HandOutReward(MinecraftSocket socket, int currentDay)
    {
        var userApi = DiHandler.GetService<IUserApi>();
        var ownsCalendar = await userApi.UserUserIdOwnsProductSlugUntilGetAsync(socket.UserId, "advent-calendar");
        var reward = 5;
        if (ownsCalendar > DateTime.UtcNow)
        {
            reward = 50;
        }
        var topupApi = DiHandler.GetService<ITopUpApi>();
        await topupApi.TopUpCustomPostAsync(socket.UserId, new()
        {
            Amount = reward,
            ProductId = "advent-reward",
            Reference = $"{DateTime.UtcNow.Year}-{currentDay}"
        });
    }

    private async Task RunInit()
    {
        InitRan = true;
        var mapping = new MappingConfiguration().Define(
                new Map<AdventAnswer>()
                    .PartitionKey(f => f.Day)
                    .ClusteringKey(f => f.UserId)
            );
        answers = new Table<AdventAnswer>(DiHandler.GetService<ISession>(), mapping);
        await answers.CreateIfNotExistsAsync();
    }

    public Question[] questions = [
        new() { QuestionText = "Whats the biggest GameMode on hypixel?", CorrectAnswer = "Skyblock", WrongAnswers = ["Bedwars", "Skywars"] },
        new() { QuestionText = "How can you change item description", CorrectAnswer = "/cofl lore", WrongAnswers = ["/cofl desc", "/cofl itemdesc"] },
        new() { QuestionText = "For 1,6,5,3,4 what is the median?", CorrectAnswer = "4", WrongAnswers = ["3", "5", "6"] },
        new() { QuestionText = "Whats the short version to write to cofl chat?", CorrectAnswer = "/fc", WrongAnswers = ["/cc", "/cofl message", "/c"] },
        new() { QuestionText = "How can you show an item to another mod user?", CorrectAnswer = "/cofl shareitem", WrongAnswers = ["/cofl showitem", "/cofl item"] },
        new() { QuestionText = "How can you check your ping to the Skycofl servers?", CorrectAnswer = "/cofl ping", WrongAnswers = ["/cofl latency", "/cofl speed"] },
        new() { QuestionText = "When will the forest island release", CorrectAnswer = "In 2-3 business days", WrongAnswers = ["yesterday", "2 days ago"] },
        new() { QuestionText = "What command allows you to store copies of your config", CorrectAnswer = "/cofl backup", WrongAnswers = ["/cofl save", "/cofl store"] },
        new() { QuestionText = "What command should you execute when you encounter an error?", CorrectAnswer = "/cofl report", WrongAnswers = ["/cofl error", "/cofl bug"] },
        new() { QuestionText = "If you don't see any flips you execute?", CorrectAnswer = "/cofl blocked", WrongAnswers = ["/cofl noflips", "/cofl noflip"] },
        new() { QuestionText = "What command allows you to change your chat name?", CorrectAnswer = "/cofl nick", WrongAnswers = ["/cofl name", "/cofl chatname"] },
        new() { QuestionText = "How can you check the speed of a flip?", CorrectAnswer = $"{McColorCodes.AQUA}/cofl time {McColorCodes.WHITE}or flip menu", WrongAnswers = [$"{McColorCodes.AQUA}/cofl speed {McColorCodes.WHITE}or flip menu", $"{McColorCodes.AQUA}/cofl flip speed"] },
        new() { QuestionText = "How can you see how much profit you made?", CorrectAnswer = "/cofl profit", WrongAnswers = ["/cofl money", "/cofl earnings"] },
        new() { QuestionText = "If a flip is tracked incorrectly how can you reload it?", CorrectAnswer = "/cofl loadfliphistory", WrongAnswers = ["/cofl reloadflip", "/cofl reload", "/cofl correct"] },
        new() { QuestionText = "How do you see the flipper with the most profit?", CorrectAnswer = "/cofl leaderboard", WrongAnswers = ["/cofl mostprofit", "/cofl topflipper"] },
        new() { QuestionText = "What year did Skyblock release", CorrectAnswer = "2019", WrongAnswers = ["2020", "2018", "2021"] },
        new() { QuestionText = "How can you see available emojis?", CorrectAnswer = "/cofl emoji", WrongAnswers = ["/cofl emotes", "/cofl smileys"] },
        new() { QuestionText = "How can you shedule a reminder?", CorrectAnswer = "/cofl addreminder", WrongAnswers = ["/cofl reminder", "/cofl remind"] },
        new() { QuestionText = "How can you toggle flips on or of?", CorrectAnswer = "/cofl flip", WrongAnswers = ["/cofl toggle", "/cofl flips"] },
        new() { QuestionText = "Where can you join kuudra", CorrectAnswer = "crimson isle", WrongAnswers = ["forest isle", "desert isle"] },
        new() { QuestionText = "What command allows you to see bazaar flips?", CorrectAnswer = "/cofl bazaar", WrongAnswers = ["/cofl bazaarflips", "/cofl bazaarflip"] },
        new() { QuestionText = "What command shows you profitable crafts?", CorrectAnswer = "/cofl craft", WrongAnswers = ["/cofl craftflip", "/cofl craftables"] },
        new() { QuestionText = "How can you set your timezone?", CorrectAnswer = "/cofl settimezone", WrongAnswers = ["/cofl timezone", "/cofl time"] },
        new() { QuestionText = "What command lists the cheapest museum exp?", CorrectAnswer = "/cofl cm", WrongAnswers = ["/cofl cheapestmuseum", "/cofl museumexp"] },
        new() { QuestionText = "What command shows you your daily trade limits?", CorrectAnswer = "/cofl limits", WrongAnswers = ["/cofl tradelimits", "/cofl trade"] },
    ];

    public class AdventAnswer
    {
        public string UserId { get; set; }
        public int Day { get; set; }
        public string Answer { get; set; }
        public bool Correct { get; set; }
    }

    public class Question
    {
        public string QuestionText { get; set; }
        public string CorrectAnswer { get; set; }
        public string[] WrongAnswers { get; set; }
    }
}
