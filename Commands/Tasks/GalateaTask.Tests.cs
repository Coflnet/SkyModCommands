using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Model;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC.Tasks;

public class GalateaTaskTests
{
    [Test]
    public async Task TestGalateaDivingTask()
    {
        var task = new GalateaDivingTask();
        var locationProfit = JsonConvert.DeserializeObject<Period[]>(sampleJson);
        var result = await task.Execute(new TaskParams
        {
            TestTime = new DateTime(2025, 7, 24, 17, 0, 0),
            ExtractedInfo = new PlayerState.Client.Model.ExtractedInfo(),
            Socket = new MinecraftSocket(),
            Cache = new ConcurrentDictionary<Type, TaskParams.CalculationCache>(),
            MaxAvailableCoins = 1000000000,
            LocationProfit = locationProfit.GroupBy(l => l.Location).ToDictionary(l => l.Key, l => l.ToArray())
        });
        result.Should().NotBeNull();
        result.ProfitPerHour.Should().BeGreaterThan(1_000_000);
        result.Details.Should().Contain("§eSHARD_DROWNED §7x209");
    }


    string sampleJson = """
    [
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 2079042,
    "startTime": "2025-07-24T17:39:47.309",
    "endTime": "2025-07-24T17:45:19.296",
    "itemsCollected": {
      "AGATHA_COUPON": 130,
      "DEEP_ROOT": 7,
      "ENCHANTED_MANGROVE_LOG": 84,
      "FORAGING_WISDOM_BOOSTER": 1,
      "MANGROVE_LOG": -46,
      "SHARD_PHANFLARE": 1,
      "SHARD_PHANPYRE": 6,
      "SWEEP_BOOSTER": 1,
      "VINESAP": 33
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 30222,
    "startTime": "2025-07-24T17:38:56.165",
    "endTime": "2025-07-24T17:39:45.89",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 20,
      "VINESAP": 9
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 33274,
    "startTime": "2025-07-24T17:38:13.015",
    "endTime": "2025-07-24T17:38:56.103",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 22,
      "VINESAP": 10
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 67672,
    "startTime": "2025-07-24T17:37:01.095",
    "endTime": "2025-07-24T17:38:10.898",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 44,
      "FIG_LOG": 474,
      "VINESAP": 19
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 24118,
    "startTime": "2025-07-24T17:36:09.377",
    "endTime": "2025-07-24T17:36:38.439",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 16,
      "VINESAP": 7
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 72652,
    "startTime": "2025-07-24T17:34:30.689",
    "endTime": "2025-07-24T17:35:45.349",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 48,
      "VINESAP": 22
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 79888,
    "startTime": "2025-07-24T17:32:22.404",
    "endTime": "2025-07-24T17:34:22.163",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 53,
      "VINESAP": 23
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 25793,
    "startTime": "2025-07-24T17:32:00.442",
    "endTime": "2025-07-24T17:32:22.338",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 17,
      "VINESAP": 8
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Reaches",
    "profit": 2467,
    "startTime": "2025-07-24T17:31:46.704",
    "endTime": "2025-07-24T17:31:59.7",
    "itemsCollected": {
      "ENCHANTED_FIG_LOG": 1,
      "ENCHANTED_MANGROVE_LOG": 1
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "Moonglade Marsh",
    "profit": 328878,
    "startTime": "2025-07-24T17:31:45.5",
    "endTime": "2025-07-24T17:31:46.644",
    "itemsCollected": {
      "DEEP_ROOT": 8,
      "ENCHANTED_MANGROVE_LOG": 6,
      "MANGROVE_LOG": 5,
      "VINESAP": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 15260,
    "startTime": "2025-07-24T17:30:54.287",
    "endTime": "2025-07-24T17:31:44.835",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 10,
      "VINESAP": 5
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 15260,
    "startTime": "2025-07-24T17:30:26.802",
    "endTime": "2025-07-24T17:30:53.909",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 10,
      "VINESAP": 5
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 46376,
    "startTime": "2025-07-24T17:29:31.746",
    "endTime": "2025-07-24T17:30:26.113",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 30,
      "VINESAP": 17
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 13770,
    "startTime": "2025-07-24T17:28:02.052",
    "endTime": "2025-07-24T17:29:21.671",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 10
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 32514,
    "startTime": "2025-07-24T17:27:03.909",
    "endTime": "2025-07-24T17:28:01.352",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 21,
      "VINESAP": 12
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "Moonglade Marsh",
    "profit": 32402,
    "startTime": "2025-07-24T17:27:02.389",
    "endTime": "2025-07-24T17:27:03.803",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 22,
      "VINESAP": 7
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 177084,
    "startTime": "2025-07-24T17:23:13.783",
    "endTime": "2025-07-24T17:26:35.975",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 35,
      "SHARD_DREADWING": 1,
      "SHARD_PHANFLARE": 2,
      "VINESAP": 16
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 68015,
    "startTime": "2025-07-24T17:19:03.566",
    "endTime": "2025-07-24T17:23:04.357",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 45,
      "VINESAP": 20
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 204719,
    "startTime": "2025-07-24T17:14:52.471",
    "endTime": "2025-07-24T17:19:02.14",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 45,
      "SHARD_PHANPYRE": 4,
      "VINESAP": 18
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 254606,
    "startTime": "2025-07-24T17:11:00.185",
    "endTime": "2025-07-24T17:14:50.589",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 33,
      "MANGROVE_LOG": -56,
      "SHARD_PHANFLARE": 5,
      "VINESAP": 14
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 25553,
    "startTime": "2025-07-24T17:09:40.712",
    "endTime": "2025-07-24T17:10:27.351",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 17,
      "VINESAP": 7
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Reaches",
    "profit": 35132,
    "startTime": "2025-07-24T17:08:59.95",
    "endTime": "2025-07-24T17:09:39.298",
    "itemsCollected": {
      "LUSHLILAC": 18,
      "SHARD_BIRRIES": 4
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 135374,
    "startTime": "2025-07-24T17:06:27.699",
    "endTime": "2025-07-24T17:08:59.881",
    "itemsCollected": {
      "DEEP_ROOT": 2,
      "ENCHANTED_FIG_LOG": 1,
      "ENCHANTED_MANGROVE_LOG": 36,
      "FORAGING_WISDOM_BOOSTER": -1,
      "MANGROVE_LOG": 31,
      "VINESAP": 15
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 66636,
    "startTime": "2025-07-24T17:04:08.704",
    "endTime": "2025-07-24T17:06:09.503",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 44,
      "VINESAP": 20
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 77347,
    "startTime": "2025-07-24T17:02:16.144",
    "endTime": "2025-07-24T17:04:07.277",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 51,
      "FIG_LOG": 529,
      "VINESAP": 20
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 586163,
    "startTime": "2025-07-24T16:56:59.036",
    "endTime": "2025-07-24T17:02:06.724",
    "itemsCollected": {
      "DEEP_ROOT": 2,
      "ENCHANTED_MANGROVE_LOG": 39,
      "MANGROVE_LOG": 62,
      "SHARD_PHANFLARE": 6,
      "SHARD_PHANPYRE": 6,
      "VINESAP": 17
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "Moonglade Marsh",
    "profit": 24448,
    "startTime": "2025-07-24T16:56:58.064",
    "endTime": "2025-07-24T16:56:58.793",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 16,
      "VINESAP": 8
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Reaches",
    "profit": 105429,
    "startTime": "2025-07-24T16:55:49.495",
    "endTime": "2025-07-24T16:56:07.029",
    "itemsCollected": {
      "DEEP_ROOT": 2,
      "ENCHANTED_MANGROVE_LOG": 17,
      "FORAGING_WISDOM_BOOSTER": -1,
      "MANGROVE_LOG": -20,
      "VINESAP": 7
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 25231,
    "startTime": "2025-07-24T16:54:46.937",
    "endTime": "2025-07-24T16:55:47.347",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 17,
      "VINESAP": 6
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "Driptoad Delve",
    "profit": 16361,
    "startTime": "2025-07-24T16:54:31.153",
    "endTime": "2025-07-24T16:54:46.64",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 11,
      "VINESAP": 4
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 45840,
    "startTime": "2025-07-24T16:53:27.372",
    "endTime": "2025-07-24T16:54:28.312",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 30,
      "VINESAP": 15
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "Moonglade Marsh",
    "profit": 29666,
    "startTime": "2025-07-24T16:53:26.942",
    "endTime": "2025-07-24T16:53:27.222",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 20,
      "VINESAP": 7
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 91088,
    "startTime": "2025-07-24T16:50:44.942",
    "endTime": "2025-07-24T16:53:26.509",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 16,
      "SHARD_PHANPYRE": 2,
      "VINESAP": 7
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Reaches",
    "profit": 122701,
    "startTime": "2025-07-24T16:50:32.476",
    "endTime": "2025-07-24T16:50:44.787",
    "itemsCollected": {
      "DEEP_ROOT": 1,
      "ENCHANTED_FIG_LOG": 1,
      "ENCHANTED_MANGROVE_LOG": 18,
      "MANGROVE_LOG": 24,
      "SHARD_INVISIBUG": 2,
      "VINESAP": 7
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 15280,
    "startTime": "2025-07-24T16:49:55.562",
    "endTime": "2025-07-24T16:50:05.57",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 10,
      "VINESAP": 5
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 176580,
    "startTime": "2025-07-24T16:47:16.683",
    "endTime": "2025-07-24T16:49:53.698",
    "itemsCollected": {
      "DEEP_ROOT": 3,
      "ENCHANTED_MANGROVE_LOG": 38,
      "FORAGING_WISDOM_BOOSTER": 1,
      "MANGROVE_LOG": -73,
      "VINESAP": 15
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Wetlands",
    "profit": 219790,
    "startTime": "2025-07-24T16:45:00.427",
    "endTime": "2025-07-24T16:47:01.24",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 48,
      "SHARD_PHANFLARE": 2,
      "SHARD_PHANPYRE": 2,
      "VINESAP": 22
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "South Reaches",
    "profit": 80,
    "startTime": "2025-07-24T16:44:39.977",
    "endTime": "2025-07-24T16:44:39.986",
    "itemsCollected": {
      "LUSHLILAC": 8
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m142CE",
    "location": "Crimson Isle",
    "profit": 273,
    "startTime": "2025-07-24T16:43:38.091",
    "endTime": "2025-07-24T16:44:39.643",
    "itemsCollected": {
      "ATTRIBUTE_SHARD": 9,
      "BLAZE_ROD": 13
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m87DW",
    "location": "South Wetlands",
    "profit": 1579050,
    "startTime": "2025-07-24T16:42:44.403",
    "endTime": "2025-07-24T16:43:38.022",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 12,
      "MANGROVE_LOG": 83,
      "SHARD_BURNINGSOUL": 10,
      "SHARD_INVISIBUG": 2,
      "VINESAP": 4
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m194BK",
    "location": "South Wetlands",
    "profit": 39132,
    "startTime": "2025-07-24T16:42:01.329",
    "endTime": "2025-07-24T16:42:43.719",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 26,
      "VINESAP": 11
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m194BK",
    "location": "Moonglade Marsh",
    "profit": 18038,
    "startTime": "2025-07-24T16:41:44.654",
    "endTime": "2025-07-24T16:42:00.942",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 12,
      "VINESAP": 5
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m194BK",
    "location": "Driptoad Delve",
    "profit": 11926,
    "startTime": "2025-07-24T16:41:41.792",
    "endTime": "2025-07-24T16:41:41.852",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 8,
      "VINESAP": 3
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m194BK",
    "location": "Moonglade Marsh",
    "profit": 5570,
    "startTime": "2025-07-24T16:41:16.311",
    "endTime": "2025-07-24T16:41:41.158",
    "itemsCollected": {
      "FIG_LOG": 1287,
      "LUSHLILAC": 6,
      "TENDER_WOOD": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m194BK",
    "location": "Evergreen Plateau",
    "profit": 10713,
    "startTime": "2025-07-24T16:40:24.584",
    "endTime": "2025-07-24T16:41:16.234",
    "itemsCollected": {
      "FIG_LOG": 2452,
      "TENDER_WOOD": 5
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m194BK",
    "location": "Tranquil Pass",
    "profit": -437,
    "startTime": "2025-07-24T16:40:07.261",
    "endTime": "2025-07-24T16:40:24.18",
    "itemsCollected": {
      "BAMBOO": -19,
      "LUSHLILAC": 8
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m194BK",
    "location": "Tranquility Sanctum",
    "profit": 486121,
    "startTime": "2025-07-24T16:38:44.354",
    "endTime": "2025-07-24T16:40:07.089",
    "itemsCollected": {
      "BAMBOO": -1,
      "SHARD_MOCHIBEAR": 6
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m194BK",
    "location": "Moonglade Marsh",
    "profit": 2467,
    "startTime": "2025-07-24T16:37:49.338",
    "endTime": "2025-07-24T16:37:50.062",
    "itemsCollected": {
      "ENCHANTED_FIG_LOG": 1,
      "ENCHANTED_MANGROVE_LOG": 1
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m194BK",
    "location": "Your Island",
    "profit": -184400,
    "startTime": "2025-07-23T22:21:28.078",
    "endTime": "2025-07-24T16:37:38.483",
    "itemsCollected": {
      "ATTRIBUTE_SHARD": 17,
      "GILL_MEMBRANE": -260,
      "LOG": 590,
      "LUSHLILAC": 6
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m181CV",
    "location": "Tangleburg",
    "profit": 151888,
    "startTime": "2025-07-23T22:18:57.496",
    "endTime": "2025-07-23T22:21:27.992",
    "itemsCollected": {
      "ATTRIBUTE_SHARD": 32,
      "GILL_MEMBRANE": 6,
      "LUSHLILAC": 2,
      "SHARD_DROWNED": 6,
      "STURDY_BONE": 1
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Fusion House",
    "profit": 88880,
    "startTime": "2025-07-23T22:18:56.497",
    "endTime": "2025-07-23T22:18:57.423",
    "itemsCollected": {
      "GILL_MEMBRANE": 40
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Tangleburg's Path",
    "profit": 49410,
    "startTime": "2025-07-23T22:18:29.796",
    "endTime": "2025-07-23T22:18:55.799",
    "itemsCollected": {
      "SHARD_DROWNED": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Drowned Reliquary",
    "profit": 296460,
    "startTime": "2025-07-23T22:16:19.527",
    "endTime": "2025-07-23T22:18:29.718",
    "itemsCollected": {
      "SHARD_DROWNED": 12
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Drowned Reliquary",
    "profit": 784629,
    "startTime": "2025-07-23T22:11:18.093",
    "endTime": "2025-07-23T22:16:19.453",
    "itemsCollected": {
      "GILL_MEMBRANE": 6,
      "SEA_LUMIES": 18,
      "SHARD_DROWNED": 31
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Drowned Reliquary",
    "profit": 1023660,
    "startTime": "2025-07-23T22:06:16.695",
    "endTime": "2025-07-23T22:11:17.935",
    "itemsCollected": {
      "ENCHANTED_SEA_LUMIES": 2,
      "GILL_MEMBRANE": 42,
      "SEA_LUMIES": -312,
      "SHARD_DROWNED": 38,
      "STURDY_BONE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Drowned Reliquary",
    "profit": 1381840,
    "startTime": "2025-07-23T22:01:06.755",
    "endTime": "2025-07-23T22:06:16.615",
    "itemsCollected": {
      "ATTRIBUTE_SHARD": 44,
      "GILL_MEMBRANE": 51,
      "SEA_LUMIES": 20,
      "SHARD_DROWNED": 54,
      "STURDY_BONE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Drowned Reliquary",
    "profit": 1930754,
    "startTime": "2025-07-23T21:55:54.202",
    "endTime": "2025-07-23T22:01:06.091",
    "itemsCollected": {
      "ATTRIBUTE_SHARD": 35,
      "GILL_MEMBRANE": 28,
      "SEA_LUMIES": 48,
      "SHARD_DROWNED": 74
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Moonglade Marsh",
    "profit": 4,
    "startTime": "2025-07-23T21:55:42.698",
    "endTime": "2025-07-23T21:55:44.279",
    "itemsCollected": {
      "BONE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Loch",
    "profit": 38001,
    "startTime": "2025-07-23T21:55:31.735",
    "endTime": "2025-07-23T21:55:41.999",
    "itemsCollected": {
      "SHARD_MOSSYBIT": 1
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Tranquility Sanctum",
    "profit": 646670,
    "startTime": "2025-07-23T21:51:25.094",
    "endTime": "2025-07-23T21:55:29.728",
    "itemsCollected": {
      "BAMBOO": -2,
      "SHARD_BAMBULEAF": 8
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Loch",
    "profit": 6728,
    "startTime": "2025-07-23T21:50:43.167",
    "endTime": "2025-07-23T21:50:43.908",
    "itemsCollected": {
      "SEA_LUMIES": 8
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 142204,
    "startTime": "2025-07-23T21:50:32.228",
    "endTime": "2025-07-23T21:50:41.751",
    "itemsCollected": {
      "SHARD_SPIKE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Kelpwoven Tunnels",
    "profit": 435022,
    "startTime": "2025-07-23T21:49:51.367",
    "endTime": "2025-07-23T21:50:32.164",
    "itemsCollected": {
      "SEA_LUMIES": 10,
      "SHARD_SPIKE": 6
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Kelpwoven Tunnels",
    "profit": 3364,
    "startTime": "2025-07-23T21:49:06.607",
    "endTime": "2025-07-23T21:49:26.162",
    "itemsCollected": {
      "SEA_LUMIES": 4
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Kelpwoven Tunnels",
    "profit": 142204,
    "startTime": "2025-07-23T21:48:52.093",
    "endTime": "2025-07-23T21:49:05.193",
    "itemsCollected": {
      "SHARD_SPIKE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Kelpwoven Tunnels",
    "profit": 13456,
    "startTime": "2025-07-23T21:48:28.659",
    "endTime": "2025-07-23T21:48:51.399",
    "itemsCollected": {
      "SEA_LUMIES": 16
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 142204,
    "startTime": "2025-07-23T21:47:53.213",
    "endTime": "2025-07-23T21:48:28.583",
    "itemsCollected": {
      "SHARD_SPIKE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Kelpwoven Tunnels",
    "profit": 3364,
    "startTime": "2025-07-23T21:47:08.928",
    "endTime": "2025-07-23T21:47:17.477",
    "itemsCollected": {
      "SEA_LUMIES": 4
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 931054,
    "startTime": "2025-07-23T21:44:52.111",
    "endTime": "2025-07-23T21:47:08.763",
    "itemsCollected": {
      "SEA_LUMIES": 8,
      "SHARD_SPIKE": 13
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Moonglade Marsh",
    "profit": 142204,
    "startTime": "2025-07-23T21:44:48.351",
    "endTime": "2025-07-23T21:44:52.015",
    "itemsCollected": {
      "SHARD_SPIKE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Kelpwoven Tunnels",
    "profit": 6728,
    "startTime": "2025-07-23T21:44:34.825",
    "endTime": "2025-07-23T21:44:48.014",
    "itemsCollected": {
      "SEA_LUMIES": 8
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Kelpwoven Tunnels",
    "profit": 165752,
    "startTime": "2025-07-23T21:43:55.483",
    "endTime": "2025-07-23T21:44:34.721",
    "itemsCollected": {
      "SEA_LUMIES": 28,
      "SHARD_SPIKE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 71102,
    "startTime": "2025-07-23T21:43:01.292",
    "endTime": "2025-07-23T21:43:55.417",
    "itemsCollected": {
      "SHARD_SPIKE": 1
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Moonglade Marsh",
    "profit": 142204,
    "startTime": "2025-07-23T21:41:57.046",
    "endTime": "2025-07-23T21:43:01.222",
    "itemsCollected": {
      "SHARD_SPIKE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Kelpwoven Tunnels",
    "profit": 13456,
    "startTime": "2025-07-23T21:41:45.069",
    "endTime": "2025-07-23T21:41:56.681",
    "itemsCollected": {
      "SEA_LUMIES": 16
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Tangleburg",
    "profit": 40,
    "startTime": "2025-07-23T21:40:55.032",
    "endTime": "2025-07-23T21:41:05.587",
    "itemsCollected": {
      "LUSHLILAC": 4
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Squid Cave",
    "profit": 352872,
    "startTime": "2025-07-23T21:40:36.759",
    "endTime": "2025-07-23T21:40:54.436",
    "itemsCollected": {
      "SHARD_LUMISQUID": 6
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Shallows",
    "profit": 26912,
    "startTime": "2025-07-23T21:40:03.16",
    "endTime": "2025-07-23T21:40:03.894",
    "itemsCollected": {
      "SEA_LUMIES": 32
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 184958,
    "startTime": "2025-07-23T21:39:16.061",
    "endTime": "2025-07-23T21:40:02.474",
    "itemsCollected": {
      "ENCHANTED_SEA_LUMIES": 4,
      "SEA_LUMIES": -564,
      "SHARD_BIRRIES": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 91552,
    "startTime": "2025-07-23T21:38:40.11",
    "endTime": "2025-07-23T21:39:15.364",
    "itemsCollected": {
      "SEA_LUMIES": 72,
      "SHARD_AZURE": 2,
      "SHARD_VERDANT": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Shallows",
    "profit": 16000,
    "startTime": "2025-07-23T21:38:30.67",
    "endTime": "2025-07-23T21:38:30.682",
    "itemsCollected": {
      "SHARD_AZURE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Squid Cave",
    "profit": 117624,
    "startTime": "2025-07-23T21:38:11.074",
    "endTime": "2025-07-23T21:38:13.232",
    "itemsCollected": {
      "SHARD_LUMISQUID": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Moonglade Marsh",
    "profit": 2998,
    "startTime": "2025-07-23T21:38:08.223",
    "endTime": "2025-07-23T21:38:09.664",
    "itemsCollected": {
      "FIG_LOG": 593,
      "LUSHLILAC": 8,
      "TENDER_WOOD": 3
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Tangleburg",
    "profit": -224,
    "startTime": "2025-07-23T21:35:08.832",
    "endTime": "2025-07-23T21:37:51.891",
    "itemsCollected": {
      "LUSHLILAC": 6,
      "MANGROVE_LOG": -71
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Squid Cave",
    "profit": 235248,
    "startTime": "2025-07-23T21:34:41.178",
    "endTime": "2025-07-23T21:35:07.446",
    "itemsCollected": {
      "SHARD_LUMISQUID": 4
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 387425,
    "startTime": "2025-07-23T21:33:09.071",
    "endTime": "2025-07-23T21:34:39.767",
    "itemsCollected": {
      "SEA_LUMIES": 90,
      "SHARD_AZURE": 2,
      "SHARD_BIRRIES": 3,
      "SHARD_JOYDIVE": 2,
      "SHARD_SALMON": 6,
      "SHARD_VERDANT": 11
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 17474,
    "startTime": "2025-07-23T21:33:08.232",
    "endTime": "2025-07-23T21:33:08.977",
    "itemsCollected": {
      "SHARD_BIRRIES": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Loch",
    "profit": 76002,
    "startTime": "2025-07-23T21:32:50.826",
    "endTime": "2025-07-23T21:32:58.811",
    "itemsCollected": {
      "SHARD_MOSSYBIT": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Shallows",
    "profit": 2600,
    "startTime": "2025-07-23T21:32:50.129",
    "endTime": "2025-07-23T21:32:50.76",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 1,
      "VINESAP": 4
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Loch",
    "profit": 5760,
    "startTime": "2025-07-23T21:32:41.322",
    "endTime": "2025-07-23T21:32:50.053",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 4
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Loch",
    "profit": 6940,
    "startTime": "2025-07-23T21:32:25.084",
    "endTime": "2025-07-23T21:32:40.615",
    "itemsCollected": {
      "ENCHANTED_MANGROVE_LOG": 5,
      "MANGROVE_LOG": -65
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Moonglade Marsh",
    "profit": 20,
    "startTime": "2025-07-23T21:32:24.358",
    "endTime": "2025-07-23T21:32:24.389",
    "itemsCollected": {
      "LUSHLILAC": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "North Reaches",
    "profit": 40,
    "startTime": "2025-07-23T21:31:35.112",
    "endTime": "2025-07-23T21:32:23.661",
    "itemsCollected": {
      "LUSHLILAC": 4
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Outpost",
    "profit": 2526,
    "startTime": "2025-07-23T21:31:15.717",
    "endTime": "2025-07-23T21:31:35.039",
    "itemsCollected": {
      "ENCHANTED_FIG_LOG": 1,
      "ENCHANTED_MANGROVE_LOG": 1
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Loch",
    "profit": 366330,
    "startTime": "2025-07-23T21:30:56.685",
    "endTime": "2025-07-23T21:31:12.266",
    "itemsCollected": {
      "AGATHA_COUPON": 30
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 266078,
    "startTime": "2025-07-23T21:30:52.28",
    "endTime": "2025-07-23T21:30:56.614",
    "itemsCollected": {
      "SEA_LUMIES": 94,
      "SHARD_JOYDIVE": 2,
      "SHARD_SALMON": 6
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 60000,
    "startTime": "2025-07-23T21:30:44.175",
    "endTime": "2025-07-23T21:30:46.363",
    "itemsCollected": {
      "SHARD_VERDANT": 8
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Depths",
    "profit": 150183,
    "startTime": "2025-07-23T21:30:03.434",
    "endTime": "2025-07-23T21:30:35.685",
    "itemsCollected": {
      "BAMBOO": 27,
      "SEA_LUMIES": 32,
      "SHARD_JOYDIVE": 2
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Tranquility Sanctum",
    "profit": 485010,
    "startTime": "2025-07-23T21:29:40.867",
    "endTime": "2025-07-23T21:30:01.154",
    "itemsCollected": {
      "SHARD_BAMBULEAF": 6
    }
  },
  {
    "playerUuid": "aadcde4ae0714253b0a562aa13c9900e",
    "server": "m143DF",
    "location": "Murkwater Shallows",
    "profit": 1682,
    "startTime": "2025-07-23T21:29:39.434",
    "endTime": "2025-07-23T21:29:39.446",
    "itemsCollected": {
      "SEA_LUMIES": 2
    }
  }
]
""";
}
