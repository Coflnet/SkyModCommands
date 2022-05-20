using System;
using System.Collections.Generic;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.ModCommands.Models
{
    public class BackupEntry
    {
        public string Name;
        public FlipSettings settings;
        public DateTime CreationDate = DateTime.Now;

        public override bool Equals(object obj)
        {
            return obj is BackupEntry entry &&
                   Name == entry.Name &&
                   CreationDate == entry.CreationDate;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, CreationDate);
        }
    }
}