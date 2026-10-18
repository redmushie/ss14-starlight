using Content.Shared._Starlight.Doors.Components;
using Content.Shared.Doors.Components;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockGroupSystem
{

    private const int GroupUpdatesPerTick = 3;
    private readonly Queue<Entity<FirelockLeaderComponent>> GroupUpdateQueue = new();

    public override void Update(float frameTime)
    {
        // Update global sensing things. This recalculates islands, one group per tick.
        UpdateSensingGlobal(frameTime);

        // Refill the update queue if empty.
        if (GroupUpdateQueue.Count == 0)
        {
            var query = EntityQueryEnumerator<FirelockComponent, FirelockLeaderComponent>();
            while (query.MoveNext(out var uid, out var firelock, out var groupComp))
            {
                if (LifeStage(uid) >= EntityLifeStage.Terminating)
                    continue;

                UpdateGroup((uid, groupComp));
            }
        }

        // Update groups.
        for (var i = 0; i < GroupUpdatesPerTick; i++)
        {
            if (!GroupUpdateQueue.TryDequeue(out var group)) break;
            UpdateGroup(group);
        }
    }

    private void UpdateGroup(Entity<FirelockLeaderComponent> group)
    {
        // Queue island recalculation if it's that time.
        if (RequiresIslandRecalculation(group))
        {
            _recalculateIslandsQueue.Enqueue(group);
            group.Comp.IslandsPending = true;
        }

        // If the islands are not ready or outdated, do NOT sense yet.
        if (!group.Comp.IslandsInitialized || group.Comp.IslandsPending)
            return;

        // Check if we should sense.
        if (group.Comp.SensingIslandsLastSensed + _sensingInterval < _timing.CurTime)
            return;

        // Sense each island.
        group.Comp.SensingIslandsLastSensed = _timing.CurTime;

        // Calculate new firelock- and tile states.
        var (firelockStates, tileStates) = CalculateStates(group);

        // Apply it.
        foreach (var (firelock, firelockState) in firelockStates)
            ApplyFirelockState(firelock, firelockState);

        // TODO: apply tile states
    }

    private void ApplyFirelockState(EntityUid id, FirelockState newState)
    {
        if (!TryComp<FirelockComponent>(id, out var firelock)) return;


    }

    private (Dictionary<EntityUid, FirelockState> FirelockStates, Dictionary<Vector2i, HologramState> TileStates) CalculateStates(Entity<FirelockLeaderComponent> group)
    {
        var firelockStates = new Dictionary<EntityUid, FirelockState>();
        var tileStates = new Dictionary<Vector2i, HologramState>();

        foreach (var sensingIsland in group.Comp.ExternalSensingIslands)
        {
            var reading = SenseIsland(group, sensingIsland);

            // Judge the sensed heat/pressure parameters against the group's thresholds.
            var judgement = JudgeReading(group, reading);

            // Convert the threshold states to light states for firelocks.
            var tileHoloState = ToHologramState(judgement);
            var tempLightState = ToLightState(judgement.LowTemperature, judgement.HighTemperature);
            var pressureLightState = ToLightState(judgement.LowPressure, judgement.HighPressure);

            // Apply to each firelock- and tile state on the island.
            foreach (var sensingTile in sensingIsland.Tiles)
            {
                // Store the tile hologram state.
                tileStates[sensingTile.Key] = tileHoloState;

                foreach (var (_, sensingFirelock, _) in sensingTile.Value.SensingFirelocks)
                {
                    // Get or create state for this firelock.
                    if (!firelockStates.TryGetValue(sensingFirelock, out var firelockState))
                        firelockStates[sensingFirelock] = firelockState = new FirelockState();

                    // Set the firelock state to whichever scores worse.
                    firelockState.TemperatureLight = LightStateExtensions.Max(firelockState.TemperatureLight, tempLightState);
                    firelockState.PressureLight = LightStateExtensions.Max(firelockState.PressureLight, pressureLightState);
                }
            }
        }

        return (firelockStates, tileStates);
    }

    #region Judgement-to-State Conversion

    private HologramState ToHologramState(SensingResult result)
    {
        // TODO: implement hologram state determining
        return HologramState.None;
    }

    private LightState ToLightState(SensorSeverity low, SensorSeverity high)
    {
        // TODO: fix this abomination
        if (high == SensorSeverity.Death)
            return LightState.HighDeath;
        if (low == SensorSeverity.Death)
            return LightState.LowDeath;
        if (high == SensorSeverity.Danger)
            return LightState.HighDanger;
        if (low == SensorSeverity.Danger)
            return LightState.LowDanger;
        if (high == SensorSeverity.Warning)
            return LightState.HighWarn;
        if (low == SensorSeverity.Warning)
            return LightState.LowWarn;
        return LightState.None;
    }

    #endregion

}

record FirelockState
{
    public ClosingState Closing = ClosingState.None;

    public LightState TemperatureLight = LightState.None;
    public LightState PressureLight = LightState.None;
}

[Flags]
enum ClosingState : byte
{
    None = 0,
    SoftClose = 1,
    HardClose = 2,
    Bolt = 4
}

[Flags]
enum LightState : byte
{
    None = 0,
    LowWarn = 1 << 0,
    HighWarn = 1 << 1,
    LowDanger = 1 << 2,
    HighDanger = 1 << 3,
    LowDeath = 1 << 4,
    HighDeath = 1 << 5,
}

static class LightStateExtensions
{
    public static LightState Max(LightState a, LightState b) => a >= b ? a : b;
}

[Flags]
enum HologramState : byte
{
    None = 0,
    AirAlarmDanger = 1 << 0,
    Cold = 1 << 1,
    LowPressure = 1 << 2,
    Hot = 1 << 3,
    HighPressure = 1 << 4,
}
