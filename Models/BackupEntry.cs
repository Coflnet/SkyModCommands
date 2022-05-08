using System;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.ModCommands.Models
{
    public class BackupEntry
    {
        public string Name;
        public FlipSettings settings;
        public DateTime CreationDate = DateTime.Now;
    }
}