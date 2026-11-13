using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Doors.Systems;
using Content.Shared._Starlight.Doors.Components;
using Content.Shared.Doors.Components;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockLeaderSystem
{
    [Dependency] private FirelockSystem _firelock = default!;

    private EntityQuery<AirtightComponent> _airtightQuery;
    private EntityQuery<FirelockComponent> _firelockQuery;
    private EntityQuery<FirelockFollowerComponent> _followerQuery;

    private int _groupCount = 3;
    private int _groupUpdatesPerTick = 1;
    private readonly Queue<Entity<FirelockLeaderComponent>> _groupUpdateQueue = new();

    public override void Initialize()
    {
        base.Initialize();
        _airtightQuery = GetEntityQuery<AirtightComponent>();
        _firelockQuery = GetEntityQuery<FirelockComponent>();
        _followerQuery = GetEntityQuery<FirelockFollowerComponent>();
    }

    public override void Update(float frameTime)
    {
        // Recalculate dirtied islands.
        UpdateIslands(frameTime);

        /*
         * TODO: Move this crap below to the Sensing partial, called from UpdateSensing
         */

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
        var now = _timing.CurTime;
        for (var i = 0; i < _groupUpdatesPerTick; i++)
        {
            if (!_groupUpdateQueue.TryDequeue(out var group)) break;
            if (LifeStage(group) >= EntityLifeStage.Terminating) continue;
            UpdateGroup(group, now);
        }
    }

    private void UpdateGroup(Entity<FirelockLeaderComponent> leader, TimeSpan now)
    {
        // Queue island recalculation if it's that time.
        if (RequiresIslandRecalculation(leader))
        {
            _recalculateIslandsQueue.Enqueue(leader);
            leader.Comp.IslandsDirty = true;
        }

        // If the islands are not ready or outdated, do NOT sense yet.
        if (!leader.Comp.IslandsInitialized || leader.Comp.IslandsDirty)
            return;

        // Check if we should sense.
        if (leader.Comp.SensingIslandsNextSense > now)
            return;
        leader.Comp.SensingIslandsNextSense = now + _sensingInterval;

        if (!_firelockQuery.TryComp(leader, out var leaderFirelock)) return;

        // Phase 1 & 2. Sense and judge each individual island.
        var islands = leader.Comp.SensingIslands.ToDictionary(i => i.Id);
        Dictionary<int, SensingJudgements> islandJudgements = new();
        foreach (var (id, island) in islands)
        {
            var reading = SenseIsland(leader, island); // Phase 1
            var judgement = JudgeReading(ref leaderFirelock.Thresholds, reading); // Phase 2
            islandJudgements[id] = judgement;
        }

        // Phase 3: Calculate deltas between every two islands.
        // The key is a tuple of two island IDs.
        var islandDeltas = CalculateIslandDeltas(islands, islandJudgements);

        // Phase 4 and 5 are per firelock.
        var firelockStates = new Dictionary<EntityUid, FirelockState>();
        // var tileStates = new Dictionary<Vector2i, HologramState>();

        foreach (var followerId in leader.Comp.Members)
        {
            if (!_followerQuery.TryComp(followerId, out var follower)) continue;

            var maximum = new SensingJudgements();
            foreach (var delta in CalculateValidDeltaIds(follower.SensedIslands))
            {
                // TODO: This should be impossible maybe?
                if (!islandDeltas.TryGetValue(delta, out var judgement))
                    continue;

                if (islands[delta.Item1].Internal || islands[delta.Item2].Internal)
                    continue;
                maximum = maximum.Max(judgement);
            }

            // Get or create state for this firelock.
            if (!firelockStates.TryGetValue(followerId, out var firelockState))
                firelockStates[followerId] = firelockState = new FirelockState();

            firelockState.Judgement = maximum;
        }

        // Apply it.
        foreach (var (firelock, firelockState) in firelockStates)
            ApplyFirelockState(firelock, firelockState);
    }

    private void ApplyFirelockState(EntityUid id, FirelockState state)
    {
        if (!TryComp<FirelockComponent>(id, out var firelock)) return;
        var closingState = GetClosingState(state.Judgement);
        _firelock.AcceptState((id, firelock), state.Judgement, closingState);
    }

    private ClosingState GetClosingState(SensingJudgements judgements)
    {
        var (_, severity) = judgements.GetHighest();
        return severity switch
        {
            SensorSeverity.Safe => ClosingState.None,
            SensorSeverity.Warning => ClosingState.SoftClose,
            SensorSeverity.Danger => ClosingState.HardClose,
            SensorSeverity.Death => ClosingState.Bolt,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

}

record FirelockState
{
    public SensingJudgements Judgement = new();
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
