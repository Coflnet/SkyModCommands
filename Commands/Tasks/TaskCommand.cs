using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC.Tasks;

[CommandDescription(
    "Lists tasks that can be done for profit",
    "Tasks are calculated based on your current progress",
    "and try to self adjust based on how many items",
    "you managed to collect recently (active tasks)",
    "Passive tasks include flips from other commands")]
public class TaskCommand : ReadOnlyListCommand<TaskResult>
{
    private ClassNameDictonary<ProfitTask> _tasks = new ClassNameDictonary<ProfitTask>();
    private ConcurrentDictionary<Type, TaskParams.CalculationCache> Cache = new();

    public TaskCommand()
    {
        // Core tasks (special logic)
        _tasks.Add<KatTask>();
        _tasks.Add<ForgeTask>();
        _tasks.Add<ComposterTask>();

        // Generic location trackers
        _tasks.Add<GalateaDivingTask>();
        _tasks.Add<GalateaFishingTask>();
        _tasks.Add<GalateaTask>();
        _tasks.Add<JerryTask>();
        _tasks.Add<GoldMineTask>();
        _tasks.Add<DeepCavernsTask>();
        _tasks.Add<DwarvenMinesMiningTask>();
        _tasks.Add<TheEndTask>();
        _tasks.Add<TheParkTask>();
        _tasks.Add<BackwaterBayouTask>();
        _tasks.Add<GardenTask>();
        _tasks.Add<CrimsonIsleTask>();

        // Fishing tasks (regular)
        _tasks.Add<PiscaryFishingTask>();
        _tasks.Add<BayouFishingTask>();
        _tasks.Add<BayouHotspotFishingTask>();
        _tasks.Add<SpookyFishingTask>();
        _tasks.Add<WinterFishingTask>();
        _tasks.Add<WaterWormFishingTask>();
        _tasks.Add<QuarryFishingTask>();
        _tasks.Add<CrimsonFishingTask>();
        _tasks.Add<CrimsonHotspotFishingTask>();
        _tasks.Add<FestivalFishingTask>();
        _tasks.Add<SquidFishingTask>();
        _tasks.Add<GalateaFishingMethodTask>();
        _tasks.Add<OasisFishingTask>();
        _tasks.Add<WaterFishingTask>();
        _tasks.Add<MagmaCoreFishingTask>();
        _tasks.Add<FlamingWormFishingTask>();

        // Fishing tasks (hunting)
        _tasks.Add<PiscaryFishingHuntingTask>();
        _tasks.Add<BayouFishingHuntingTask>();
        _tasks.Add<BayouHotspotFishingHuntingTask>();
        _tasks.Add<SpookyFishingHuntingTask>();
        _tasks.Add<WinterFishingHuntingTask>();
        _tasks.Add<WaterWormFishingHuntingTask>();
        _tasks.Add<QuarryFishingHuntingTask>();
        _tasks.Add<CrimsonFishingHuntingTask>();
        _tasks.Add<FestivalFishingHuntingTask>();
        _tasks.Add<SquidFishingHuntingTask>();
        _tasks.Add<GalateaFishingHuntingTask>();
        _tasks.Add<OasisFishingHuntingTask>();
        _tasks.Add<WaterFishingHuntingTask>();

        // Kuudra tasks
        _tasks.Add<KuudraT1Task>();
        _tasks.Add<KuudraT2Task>();
        _tasks.Add<KuudraT3Task>();
        _tasks.Add<KuudraT4Task>();
        _tasks.Add<KuudraT5Task>();

        // Slayer tasks
        _tasks.Add<T3InfernoDemonlordTask>();
        _tasks.Add<T4InfernoDemonlordTask>();
        _tasks.Add<T5TarantulaTask>();
        _tasks.Add<T4TarantulaTask>();
        _tasks.Add<AshfangTask>();
        _tasks.Add<BarbarianDukeXTask>();
        _tasks.Add<T4VoidgloomsTask>();
        _tasks.Add<T4VoidgloomsFdTask>();

        // Mob farm tasks (Galatea)
        _tasks.Add<CinderbatTask>();
        _tasks.Add<BurningsoulTask>();
        _tasks.Add<LumisquidTask>();
        _tasks.Add<ShellwiseTask>();
        _tasks.Add<MatchoTask>();
        _tasks.Add<StridersurferTask>();
        _tasks.Add<SporeTask>();
        _tasks.Add<BladesoulTask>();
        _tasks.Add<JoydiveTask>();
        _tasks.Add<DrownedTask>();
        _tasks.Add<CoralotTask>();
        _tasks.Add<BambuleafTask>();
        _tasks.Add<HideonleafTask>();
        _tasks.Add<DreadwingTask>();
        _tasks.Add<SpikeTask>();
        _tasks.Add<SeerTask>();
        _tasks.Add<MochibearkTask>();
        _tasks.Add<MossybitTask>();

        // Mob farm tasks (non-Galatea)
        _tasks.Add<VoraciousSpiderTask>();
        _tasks.Add<GoldenGhoulTask>();
        _tasks.Add<StarSentryTask>();
        _tasks.Add<AutomatonTask>();
        _tasks.Add<XyzMobTask>();
        _tasks.Add<GhostMobTask>();

        // Hunting tasks
        _tasks.Add<RainSlimeHuntingTask>();
        _tasks.Add<HellwispHuntingTask>();
        _tasks.Add<XyzHuntingTask>();
        _tasks.Add<KadaKnightHuntingTask>();
        _tasks.Add<InvisibugHuntingTask>();
        _tasks.Add<YogHuntingTask>();
        _tasks.Add<FlareHuntingTask>();
        _tasks.Add<BezalHuntingTask>();
        _tasks.Add<GhostHuntingTask>();
        _tasks.Add<FlamingSpiderHuntingTask>();
        _tasks.Add<ObsidianDefenderHuntingTask>();
        _tasks.Add<WitherSpecterHuntingTask>();
        _tasks.Add<ZealotHuntingTask>();
        _tasks.Add<BruiserHuntingTask>();
        _tasks.Add<PestHuntingTask>();

        // Diana / Mythological event tasks
        _tasks.Add<DianaTask>();
        _tasks.Add<DianaHuntingTask>();

        // Mining tasks (gemstone)
        _tasks.Add<ThystMiningTask>();
        _tasks.Add<JasperMiningTask>();
        _tasks.Add<JadeMiningTask>();
        _tasks.Add<AmberMiningTask>();
        _tasks.Add<SapphireMiningTask>();
        _tasks.Add<PeridotMiningTask>();

        // Mining tasks (ore)
        _tasks.Add<CoalMiningTask>();
        _tasks.Add<DiamondMiningTask>();
        _tasks.Add<RedstoneMiningTask>();
        _tasks.Add<CobblestoneMiningTask>();
        _tasks.Add<ObsidianMiningTask>();
        _tasks.Add<TungstenMiningTask>();
        _tasks.Add<UmberMiningTask>();

        // Mining tasks (special)
        _tasks.Add<NucleusMiningTask>();
        _tasks.Add<SludgeMiningTask>();
        _tasks.Add<SludgeMiningGemMixtureTask>();
        _tasks.Add<SludgeMiningCoalTask>();
        _tasks.Add<ScathaMiningTask>();
        _tasks.Add<PrecursorCityPowderMiningTask>();
        _tasks.Add<JunglePowderMiningTask>();
        _tasks.Add<MithrilDepositsPowderMiningTask>();
        _tasks.Add<GoblinHoldoutPowderMiningTask>();

        // Crafting tasks
        _tasks.Add<ReaperScytheTask>();
        _tasks.Add<GauntletOfContagionTask>();
        _tasks.Add<ExportableCarrotsCraftTask>();
        _tasks.Add<ShimmeringLightSlippersTask>();
        _tasks.Add<ExtremelyRealShurikenTask>();
        _tasks.Add<ShimmeringLightHoodTask>();
        _tasks.Add<PolarvoidBookTask>();
        _tasks.Add<GrandmasKnittingNeedleTask>();
        _tasks.Add<SoulOfTheAlphaTask>();
        _tasks.Add<BluetoothRingTask>();
        _tasks.Add<DiscriteTask>();
        _tasks.Add<CaducousFeederTask>();

        // Bazaar crafting tasks
        _tasks.Add<BladeSoulBzTask>();
        _tasks.Add<AshfangBzTask>();
        _tasks.Add<EmptyChumcapBucketTask>();
        _tasks.Add<EndermanPetFdTask>();
        _tasks.Add<ExportableCarrotsTask>();

        // Dungeon tasks
        _tasks.Add<M4Task>();
        _tasks.Add<M5Task>();
        _tasks.Add<M6Task>();
        _tasks.Add<M7Task>();
        _tasks.Add<M7KismetTask>();

        // Garden tasks
        _tasks.Add<PestTask>();
        _tasks.Add<FigTask>();

        // Misc tasks
        _tasks.Add<ZealotsFdTask>();
        _tasks.Add<RedMushroomTask>();
        _tasks.Add<BrownMushroomTask>();
        _tasks.Add<MyceliumTask>();
    }
    public override bool IsPublic => true;

