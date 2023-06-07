using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Figgle;
using Newtonsoft.Json;
using WebSocketSharp;

namespace Coflnet.Sky.Commands.MC
{
    public class CaptchaGenerator
    {
        private static Random random = new();
        static Figgle.FiggleFont[] readableFonts = new[] {
            FiggleFonts.Diamond, FiggleFonts.Contrast, FiggleFonts.BarbWire, FiggleFonts.Colossal, FiggleFonts.Banner4, FiggleFonts.Banner3,
            FiggleFonts.Banner, FiggleFonts.Arrows, FiggleFonts.AmcTubes, FiggleFonts.Acrobatic, FiggleFonts.Alligator, FiggleFonts.Alligator2,
            FiggleFonts.Alligator3, FiggleFonts.Alphabet, FiggleFonts.AmcAaa01, FiggleFonts.AmcSlash, FiggleFonts.AmcSlder
        };

        public ChatPart[] SetupChallenge(IMinecraftSocket socket, CaptchaInfo info)
        {
            // hello there, you found where I generate questions
            // feel free to look at the implementation and create solvers
            // I am gonna make it more complicated when someone actually breaks it :)
            using var captchaSpan = socket.CreateActivity("newCaptcha");
            CaptchaChallenge challenge = random.Next(0, 4000) switch
            {
                > 2 => AsciBaded(socket),
                0 => MinMax(socket),
                1 => ColorBased(socket),
                _ => MathBased(socket)
            };

            captchaSpan?.Log(JsonConvert.SerializeObject(new { info.CurrentSolutions, challenge.Options, challenge.Correct }, Formatting.Indented));

            info.CurrentSolutions = challenge.Correct.Select(c => c.Code).ToList();
            info.LastGenerated = DateTime.UtcNow;
            var captchaType = socket.AccountInfo.CaptchaType;
            return new DialogBuilder().LineBreak()
                .ForEach(challenge.Options, (d, o) => d.CoflCommand<CaptchaCommand>(o.Text, o.Code, o.Text)).Break
                .MsgLine($"{challenge.Question}", null, "anti macro question, please click on the answer")
                .If(() => captchaType != "vertical", db => db.LineBreak()
                            .CoflCommand<CaptchaCommand>(McColorCodes.AQUA + "Vertical |", "vertical",
                                $"{McColorCodes.GREEN}Use vertical captcha \n{McColorCodes.GRAY}this will print the letters below one another\n"
                                + "and helps if the green lines don't match up\nbecause you use a different font\n(you may need to solve one more captcha)"))
                .If(() => captchaType == "vertical", db => db.CoflCommand<CaptchaCommand>("Big captcha", "big", "Use horizontal captcha"))
                .CoflCommand<CaptchaCommand>(McColorCodes.ITALIC + " Another", "another", "Too difficult?\nGet another captcha")
                .If(() => captchaType == "vertical", db => db.CoflCommand<CaptchaCommand>(McColorCodes.LIGHT_PURPLE + " I use optifine", "optifine",
                        McColorCodes.GREEN + "The green lines don't allign \nand you use optifine?\ntry this :) or one of the\noptions to the left"))
                    .CoflCommand<VoidCommand>(" ", " ");
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
            var lines = RenderCharLines(letter, socket.AccountInfo);
            var chars = new List<List<Option>>();
            chars.Add(lines);
            var index = 0;
            while (chars.Sum(c => c.First().Text.Length) < 69)
                chars.Add(RenderCharLines(alphaBet[index++], socket.AccountInfo));

            var challenge = new CaptchaChallenge()
            {
                Question = "Select the letter " + McColorCodes.AQUA + letter +
                    $"\n{McColorCodes.GRAY}Click what looks the most like the letter " + McColorCodes.AQUA + letter
            };
            var bigger = chars.Max(l => l.Count);
            chars = chars.OrderBy(r => random.Next()).ToList();
            List<Option> parts = new();
            var vertical = socket.AccountInfo.CaptchaType == "vertical";
            HashSet<Option> solutions = new();
            if (!vertical)
                for (int i = 0; i < bigger; i++)
                {
                    if (i != 0)
                        parts.Add(new() { Text = McColorCodes.GREEN + "|\n" });
                    foreach (var item in chars)
                    {
                        AddLineOrEmpty(item, parts, i, lines, solutions);
                    }
                    if (chars.All(c => c.Count <= i || string.IsNullOrWhiteSpace(c[i].Text)))
                        break;
                }
            else
                foreach (var letterAsci in chars)
                {
                    for (int i = 0; i < letterAsci.Count; i++)
                    {
                        AddLineOrEmpty(letterAsci, parts, i, lines, solutions);
                        parts.AddRange(AddParts("".PadLeft(random.Next(2, 8))));
                        parts.Add(new() { Text = "\n" });
                    }
                }
            challenge.Correct = solutions;
            challenge.Options = parts;
            return challenge;
        }

