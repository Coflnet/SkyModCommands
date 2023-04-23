using System.Collections.Generic;
using Coflnet.Sky.Commands.MC;
using System.Linq;
using Coflnet.Sky.Commands.Shared;
using System;

namespace Coflnet.Sky.ModCommands.Dialogs
{
    public class DialogBuilder
    {
        private List<ChatPart> Parts = new List<ChatPart>() { new ChatPart(MinecraftSocket.COFLNET) };

        private string CurrentUrl;

        public static DialogBuilder New => new DialogBuilder();

        public DialogBuilder Msg(string message, string onClick = null, string hover = null)
        {
            Parts.Add(new ChatPart(message, onClick == null ? CurrentUrl : onClick, hover));
            return this;
        }
        public DialogBuilder MsgLine(string message, string onClick = null, string hover = null)
        {
            Parts.Add(new ChatPart(message + "\n", onClick == null ? CurrentUrl : onClick, hover));
            return this;
        }
        public DialogBuilder ForEach<T>(IEnumerable<T> collection, Action<DialogBuilder,T> action)
        {
            foreach (var item in collection)
            {
                action(this, item);
            }
            return this;
        }
        public DialogBuilder ForEach<T>(IEnumerable<T> collection, Action<DialogBuilder,T, int> action)
        {
            int i = 0;
            foreach (var item in collection)
            {
                action(this, item, i++);
            }
            return this;
        }

        public DialogBuilder LineBreak()
        {
            Parts.Add(new ChatPart("\n"));
            return this;
        }
        public DialogBuilder Lines(params string[] lines)
        {
            foreach (var item in lines)
            {
                Parts.Add(new ChatPart(item + "\n"));
            }
            return this;
        }

        public DialogBuilder Break => LineBreak();

        public DialogBuilder BlockCommands()
        {
            GetLastPart().onClick = null;
            return this;
        }

        public DialogBuilder SeparatorLine()
        {
            Parts.Add(new ChatPart("---------------------------------\n"));
            return this;
        }

        /// <summary>
        /// Sets the default action for next parts
        /// </summary>
        /// <param name="onClick"></param>
        /// <returns></returns>
        public DialogBuilder SetDefaultAction(string onClick)
        {
            CurrentUrl = onClick;
            return this;
        }
        /// <summary>
        /// Makes the color of the last part gray 
        /// </summary>
        /// <returns></returns>
        public DialogBuilder AsGray()
        {
            var last = GetLastPart();
            last.text = McColorCodes.GRAY + last.text;
            return this;
        }

        /// <summary>
        /// Creates a type safe onclick to another dialog
        /// </summary>
        /// <param name="message"></param>
        /// <param name="context"></param>
        /// <param name="hover"></param>
        /// <typeparam name="TDialog"></typeparam>
        /// <returns></returns>
        public DialogBuilder DialogLink<TDialog>(string message, string context, string hover = null) where TDialog : Dialog
        {
            var dialogName = ClassNameDictonary<Dialog>.GetCleardName<TDialog>();// typeof(TDialog).Name.Replace(typeof(Dialog).Name,"");
            return CoflCommand<DialogCommand>(message, $"{dialogName} {context}", hover);
        }


        public DialogBuilder If(Func<bool> condition, Action<DialogBuilder> builder)
        {
            if(condition())
                builder(this);
            return this;
        }

        /// <summary>
        /// Creates a onclik to another command
        /// </summary>
        /// <param name="message"></param>
        /// <param name="context"></param>
        /// <param name="hover"></param>
        /// <typeparam name="TCom"></typeparam>
        /// <returns></returns>
        public DialogBuilder CoflCommand<TCom>(string message, string context, string hover = null) where TCom : McCommand
        {
            var comandName = ClassNameDictonary<TCom>.GetCleardName<TCom>();
            return Msg(message, $"/cofl {comandName} {context}", hover);
        }



        protected ChatPart GetLastPart()
        {
            return Parts.Last();
        }

        public ChatPart[] Build()
        {
            return Parts.ToArray();
        }



        public static implicit operator ChatPart[](DialogBuilder input)
        {
            return input.Build();
        }
    }
}
