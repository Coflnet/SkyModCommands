using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public static class ItemConversionHelpers
    {
        public static SaveAuction ConvertToAuction(PlayerState.Client.Model.Item item)
        {
            var auction = new SaveAuction()
            {
                Tag = item.Tag,
                ItemName = item.ItemName,
                Enchantments = item.Enchantments?.Select(e => new Enchantment()
                {
                    Type = Enum.Parse<Enchantment.EnchantmentType>(e.Key, true),
                    Level = (byte)e.Value
                }).ToList() ?? new List<Enchantment>(),
                Count = item.Count ?? 1,
            };

            if (item.ExtraAttributes == null)
                throw new CoflnetException("invalid item", "The item attributes could not be read, please open your inventory and try again a few seconds after");

            auction.SetFlattenedNbt(NBT.FlattenNbtData(item.ExtraAttributes));
            auction.Tier = (Tier)int.Parse(item.ExtraAttributes.GetValueOrDefault("tier", 0).ToString());
            return auction;
        }
    }
}