        private void AddLineOrEmpty(List<Option> letterAsci, List<Option> parts, int i, List<Option> lines, HashSet<Option> solutions)
        {
            foreach (var item in GetSplitParts(letterAsci, i))
            {
                parts.Add(item);
                if (letterAsci == lines)
                    solutions.Add(item);
            }
        }

        private static IEnumerable<Option> GetSplitParts(List<Option> lines, int i)
        {
            if (lines.Count > i && !string.IsNullOrWhiteSpace(lines[i].Text))
                return AddParts(lines[i].Text);
            else
            {
                var length = lines.Where(l => l.Text.Length > 1).Max(l => l.Text.Length - (l.Text.Count(c => c == '´' || c == '!' || c == '|' || c == '.') / 2 + l.Text.Count(c => c == ';') / 3));
                var padding = "".PadLeft(length);
                if (Random.Shared.Next(6) == 0)
                    padding = padding.Remove(1, 1).Insert(Random.Shared.Next(0, length - 1), "🇧🇾".First().ToString());
                return AddParts(padding);
            }
        }

        private static IEnumerable<Option> AddParts(string padding)
        {
            foreach (var item in Split(padding, random.Next(2, 5)))
            {
                if (item.IsNullOrEmpty())
                {
                    Console.WriteLine("part of " + padding);
                    continue;
                }
                var piece = item;
                if (Random.Shared.Next(3) == 0)
                    piece = item.Insert(Random.Shared.Next(0, item.Length - 1), string.Join(null, Enumerable.Range(0, random.Next(1, 10)).Select(x => "🇧🇾")));
                yield return new() { Text = piece };
            }
        }

        static IEnumerable<string> Split(string str, int chunkSize)
        {
            var currentIndex = 0;
            do
            {
                var length = Math.Min(random.Next(1, 5), str.Length - currentIndex);
                yield return str.Substring(currentIndex, length);
                currentIndex += length;
            }
            while (str.Length > currentIndex);
        }

        private List<Option> RenderCharLines(char letter, AccountInfo info)
        {
            var selectedRenderer = readableFonts.OrderBy(r => Random.Shared.Next()).First();
            var rendered = selectedRenderer.Render(letter.ToString());

            var builder = new System.Text.StringBuilder(rendered.Length);
            var hasSpaceEnd = rendered.Split('\n').All(l => string.IsNullOrEmpty(l) || l.Last() == ' ');
            var last = ' ';
            foreach (var item in rendered)
            {
                var lastAtStart = last;
                if (!hasSpaceEnd && item == '\n')
                    if (random.Next(0, 20) == 0)
                        last = WriteDot(builder, last);
                    else
                        builder.Append(' ');
                if (item == '\n')
                    builder.Append(item);
                else if (item == ' ')
                    if (random.Next(0, 50) == 0)
                        last = WriteDot(builder, last);
                    else
                        builder.Append(' ');
                else if (item == ':' || item == '\'' || item == '.')
                    builder.Append("´´");
                else
                {
                    last = WriteDot(builder, last);
                }
                if (lastAtStart == last)
                    last = item;
            }
            string[] lines = null;
            if (info.CaptchaType == "optifine")
            {
                builder.Replace("´", ".");

                lines = builder.ToString().Split("\n").ToArray();
            }
            else
                lines = builder.ToString().Split('\n');
            return lines.Select(l => new Option()
            {
                Text = l,
                Hover = letter.ToString() + " " + selectedRenderer.GetType().Name
            }).ToList();
        }

        private static char WriteDot(StringBuilder builder, char last)
        {
            if (random.Next(0, 20) == 0)
                builder.Append("!!");
            else if (random.Next(0, 40) == 0)
                builder.Append(" ");
            else if (last != "🇧🇾"[0])
            {
                builder.Append("🇧🇾"[1]);
            }
            else
            {
                builder.Append("🇧🇾"[0]);
                last = "🇧🇾"[0];
            }

            return last;
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
