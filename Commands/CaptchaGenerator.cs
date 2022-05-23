using System;
using System.Linq;
using Coflnet.Sky.ModCommands.Dialogs;
using WebSocketSharp;

namespace Coflnet.Sky.Commands.MC
{
    public class CaptchaGenerator
    {
        private Random random = new();

        public ChatPart[] SetupChallenge(SessionInfo info)
        {
            // hello there, you found where I generate questions
            // feel free to look at the implementation and create solvers
            // I am gonna make it more complicated when someone actually breaks it :)
            var numbers = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }.OrderBy(a => random.Next()).ToList();
            var first = numbers.First();
            var second = numbers.Skip(1).First();
            var word = new string[] { "added to", "plus", "+", "and" }.OrderBy(a => random.Next()).First();
            var solution = first + second;
            if (solution > 9)
            {
                word = new string[] { "minus", "less", "-" }.OrderBy(a => random.Next()).First();
                var bigger = Math.Max(first, second);
                var smaler = Math.Min(first, second);
                first = bigger;
                second = smaler;
                solution = bigger - smaler;
            }

            var correct = CreateOption(solution);
            var options = numbers.Skip(2).Where(n => n != solution).Take(5).Select(o => CreateOption(o)).Append(correct).OrderBy(s => random.Next());
            info.CaptchaSolution = correct.Code;
            return new DialogBuilder()
                .MsgLine($"What is {McColorCodes.AQUA}{first} {McColorCodes.GRAY}{word} {McColorCodes.AQUA}{second}{McColorCodes.GRAY}", null, "anti macro question, please click on the answer")
                .ForEach(options, (d, o) => d.CoflCommand<CaptchaCommand>(o.Text, o.Code, "Click to select " + o.Text));
        }

        private Option CreateOption(int o)
        {
            return new Option() { Text = $"{McColorCodes.DARK_GRAY} > {McColorCodes.YELLOW}{o}\n", Code = GetCode() };
        }

        private string GetCode()
        {
            return random.NextInt64().ToString("X");
        }

        public class Option
        {
            public string Text;
            public string Code;
        }
    }
}
