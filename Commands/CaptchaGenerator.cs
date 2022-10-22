using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;
using OpenTracing;
using WebSocketSharp;

namespace Coflnet.Sky.Commands.MC
{
    public class CaptchaGenerator
    {
        private static Random random = new();

        public ChatPart[] SetupChallenge(IMinecraftSocket socket, SessionInfo info)
        {
            // hello there, you found where I generate questions
            // feel free to look at the implementation and create solvers
            // I am gonna make it more complicated when someone actually breaks it :)
            using var captchaSpan = socket.tracer?.BuildSpan("newCaptcha").AsChildOf(socket.ConSpan).StartActive();
            CaptchaChallenge challenge = random.Next(0, 4000) switch
            {
                > 2 => AsciBaded(socket),
                0 => MinMax(socket),
                1 => ColorBased(socket),
                _ => MathBased(socket)
            };


            captchaSpan?.Span.Log(JsonConvert.SerializeObject(new { info.CaptchaSolutions, challenge.Options, challenge.Correct }, Formatting.Indented));

            info.CaptchaSolutions = challenge.Correct.Select(c => c.Code).ToList();
            return new DialogBuilder()
                .MsgLine($"{challenge.Question} (click correct answer)", null, "anti macro question, please click on the answer")
                .ForEach(challenge.Options, (d, o) => d.CoflCommand<CaptchaCommand>(o.Text, o.Code, "Click to select " + o.Text))
                .If(() => info.ChatWidth > 20, db => db.CoflCommand<CaptchaCommand>("Small chat", "small", "Use small chat (you will need to solve one more)"))
                .If(() => info.ChatWidth <= 20, db => db.CoflCommand<CaptchaCommand>("Big captcha", "big", "Use big chat"));
        }

        private CaptchaChallenge MinMax(IMinecraftSocket socket)
        {
            var numbers = new List<int>();
            while (numbers.Count < 6)
            {
                var number = random.Next(0, 100);
                if (!numbers.Contains(number))
                    numbers.Add(number);
            }

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
                Correct = new Option[] { correct }
            };
        }

        private CaptchaChallenge AsciBaded(IMinecraftSocket socket)
        {
            var alphaBet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".OrderBy(r => random.Next()).ToList();
            var letter = alphaBet.Last();
            var lines = RenderCharLines(letter);
            var chars = new List<List<Option>>();
            chars.Add(lines);
            var index = 0;
            while (chars.Sum(c => c.First().Text.Length) < 55)
                chars.Add(RenderCharLines(alphaBet[index++]));

            //socket.Dialog(db => db.LineBreak().Lines(lines.Select(m => m + "|").ToArray()));
            var challenge = new CaptchaChallenge() { Question = "Select the letter " + letter };
            var bigger = chars.Max(l => l.Count);
            chars = chars.OrderBy(r => random.Next()).ToList();
            List<Option> parts = new();
            var small = socket.SessionInfo.ChatWidth < 20;
            if (!small)
                for (int i = 0; i < bigger; i++)
                {
                    parts.Add(new() { Text = "|\n" });
                    foreach (var item in chars)
                    {
                        AddLineOrEmpty(item, parts, i);
                    }
                    if (chars.All(c => c.Count <= i || string.IsNullOrWhiteSpace(c[i].Text)))
                        break;
                }
            else
                foreach (var letterAsci in chars)
                {
                    for (int i = 0; i < letterAsci.Count; i++)
                    {
                        AddLineOrEmpty(letterAsci, parts, i);
                        parts.Add(new() { Text = "|\n" });
                    }
                }
            challenge.Correct = lines;
            challenge.Options = parts;
            return challenge;
        }

