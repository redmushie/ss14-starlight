using Content.Server.Atmos.Components;
using Content.Server.Doors.Systems;
using Content.Shared._Starlight.Doors.Components;
using Content.Shared.Doors.Components;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockLeaderSystem
{
    [Dependency] private FirelockSystem _firelock = default!;

    private EntityQuery<AirtightComponent> _airtightQuery;

    private int _groupCount = 3;
    private int _groupUpdatesPerTick = 1;
    private readonly Queue<Entity<FirelockLeaderComponent>> _groupUpdateQueue = new();

    public override void Initialize()
    {
        base.Initialize();
        _airtightQuery = GetEntityQuery<AirtightComponent>();
    }

    public override void Update(float frameTime)
    {
        // Update global sensing things. This recalculates islands, one group per tick.
        UpdateSensingGlobal(frameTime);

        // Refill the update queue if empty.
        if (_groupUpdateQueue.Count == 0)
        {
            var query = EntityQueryEnumerator<FirelockLeaderComponent>();
            while (query.MoveNext(out var uid, out var leader))
            {
                if (LifeStage(uid) >= EntityLifeStage.Terminating)
                    continue;

                _groupUpdateQueue.Enqueue((uid, leader));
            }

            _groupCount = _groupUpdateQueue.Count;
            _groupUpdatesPerTick = (int)Math.Ceiling((double)_groupCount / _timing.TickRate);
        }

        // Update groups.
        for (var i = 0; i < _groupUpdatesPerTick; i++)
        {
            if (!_groupUpdateQueue.TryDequeue(out var group)) break;
            if (LifeStage(group) >= EntityLifeStage.Terminating) continue;
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
        if (group.Comp.SensingIslandsLastSensed + _sensingInterval > _timing.CurTime)
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

        _firelock.AcceptEnvironmentReadings((id, firelock), newState.Readings);
    }

    private (Dictionary<EntityUid, FirelockState> FirelockStates, Dictionary<Vector2i, HologramState> TileStates) CalculateStates(Entity<FirelockLeaderComponent> group)
    {
        var firelockStates = new Dictionary<EntityUid, FirelockState>();
        var tileStates = new Dictionary<Vector2i, HologramState>();

        foreach (var sensingIsland in group.Comp.ExternalSensingIslands)
        {
            var reading = SenseIsland(group, sensingIsland);

            // Judge the sensed heat/pressure parameters against the group's thresholds.
            // TODO clean up

            // Convert the threshold states to light states for firelocks.
            var tileHoloState = ToHologramState(reading);

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

                    firelockState.Readings.Add(reading);
                }
            }
        }

        return (firelockStates, tileStates);
    }

    #region Judgement-to-State Conversion

    private HologramState ToHologramState(SensingReading reading)
    {
        // TODO: implement hologram state determining
        return HologramState.None;
    }

    #endregion

}

record FirelockState
{
    public List<SensingReading> Readings = new();
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
