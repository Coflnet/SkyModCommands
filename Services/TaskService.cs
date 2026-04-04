using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Commands.MC.Tasks;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Shared singleton that holds the task registry and executes tasks.
/// Used by both the WebSocket TaskCommand and the REST TaskController.
/// </summary>
public class TaskService
{
    private readonly List<ProfitTask> _tasks;

    public TaskService()
    {
        _tasks =
        [
            // Core tasks (special logic)
            new KatTask(),
            new ForgeTask(),
            new ComposterTask(),

            // Generic location trackers
            new GalateaDivingTask(),
            new GalateaFishingTask(),
            new GalateaTask(),
            new JerryTask(),
            new GoldMineTask(),
            new DeepCavernsTask(),
            new DwarvenMinesMiningTask(),
            new TheEndTask(),
            new TheParkTask(),
            new BackwaterBayouTask(),
            new GardenTask(),
            new CrimsonIsleTask(),

            // Fishing tasks (regular)
            new PiscaryFishingTask(),
            new BayouFishingTask(),
            new BayouHotspotFishingTask(),
            new SpookyFishingTask(),
            new WinterFishingTask(),
            new WaterWormFishingTask(),
            new QuarryFishingTask(),
            new CrimsonFishingTask(),
            new CrimsonHotspotFishingTask(),
            new FestivalFishingTask(),
            new SquidFishingTask(),
            new GalateaFishingMethodTask(),
            new OasisFishingTask(),
            new WaterFishingTask(),
            new MagmaCoreFishingTask(),
            new FlamingWormFishingTask(),

            // Fishing tasks (hunting)
            new PiscaryFishingHuntingTask(),
            new BayouFishingHuntingTask(),
            new BayouHotspotFishingHuntingTask(),
            new SpookyFishingHuntingTask(),
            new WinterFishingHuntingTask(),
            new WaterWormFishingHuntingTask(),
            new QuarryFishingHuntingTask(),
            new CrimsonFishingHuntingTask(),
            new FestivalFishingHuntingTask(),
            new SquidFishingHuntingTask(),
            new GalateaFishingHuntingTask(),
            new OasisFishingHuntingTask(),
            new WaterFishingHuntingTask(),

            // Kuudra tasks
            new KuudraT1Task(),
            new KuudraT2Task(),
            new KuudraT3Task(),
            new KuudraT4Task(),
            new KuudraT5Task(),

            // Slayer tasks
            new T3InfernoDemonlordTask(),
            new T4InfernoDemonlordTask(),
            new T5TarantulaTask(),
            new T4TarantulaTask(),
            new AshfangTask(),
            new BarbarianDukeXTask(),
            new T4VoidgloomsTask(),
            new T4VoidgloomsFdTask(),

            // Mob farm tasks (Galatea)
            new CinderbatTask(),
            new BurningsoulTask(),
            new LumisquidTask(),
            new ShellwiseTask(),
            new MatchoTask(),
            new StridersurferTask(),
            new SporeTask(),
            new BladesoulTask(),
            new JoydiveTask(),
            new DrownedTask(),
            new CoralotTask(),
            new BambuleafTask(),
            new HideonleafTask(),
            new DreadwingTask(),
            new SpikeTask(),
            new SeerTask(),
            new MochibearkTask(),
            new MossybitTask(),

            // Mob farm tasks (non-Galatea)
            new VoraciousSpiderTask(),
            new GoldenGhoulTask(),
            new StarSentryTask(),
            new AutomatonTask(),
            new XyzMobTask(),
            new GhostMobTask(),

            // Hunting tasks
            new RainSlimeHuntingTask(),
            new HellwispHuntingTask(),
            new XyzHuntingTask(),
            new KadaKnightHuntingTask(),
            new InvisibugHuntingTask(),
            new YogHuntingTask(),
            new FlareHuntingTask(),
            new BezalHuntingTask(),
            new GhostHuntingTask(),
            new FlamingSpiderHuntingTask(),
            new ObsidianDefenderHuntingTask(),
            new WitherSpecterHuntingTask(),
            new ZealotHuntingTask(),
            new BruiserHuntingTask(),
            new PestHuntingTask(),

            // Diana / Mythological event tasks
            new DianaTask(),
            new DianaHuntingTask(),

            // Mining tasks (gemstone)
            new ThystMiningTask(),
            new JasperMiningTask(),
            new JadeMiningTask(),
            new AmberMiningTask(),
            new SapphireMiningTask(),
            new PeridotMiningTask(),

            // Mining tasks (ore)
            new CoalMiningTask(),
            new DiamondMiningTask(),
            new RedstoneMiningTask(),
            new CobblestoneMiningTask(),
            new ObsidianMiningTask(),
            new TungstenMiningTask(),
            new UmberMiningTask(),

            // Mining tasks (special)
            new NucleusMiningTask(),
            new SludgeMiningTask(),
            new SludgeMiningGemMixtureTask(),
            new SludgeMiningCoalTask(),
            new ScathaMiningTask(),
            new PrecursorCityPowderMiningTask(),
            new JunglePowderMiningTask(),
            new MithrilDepositsPowderMiningTask(),
            new GoblinHoldoutPowderMiningTask(),

            // Crafting tasks
            new ReaperScytheTask(),
            new GauntletOfContagionTask(),
            new ExportableCarrotsCraftTask(),
            new ShimmeringLightSlippersTask(),
            new ExtremelyRealShurikenTask(),
            new ShimmeringLightHoodTask(),
            new PolarvoidBookTask(),
            new GrandmasKnittingNeedleTask(),
            new SoulOfTheAlphaTask(),
            new BluetoothRingTask(),
            new DiscriteTask(),
            new CaducousFeederTask(),

            // Bazaar crafting tasks
            new BladeSoulBzTask(),
            new AshfangBzTask(),
            new EmptyChumcapBucketTask(),
            new EndermanPetFdTask(),
            new ExportableCarrotsTask(),

            // Dungeon tasks
            new M4Task(),
            new M5Task(),
            new M6Task(),
            new M7Task(),
            new M7KismetTask(),

            // Garden tasks
            new PestTask(),
            new FigTask(),

            // Misc tasks
            new ZealotsFdTask(),
            new RedMushroomTask(),
            new BrownMushroomTask(),
            new MyceliumTask(),
        ];
    }

    /// <summary>
    /// Returns all registered task instances.
    /// </summary>
    public IReadOnlyList<ProfitTask> Tasks => _tasks;

    /// <summary>
    /// Execute all tasks against the given parameters and return results sorted by profit.
    /// </summary>
    public async Task<List<TaskResult>> ExecuteAll(TaskParams parameters)
    {
        var all = await System.Threading.Tasks.Task.WhenAll(_tasks.Select(async task =>
        {
            try
            {
                return await task.Execute(parameters);
            }
            catch (Exception e)
            {
                return new TaskResult
                {
                    ProfitPerHour = 0,
                    Name = task.Name,
                    Message = $"Error calculating {task.Name}",
                    Details = e.ToString()
                };
            }
        }));
        return all.OrderByDescending(r => r.ProfitPerHour).ToList();
    }

    /// <summary>
    /// Returns metadata for all registered MethodTask instances (no execution needed).
    /// </summary>
    public List<MethodMetadata> GetMethodMetadata()
    {
        return _tasks.OfType<MethodTask>().Select(t => new MethodMetadata
        {
            Name = t.Name,
            Description = t.Description,
        }).ToList();
    }
}

public class MethodMetadata
{
    public string Name { get; set; }
    public string Description { get; set; }
}