        private static void AddLineOrEmpty(List<Option> lines, List<Option> parts, int i)
        {
            if (lines.Count > i && !string.IsNullOrWhiteSpace(lines[i].Text))
                parts.Add(lines[i]);
            else
            {
                var length = lines.Where(l => l.Text.Length > 1).Max(l => l.Text.Length - l.Text.Count(c=>c== '´')/2);
                var padding = "".PadLeft(length);
                if (Random.Shared.Next(8) == 0)
                    padding = padding.Remove(1, 1).Insert(Random.Shared.Next(0, length - 1), "🇧🇾".First().ToString());
                else
                    padding = padding.Insert(Random.Shared.Next(0, length - 1), "🇧🇾");
                parts.Add(new() { Text = padding, Hover = "spacing" });
            }
        }

        private static List<Option> RenderCharLines(char letter)
        {
            // Figgle.FiggleFonts.BarbWire,Figgle.FiggleFonts.Banner4,Figgle.FiggleFonts.Banner3D, Figgle.FiggleFonts.Banner3, Figgle.FiggleFonts.Banner, Figgle.FiggleFonts.Arrows, Figgle.FiggleFonts.AmcTubes, Figgle.FiggleFonts.Acrobatic, Figgle.FiggleFonts.Alligator, Figgle.FiggleFonts.Alligator2, Figgle.FiggleFonts.Alligator3, Figgle.FiggleFonts.Alphabet, Figgle.FiggleFonts.AmcAaa01, Figgle.FiggleFonts.AmcSlash, Figgle.FiggleFonts.AmcSlder
            var readableFonts = new Figgle.FiggleFont[] {Figgle.FiggleFonts.Banner4, Figgle.FiggleFonts.Banner3, Figgle.FiggleFonts.Banner, Figgle.FiggleFonts.Arrows, Figgle.FiggleFonts.AmcTubes, Figgle.FiggleFonts.Acrobatic, Figgle.FiggleFonts.Alligator, Figgle.FiggleFonts.Alligator2, Figgle.FiggleFonts.Alligator3, Figgle.FiggleFonts.Alphabet, Figgle.FiggleFonts.AmcAaa01, Figgle.FiggleFonts.AmcSlash, Figgle.FiggleFonts.AmcSlder};
            var selectedRenderer = readableFonts.OrderBy(r => Random.Shared.Next()).First();
            var rendered = selectedRenderer.Render(letter.ToString());

            //Console.WriteLine(rendered);

            var builder = new System.Text.StringBuilder(rendered.Length);
            var hasSpaceEnd = rendered.Split('\n').All(l => string.IsNullOrEmpty(l) || l.Last() == ' ');
            foreach (var item in rendered)
            {
                if(!hasSpaceEnd && item == '\n')
                    builder.Append(' ');
                if (item == ' ' || item == '\n')
                    builder.Append(item);
                else if(item == ':' || item == '\'' || item == '.')
                    builder.Append("´´");
                else
                    builder.Append("🇧🇾".First());
            }
            var lines = builder.ToString().Split('\n');
            return lines.Select(l => new Option()
            {
                Text = l,
                Hover = letter.ToString() + " " + selectedRenderer.GetType().Name
            }).ToList();
        }

        private CaptchaChallenge ColorBased(IMinecraftSocket socket)
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

            var transformed = colors.OrderBy(c => random.Next()).Select(c => new
            {
                c,
                s = CreateOption(c.Key)
            }).ToArray();
            var correct = transformed.First();

            return new()
            {
                Question = $"{correct.c.Value}What is the color of this message?",
                Options = transformed.Select(t => t.s).OrderBy(s => random.Next()).ToArray(),
                Correct = new Option[] { correct.s }
            };
        }

        private CaptchaChallenge MathBased(IMinecraftSocket socket)
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
                word = new string[] { "minus", "subtract", "-", "reduced by" }.OrderBy(a => random.Next()).First();
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

            return new CaptchaChallenge()
            {
                Question = question,
                Options = options.ToArray(),
                Correct = new Option[] { correct }
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
            public string Hover;
        }

        public class CaptchaChallenge
        {
            public IEnumerable<Option> Options;
            public IEnumerable<Option> Correct;
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