    protected override void Format(MinecraftSocket socket, DialogBuilder db, TaskResult elem)
    {
        db.MsgLine($"§6{socket.FormatPrice(elem.ProfitPerHour)} /h {McColorCodes.GRAY}{elem.Message}", elem.OnClick, elem.Details);
    }

    protected override async Task<IEnumerable<TaskResult>> GetElements(MinecraftSocket socket, string val)
    {
        var itemsApi = socket.GetService<Items.Client.Api.IItemsApi>();
        var cleanPrices = socket.GetService<ISniperClient>().GetCleanPrices();
        var bazaarPrices = socket.GetService<IBazaarApi>().GetAllPricesAsync();
        var locationProfitTask = socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdProfitHistoryGetAsync(socket.SessionInfo.McUuid, DateTime.UtcNow, 300);
        var namesTask = itemsApi.ItemNamesGetWithHttpInfoAsync();
        var extractedState = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdExtractedGetAsync(socket.SessionInfo.McName);
        var locationProfit = await locationProfitTask;
        var names = JsonConvert.DeserializeObject<List<Items.Client.Model.ItemPreview>>((await namesTask).RawContent);
        var nameLookup = names?.ToDictionary(i => i.Tag, i => i.Name) ?? [];
        if(nameLookup.Count == 0)
        {
            socket.SendMessage($"{COFLNET}{McColorCodes.RED}Could not get item names, using tags instead");
        }

        var parameters = new TaskParams
        {
            TestTime = DateTime.UtcNow,
            ExtractedInfo = extractedState,
            Socket = socket,
            Cache = Cache,
            CleanPrices = await cleanPrices,
            BazaarPrices = await bazaarPrices,
            Names = nameLookup,
            LocationProfit = locationProfit.Where(d => d.EndTime - d.StartTime < TimeSpan.FromHours(1)).GroupBy(l=>l.Location)?.ToDictionary(l => l.Key, l => l.ToArray()) ?? [],
            MaxAvailableCoins = socket.SessionInfo.Purse > 0 ? socket.SessionInfo.Purse : 1000000000 // Default to 1 billion coins if not set
        };
        var all = await Task.WhenAll(_tasks.Select(async t =>
        {
            try
            {
                return await t.Value.Execute(parameters);
            }
            catch (Exception e)
            {
                return new TaskResult
                {
                    ProfitPerHour = 0,
                    Message = $"§cError while trying to calculate task {t.Key} {t.Value.Description}",
                    Details = e.ToString()
                };
            }
        }).ToList());
        return all.OrderByDescending(r => r.ProfitPerHour).ToList();
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<TaskResult> elements, IEnumerable<TaskResult> toDisplay)
    {
        db.MsgLine("Please let us know if any of the numbers are incorrect on discord", "/cofl report numbers incorrect", "For larger bugs you will usually be rewarded as well\nClick to get a report reference id!");
        if (socket.Version.StartsWith("1.5") || socket.Version.StartsWith("1.6"))
            db.MsgLine($"{McColorCodes.RED}There is a newer mod version that improves this feature");
    }

    protected override string GetId(TaskResult elem)
    {
        return elem.ProfitPerHour + elem.Message;
    }
}
