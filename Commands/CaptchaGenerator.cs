using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;
using OpenTracing;
using WebSocketSharp;

namespace Coflnet.Sky.Commands.MC
{
    public class CaptchaGenerator
    {
        private static Random random = new();

        public ChatPart[] SetupChallenge(MinecraftSocket socket, SessionInfo info)
        {
            // hello there, you found where I generate questions
            // feel free to look at the implementation and create solvers
            // I am gonna make it more complicated when someone actually breaks it :)
            var captchaSpan = socket?.tracer.BuildSpan("newCaptcha").AsChildOf(socket.ConSpan).StartActive();
            CaptchaChallenge challenge = random.Next(0, 4) switch
            {
                0 => MinMax(socket),
                1 => ColorBased(socket),
                _ => MathBased(socket)
            };


            captchaSpan?.Span.Log(JsonConvert.SerializeObject(new { info.CaptchaSolution, challenge.Options, challenge.Correct }, Formatting.Indented));

            info.CaptchaSolution = challenge.Correct.Code;
            return new DialogBuilder()
                .MsgLine($"{challenge.Question} (click correct answer)", null, "anti macro question, please click on the answer")
                .ForEach(challenge.Options, (d, o) => d.CoflCommand<CaptchaCommand>(o.Text, o.Code, "Click to select " + o.Text));
        }

        private CaptchaChallenge MinMax(MinecraftSocket socket)
        {
            var numbers = new List<int>();
            for (var i = 0; i < 6; i++)
                numbers.Add(random.Next(1, 100));

            var transformed = numbers.Select(n => new
            {
                n,
                s = CreateOption(random.Next() % 2 == 1 ? n.ToString() : NumberToWords(n))
            }).ToArray();

            var d = "highest";
            var correct = transformed.MaxBy(t => t.n).s;
            if (random.Next() % 2 == 1)
            {
                d = "lowest";
                correct = transformed.MinBy(t => t.n).s;
            }

            return new()
            {
                Question = $"What is the {McColorCodes.AQUA}{McColorCodes.BOLD}{d}{McColorCodes.RESET} of these numbers?",
                Options = transformed.Select(t => t.s).ToArray(),
                Correct = correct
            };
        }


        private CaptchaChallenge ColorBased(MinecraftSocket socket)
        {
            var colors = new Dictionary<string, string>{
                { "red", McColorCodes.RED},
                { "green", McColorCodes.GREEN},
                { "bright yellow", McColorCodes.YELLOW},
                { "blue", McColorCodes.BLUE},
                { "gray", McColorCodes.GRAY},
                { "white", McColorCodes.WHITE},
                { "purple", McColorCodes.DARK_PURPLE},
                { "gold/orange", McColorCodes.GOLD}
            };
            
            var transformed = colors.OrderBy(c=>random.Next()).Select(c => new
            {
                c,
                s = CreateOption(c.Key)
            }).ToArray();
            var correct = transformed.First();

            return new()
            {
                Question = $"{correct.c.Value}What is the color of this message?",
                Options = transformed.Select(t => t.s).ToArray(),
                Correct = correct.s
            };
        }

        private CaptchaChallenge MathBased(MinecraftSocket socket)
        {
            var numbers = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }.OrderBy(a => random.Next()).ToList();
            var altFonts = new string[] {
                "1ϩӠ4Ƽ67890", "①②③④⑤⑥⑦⑧⑨0",  "１２３４５６７８９０", "⑴⑵⑶⑷⑸⑹⑺⑻⑼0",
                //"₁₂₃₄₅₆₇₈₉₀", "➊➋➌➍➎➏➐➑➒⓪", "¹²³⁴⁵⁶⁷⁸⁹⁰", "➀➁➂➃➄➅➆➇➈0" 
                }; // 𝟙५
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

            var correct = CreateOption(solution.ToString());
            var options = numbers.Skip(2).Where(n => n != solution).Take(5).Select(o => CreateOption(o.ToString())).Append(correct).OrderBy(s => random.Next());

            var secondAsString = altFonts.OrderBy(f => random.Next()).First()[(second + 9) % 10];
            var firstAsString = NumberToWords(first);
            var question = $"What is {McColorCodes.AQUA}{firstAsString} {McColorCodes.GRAY}{word} {McColorCodes.AQUA}{secondAsString}{McColorCodes.GRAY}";
            Console.WriteLine(question);

            return new CaptchaChallenge()
            {
                Question = question,
                Options = options.ToArray(),
                Correct = correct
            };
        }

        private Option CreateOption(string o)
        {
            return new Option() { Text = $"{McColorCodes.DARK_GRAY} > {McColorCodes.YELLOW}{o}\n" };
        }

        private static string GetCode()
        {
            return random.NextInt64().ToString("X");
        }

        public class Option
        {
            public string Text;
            public string Code = GetCode();
        }

        public class CaptchaChallenge
        {
            public IEnumerable<Option> Options;
            public Option Correct;
            public string Question;
        }

        /// <summary>
        /// Nice answer from https://stackoverflow.com/a/2730393
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public static string NumberToWords(int number)
        {
            if (number == 0)
                return "zero";

            if (number < 0)
                return "minus " + NumberToWords(Math.Abs(number));

            string words = "";

            if ((number / 1000000) > 0)
            {
                words += NumberToWords(number / 1000000) + " million ";
                number %= 1000000;
            }

            if ((number / 1000) > 0)
            {
                words += NumberToWords(number / 1000) + " thousand ";
                number %= 1000;
            }

            if ((number / 100) > 0)
            {
                words += NumberToWords(number / 100) + " hundred ";
                number %= 100;
            }

            if (number > 0)
            {
                if (words != "")
                    words += "and ";

                var unitsMap = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
                var tensMap = new[] { "zero", "ten", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

                if (number < 20)
                    words += unitsMap[number];
                else
                {
                    words += tensMap[number / 10];
                    if ((number % 10) > 0)
                        words += "-" + unitsMap[number % 10];
                }
            }

            return words;
        }
    }
}
