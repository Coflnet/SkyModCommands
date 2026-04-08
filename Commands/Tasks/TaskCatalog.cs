using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC.Tasks;

internal static class TaskCatalog
{
    internal static ClassNameDictonary<ProfitTask> Create()
    {
        var tasks = new ClassNameDictonary<ProfitTask>();

        // Core tasks (special logic)
        tasks.Add<KatTask>();
        tasks.Add<ForgeTask>();
        tasks.Add<ComposterTask>();

        // Generic location trackers
        tasks.Add<GalateaDivingTask>();
        tasks.Add<GalateaFishingTask>();
        tasks.Add<GalateaTask>();
        tasks.Add<JerryTask>();
        tasks.Add<GoldMineTask>();
        tasks.Add<DeepCavernsTask>();
        tasks.Add<DwarvenMinesMiningTask>();
        tasks.Add<TheEndTask>();
        tasks.Add<TheParkTask>();
        tasks.Add<BackwaterBayouTask>();
        tasks.Add<GardenTask>();
        tasks.Add<CrimsonIsleTask>();

        // Fishing tasks (regular)
        tasks.Add<PiscaryFishingTask>();
        tasks.Add<BayouFishingTask>();
        tasks.Add<BayouHotspotFishingTask>();
        tasks.Add<SpookyFishingTask>();
        tasks.Add<WinterFishingTask>();
        tasks.Add<WaterWormFishingTask>();
        tasks.Add<QuarryFishingTask>();
        tasks.Add<CrimsonFishingTask>();
        tasks.Add<CrimsonHotspotFishingTask>();
        tasks.Add<FestivalFishingTask>();
        tasks.Add<SquidFishingTask>();
        tasks.Add<GalateaFishingMethodTask>();
        tasks.Add<OasisFishingTask>();
        tasks.Add<WaterFishingTask>();
        tasks.Add<MagmaCoreFishingTask>();
        tasks.Add<FlamingWormFishingTask>();

        // Fishing tasks (hunting)
        tasks.Add<PiscaryFishingHuntingTask>();
        tasks.Add<BayouFishingHuntingTask>();
        tasks.Add<BayouHotspotFishingHuntingTask>();
        tasks.Add<SpookyFishingHuntingTask>();
        tasks.Add<WinterFishingHuntingTask>();
        tasks.Add<WaterWormFishingHuntingTask>();
        tasks.Add<QuarryFishingHuntingTask>();
        tasks.Add<CrimsonFishingHuntingTask>();
        tasks.Add<FestivalFishingHuntingTask>();
        tasks.Add<SquidFishingHuntingTask>();
        tasks.Add<GalateaFishingHuntingTask>();
        tasks.Add<OasisFishingHuntingTask>();
        tasks.Add<WaterFishingHuntingTask>();

        // Kuudra tasks
        tasks.Add<KuudraT1Task>();
        tasks.Add<KuudraT2Task>();
        tasks.Add<KuudraT3Task>();
        tasks.Add<KuudraT4Task>();
        tasks.Add<KuudraT5Task>();

        // Slayer tasks
        tasks.Add<T3InfernoDemonlordTask>();
        tasks.Add<T4InfernoDemonlordTask>();
        tasks.Add<T5TarantulaTask>();
        tasks.Add<T4TarantulaTask>();
        tasks.Add<AshfangTask>();
        tasks.Add<BarbarianDukeXTask>();
        tasks.Add<T4VoidgloomsTask>();
        tasks.Add<T4VoidgloomsFdTask>();

        // Mob farm tasks (Galatea)
        tasks.Add<CinderbatTask>();
        tasks.Add<BurningsoulTask>();
        tasks.Add<LumisquidTask>();
        tasks.Add<ShellwiseTask>();
        tasks.Add<MatchoTask>();
        tasks.Add<StridersurferTask>();
        tasks.Add<SporeTask>();
        tasks.Add<BladesoulTask>();
        tasks.Add<JoydiveTask>();
        tasks.Add<DrownedTask>();
        tasks.Add<CoralotTask>();
        tasks.Add<BambuleafTask>();
        tasks.Add<HideonleafTask>();
        tasks.Add<DreadwingTask>();
        tasks.Add<SpikeTask>();
        tasks.Add<SeerTask>();
        tasks.Add<MochibearkTask>();
        tasks.Add<MossybitTask>();

        // Mob farm tasks (non-Galatea)
        tasks.Add<VoraciousSpiderTask>();
        tasks.Add<GoldenGhoulTask>();
        tasks.Add<StarSentryTask>();
        tasks.Add<AutomatonTask>();
        tasks.Add<XyzMobTask>();
        tasks.Add<GhostMobTask>();

        // Hunting tasks
        tasks.Add<RainSlimeHuntingTask>();
        tasks.Add<HellwispHuntingTask>();
        tasks.Add<XyzHuntingTask>();
        tasks.Add<KadaKnightHuntingTask>();
        tasks.Add<InvisibugHuntingTask>();
        tasks.Add<YogHuntingTask>();
        tasks.Add<FlareHuntingTask>();
        tasks.Add<BezalHuntingTask>();
        tasks.Add<GhostHuntingTask>();
        tasks.Add<FlamingSpiderHuntingTask>();
        tasks.Add<ObsidianDefenderHuntingTask>();
        tasks.Add<WitherSpecterHuntingTask>();
        tasks.Add<ZealotHuntingTask>();
        tasks.Add<BruiserHuntingTask>();
        tasks.Add<PestHuntingTask>();

        // Diana / Mythological event tasks
        tasks.Add<DianaTask>();
        tasks.Add<DianaHuntingTask>();

        // Mining tasks (gemstone)
        tasks.Add<ThystMiningTask>();
        tasks.Add<JasperMiningTask>();
        tasks.Add<JadeMiningTask>();
        tasks.Add<AmberMiningTask>();
        tasks.Add<SapphireMiningTask>();
        tasks.Add<PeridotMiningTask>();

        // Mining tasks (ore)
        tasks.Add<CoalMiningTask>();
        tasks.Add<DiamondMiningTask>();
        tasks.Add<RedstoneMiningTask>();
        tasks.Add<CobblestoneMiningTask>();
        tasks.Add<ObsidianMiningTask>();
        tasks.Add<TungstenMiningTask>();
        tasks.Add<UmberMiningTask>();

        // Mining tasks (special)
        tasks.Add<NucleusMiningTask>();
        tasks.Add<SludgeMiningTask>();
        tasks.Add<SludgeMiningGemMixtureTask>();
        tasks.Add<SludgeMiningCoalTask>();
        tasks.Add<ScathaMiningTask>();
        tasks.Add<PrecursorCityPowderMiningTask>();
        tasks.Add<JunglePowderMiningTask>();
        tasks.Add<MithrilDepositsPowderMiningTask>();
        tasks.Add<GoblinHoldoutPowderMiningTask>();

        // Crafting tasks
        tasks.Add<ReaperScytheTask>();
        tasks.Add<GauntletOfContagionTask>();
        tasks.Add<ExportableCarrotsCraftTask>();
        tasks.Add<ShimmeringLightSlippersTask>();
        tasks.Add<ExtremelyRealShurikenTask>();
        tasks.Add<ShimmeringLightHoodTask>();
        tasks.Add<PolarvoidBookTask>();
        tasks.Add<GrandmasKnittingNeedleTask>();
        tasks.Add<SoulOfTheAlphaTask>();
        tasks.Add<BluetoothRingTask>();
        tasks.Add<DiscriteTask>();
        tasks.Add<CaducousFeederTask>();

        // Bazaar crafting tasks
        tasks.Add<BladeSoulBzTask>();
        tasks.Add<AshfangBzTask>();
        tasks.Add<EmptyChumcapBucketTask>();
        tasks.Add<EndermanPetFdTask>();
        tasks.Add<ExportableCarrotsTask>();

        // Dungeon tasks
        tasks.Add<M4Task>();
        tasks.Add<M5Task>();
        tasks.Add<M6Task>();
        tasks.Add<M7Task>();
        tasks.Add<M7KismetTask>();

        // Garden tasks
        tasks.Add<PestTask>();
        tasks.Add<FigTask>();

        // Misc tasks
        tasks.Add<ZealotsFdTask>();
        tasks.Add<RedMushroomTask>();
        tasks.Add<BrownMushroomTask>();
        tasks.Add<MyceliumTask>();

        // Passive tasks
        tasks.Add<HuntingTrapTask>();

        // Limited/daily tasks
        tasks.Add<DailyCrimsonQuestsTask>();
        tasks.Add<ExperimentationTableTask>();
        tasks.Add<RiftAccessTask>();
        tasks.Add<ViperShardNpcFlipTask>();

        return tasks;
    }
}