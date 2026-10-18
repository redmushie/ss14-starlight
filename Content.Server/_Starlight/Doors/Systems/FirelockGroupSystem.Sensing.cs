using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Starlight.Doors.Components;
using Content.Shared._Starlight.Maths;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockGroupSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;

    private readonly TimeSpan _recalculateIslandsInterval = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _sensingInterval = TimeSpan.FromSeconds(1);
    private readonly Queue<Entity<FirelockLeaderComponent>> _recalculateIslandsQueue = new();

    #region Internal API

    private void DirtySensingIslands(Entity<FirelockLeaderComponent> group)
    {
        // Already dirty -> do nothing
        if (group.Comp.IslandsPending)
            return;

        // Dirty
        group.Comp.IslandsPending = true;
        _recalculateIslandsQueue.Enqueue(group);
    }

    #endregion
    #region Updating

    private void UpdateSensingGlobal(float frameTime)
    {
        // Recalculate islands of one queued group per tick.
        if (_recalculateIslandsQueue.TryDequeue(out var group))
        {
            Log.Info($"Recalculating islands for group: {group.Owner}");
            RecalculateSensingIslands(group);
            group.Comp.IslandsInitialized = true;
            group.Comp.IslandsPending = false;
        }

        // TODO: call UpdateSensing
    }

    private void UpdateSensing(float frameTime, Entity<FirelockLeaderComponent> group)
    {
        // Queue island recalculation if it's that time.
        if (RequiresIslandRecalculation(group))
        {
            _recalculateIslandsQueue.Enqueue(group);
            group.Comp.IslandsPending = true;
        }

        // If we haven't determined islands at all yet, we cannot sense, so exit here.
        if (!group.Comp.IslandsInitialized)
            return;

        // Check if we should sense.
        if (group.Comp.SensingIslandsLastSensed + _sensingInterval < _timing.CurTime)
            return;

        // Sense each island.
        group.Comp.SensingIslandsLastSensed = _timing.CurTime;
        foreach (var sensingIsland in group.Comp.ExternalSensingIslands)
        {
            var readings = SenseIsland(group, sensingIsland);

        }
    }

    private bool RequiresIslandRecalculation(Entity<FirelockLeaderComponent> group) =>
        !group.Comp.IslandsPending && (!group.Comp.IslandsInitialized ||
        group.Comp.SensingIslandsLastCalculated + _recalculateIslandsInterval < _timing.CurTime);

    private void RecalculateSensingIslands(Entity<FirelockLeaderComponent> group)
    {
        group.Comp.IslandsPending = false;
        group.Comp.SensingIslandsLastCalculated = _timing.CurTime;

        var airtightQuery = GetEntityQuery<AirtightComponent>();

        var sensingTiles = CollectSensingTiles(group, airtightQuery);
        var (internalIsland, externalIslands) = CollectSensingIslands(sensingTiles);

        group.Comp.ExternalSensingIslands = externalIslands;
        group.Comp.InternalSensingIsland = internalIsland;
    }

    #endregion
    #region Judging

    private SensingResult JudgeReading(
        Entity<FirelockLeaderComponent> group,
        SensorReading reading)
    {
        var judgement = new SensingResult();
        JudgeReadingPressure(ref group.Comp.SensingThresholds, ref reading, ref judgement);
        JudgeReadingTemperature(ref group.Comp.SensingThresholds, ref reading, ref judgement);
        return judgement;
    }

    private void JudgeReadingPressure(
        ref SensingThresholds thresholds,
        ref SensorReading reading,
        ref SensingResult judgement)
    {
        var permittedPressureDelta = 20 + (0.1f * reading.MaxPressure);
        // TODO: Also meet-in-the-middle for permitted delta maybe?
        // TODO: Debounce permittedPressureDelta / permittedTemperatureDelta, add activation and deactivation margins.

        // If the delta is too small, we will not trigger.
        if (reading.DeltaPressure < permittedPressureDelta)
            return;

        // TODO: For now, no direct delta states.
        judgement.DeltaPressure = SensorSeverity.Safe;

        judgement.LowPressure = JudgeSeverity(-reading.MinPressure,
            warn: -thresholds.LowPressureWarning,
            danger: -thresholds.LowPressureDanger);

        judgement.HighPressure = JudgeSeverity(reading.MaxPressure,
            warn: thresholds.HighPressureWarning,
            danger: thresholds.HighPressureDanger,
            death: thresholds.HighPressureDeath);
    }

    private void JudgeReadingTemperature(
        ref SensingThresholds thresholds,
        ref SensorReading reading,
        ref SensingResult judgement)
    {
        var permittedTemperatureDelta = 0.2f * reading.MaxTemperature;
        // TODO: Also meet-in-the-middle for permitted delta maybe?
        // TODO: Debounce permittedPressureDelta / permittedTemperatureDelta, add activation and deactivation margins.

        // If the delta is too small, we will not trigger.
        if (reading.DeltaTemperature < permittedTemperatureDelta)
            return;

        // TODO: For now, no direct delta states.
        judgement.DeltaTemperature = SensorSeverity.Safe;

        judgement.LowTemperature = JudgeSeverity(-reading.MinTemperature,
            warn: -thresholds.LowTemperatureWarning,
            danger: -thresholds.LowTemperatureDanger,
            death: -thresholds.LowTemperatureDeath);

        judgement.HighTemperature = JudgeSeverity(reading.MaxTemperature,
            warn: thresholds.HighTemperatureWarning,
            danger: thresholds.HighTemperatureDanger,
            death: thresholds.HighTemperatureDeath);
    }

    /// <summary>
    /// Given a reading and thresholds, checks if the reading exceeds each of the thresholds, and returns the
    /// corresponding severity. Tip: You can pass all-negative values to use for low and minimums rather than highs and
    /// maximums.
    /// </summary>
    private SensorSeverity JudgeSeverity(float reading, float? warn = null, float? danger = null, float? death = null)
    {
        if (reading > death)
            return SensorSeverity.Death;
        if (reading > danger)
            return SensorSeverity.Danger;
        if (reading > warn)
            return SensorSeverity.Warning;
        return SensorSeverity.Safe;
    }

    #endregion
    #region Sensing

    /// <summary>
    /// Sense one island.
    /// </summary>
    /// <param name="group"></param>
    /// <param name="island"></param>
    /// <returns></returns>
    private SensorReading SenseIsland(
        Entity<FirelockLeaderComponent> group,
        SensingIsland island)
    {
        var xform = Transform(group.Owner);
        var airtightQuery = GetEntityQuery<AirtightComponent>();
        var result = new SensorReading();

        if (!HasComp<GridAtmosphereComponent>(xform.ParentUid))
            return result;

        var grid = Comp<MapGridComponent>(xform.ParentUid);
        var (minPressure, maxPressure) = (float.MaxValue, float.MinValue);
        var (minTemperature, maxTemperature) = (float.MaxValue, float.MinValue);

        // We bulk request the gas mixtures in each sensing tile of this island.
        var positions
            = island.Tiles.Keys.ToList();
        var gases = _atmos.GetTileMixtures(xform.ParentUid, xform.MapUid, positions);
        if (gases == null)
            return result;

        for (var i = 0; i < positions.Count; i++)
        {
            var pos = positions[i];
            var tile = island.Tiles[pos];
            var gas = gases[i];

            // If there is nothing to sense, do nothing, I guess.
            if (gas == null)
                continue;

            // Sensing tiles can:
            // - Be external to any firelock. Direction points from the tile to the firelock.
            // - Be a tile containing one of our full firelocks. Direction is none.
            // - Be a tile containing one of our edge firelocks. Direction is the way the firelock faces.

            // If this tile is external to the firelock, and is not containing an edge firelock,
            // then we should check if the direction of this tile towards the sensing firelock(s) is blocked.
            // If at least one is not blocked, we accept the tile's atmospheric reading.
            var sensingDirections = tile.SensingFirelocks
                .Select(pair => pair.Item1)
                .Aggregate((dir1, dir2) => dir1 | dir2);

            // Of all our firelocks that can sense this tile, determine which directions are not blocked
            // by the contents of this tile. (E.g. a solid wall blocks all directions, but a directional window might
            // block the sensing for one firelock but not another).
            var unblockedDirections = GetUnblockedAirtightDirections(
                _map.GetAnchoredEntities(xform.ParentUid, grid, pos), sensingDirections, airtightQuery);

            // If zero sensing directions remain, we cannot sense this tile, so skip.
            if (unblockedDirections == Direction8.None)
                continue;

            // At this point we know for sure at least one firelock is able to sense this tile.
            // Time to sense!
            minPressure = Math.Min(minPressure, gas.Pressure);
            maxPressure = Math.Max(maxPressure, gas.Pressure);
            minTemperature = Math.Min(minTemperature, gas.Temperature);
            maxTemperature = Math.Max(maxTemperature, gas.Temperature);
        }

        return result;
    }

    /// <summary>
    /// Given a set of atmos directions, check if the enti
    /// </summary>
    /// <param name="tileEntities"></param>
    /// <param name="directions"></param>
    /// <param name="airtightQuery"></param>
    /// <returns></returns>
    private Direction8 GetUnblockedAirtightDirections(
        IEnumerable<EntityUid> tileEntities,
        Direction8 directions,
        EntityQuery<AirtightComponent> airtightQuery)
    {
        foreach (var ent in tileEntities)
        {
            if (!airtightQuery.TryGetComponent(ent, out var airtight) || !airtight.AirBlocked)
                continue;

            // Leaves only unblocked air directions.
            directions &= ~(airtight.AirBlockedDirection.ToDirection8());
        }
        return directions;
    }

    #endregion
    #region Island management

    /// <summary>
    /// Given a firelock group, collects all tiles eligible for sensing, grouped by coordinate.
    /// </summary>
    private Dictionary<Vector2i, SensingTile> CollectSensingTiles(
        Entity<FirelockLeaderComponent> group,
        EntityQuery<AirtightComponent> airtightQuery)
    {
        Dictionary<Vector2i, SensingTile> tiles = new();

        foreach (var memberId in group.Comp.Members)
        {
            if (!airtightQuery.TryGetComponent(memberId, out var airtight))
                continue;

            var xform = Transform(memberId);
            var grid = Comp<MapGridComponent>(xform.ParentUid);
            var pos = _map.CoordinatesToTile(xform.ParentUid, grid, xform.Coordinates);


            // Get or create the tile data.
            var firelockTile = tiles.GetValueOrDefault(pos, new SensingTile(pos));
            firelockTile.Firelock = memberId;
            firelockTile.FirelockDirection = ((AtmosDirection)airtight.CurrentAirBlockedDirection).ToDirection8();
            tiles[pos] = firelockTile;

            var firelockDirection = firelockTile.FirelockDirection;
            var edgeFirelock = firelockDirection is not Direction8.All and not Direction8.None;

            firelockTile.SensedTiles.Add(pos);
            if (edgeFirelock)
            {
                // If the firelock direction is specific, that means we are dealing with an edge firelock.
                // Edge firelocks are able to detect the contents of their own tile under basically all conditions
                // except full tile blocking. Hence, the sensing direction we store is All.
                firelockTile.SensingFirelocks.Add((Direction8.All, pos));

                /*
                 * Edge firelocks also have a unique sensing island pattern. Assuming a north-blocking edge firelock,
                 * the overlapping tile, which is thus implicitly 'south' of the edge firelock, will not be able to
                 * pair with north, north-east and north-west directions.
                 */
                var frontCone = firelockDirection.TurnCounterClockwise() | firelockDirection | firelockDirection.TurnClockwise();
                firelockTile.IslandingDirections &= ~frontCone; // Disable the obscured tile from islanding with the front.

                /*
                 * In addition, the tile directly obscured by the blocked side will have its south, south-east and
                 * south-west directions blocked. This is applied during the direction-loop.
                 */
            }
            else
            {
                // Full firelock still senses its own tile, and works unless the tile is fully blocked.
                firelockTile.SensedTiles.Add(pos);
                firelockTile.SensingFirelocks.Add((Direction8.All, pos));
                firelockTile.IslandingDirections = Direction8.None;
            }

            List<Vector2i> adjacentTiles = new(4);
            List<Direction8> directions = new(4);

            foreach (var dir in Direction8Extensions.Cardinals)
            {
                directions.Add(dir);
                adjacentTiles.Add(pos.Offset(dir));
            }

            /*
             * Add sensing tiles for each direction.
             */
            for (var i = 0; i < directions.Count; i++)
            {
                var dir = directions[i];
                var oppositeDir = dir.GetOpposite();
                var adjacentPos = adjacentTiles[i];

                // If we are an edge firelock, and this is the tile in our 'protected' direction (e.g. we face north,
                // and this is the tile south of us), then we need **not** add any sensing tile, as it is already
                // covered by our firelockTile.
                if (oppositeDir == firelockDirection)
                    continue;

                // Create a new sensing tile and add it to the dictionary.
                var adjacentTile = tiles.GetValueOrDefault(adjacentPos, new SensingTile(adjacentPos));

                // If we are an edge firelock (dir != All) and the tile in this direction is the tile that is the tile
                // that the edge firelock is facing, then we have to prevent said tile from pairing in *our* direction.
                if (dir == firelockDirection)
                    adjacentTile.IslandingDirections &= ~GetDirectionCone45(oppositeDir);

                // Tiles adjacent to a solid firelock may not diagonally island with one another.
                // This effectively blocks the two diagonals facing the firelock tile.
                if (!edgeFirelock)
                    adjacentTile.IslandingDirections &= ~(oppositeDir.TurnCounterClockwise() | oppositeDir.TurnClockwise());

                // TODO: this naively adds links between our tile and the adjacent tile, regardless of whether the IslandingDirections permit this. probably not an issue though
                adjacentTile.SensingFirelocks.Add((oppositeDir, pos));
                firelockTile.SensedTiles.Add(adjacentPos);
                tiles[adjacentPos] = adjacentTile;
            }
        }

        return tiles;
    }

    private (SensingIsland, List<SensingIsland>) CollectSensingIslands(Dictionary<Vector2i, SensingTile> tiles)
    {
        HashSet<Vector2i> visitedTiles = new();
        HashSet<Vector2i> remainingTiles = new(tiles.Keys);
        Queue<Vector2i> toVisit = new();

        List<SensingIsland> completedIslands = new();
        var currentIsland = new SensingIsland();
        var internalIsland = new SensingIsland { Internal = true };

        void QueueNextIsland()
        {
            if (remainingTiles.Count == 0)
                return;

            var first = remainingTiles.First();
            remainingTiles.Remove(first);
            toVisit.Enqueue(first);
        }
        QueueNextIsland();

        while (toVisit.Count > 0 || remainingTiles.Count > 0)
        {
            // We start a new island when no visitable tiles remain on this island, but not all tiles were visited yet.
            if (toVisit.Count == 0)
            {
                completedIslands.Add(currentIsland);
                currentIsland = new SensingIsland();
                QueueNextIsland();
                continue;
            }

            // Grab the next tile.
            var currentPos = toVisit.Dequeue();
            visitedTiles.Add(currentPos);
            remainingTiles.Remove(currentPos);
            var currentTile = tiles[currentPos];

            // If the tile is part of the internal sensing island, add it to that island, no need to floodfill.
            if (currentTile.IsFirelock && currentTile.FirelockDirection == Direction8.All)
            {
                internalIsland.Tiles.Add(currentPos, currentTile);
                continue;
            }

            // Add it to our island.
            currentIsland.Tiles.Add(currentPos, currentTile);

            // Queue adjacent that haven't already been visited.
            foreach (var direction in Direction8Extensions.AllDirections)
            {
                var adjacentPos = currentPos.Offset(direction);
                if (visitedTiles.Contains(adjacentPos)) continue;
                if (!tiles.TryGetValue(adjacentPos, out var adjacentTile)) continue;
                if (toVisit.Contains(adjacentPos)) continue;

                // Check if current tile is allowed to island with the adjacent tile, and vice versa.
                if ((currentTile.IslandingDirections & direction) == 0) continue;
                if ((adjacentTile.IslandingDirections & direction.GetOpposite()) == 0) continue;

                // Add it to our islanding queue.
                toVisit.Enqueue(adjacentPos);
            }
        }

        // Wrap up the last island.
        if (currentIsland.Tiles.Count > 0)
            completedIslands.Add(currentIsland);

        return (internalIsland, completedIslands);
    }

    private Direction8 GetDirectionCone45(Direction8 direction) =>
        direction.TurnCounterClockwise() | direction | direction.TurnClockwise();

    #endregion
}

record struct SensorReading(float MinPressure, float MaxPressure, float MinTemperature, float MaxTemperature)
{
    public float MinPressure = MinPressure;
    public float MaxPressure = MaxPressure;
    public float MinTemperature = MinTemperature;
    public float MaxTemperature = MaxTemperature;

    public float DeltaPressure = MaxPressure - MinPressure;
    public float DeltaTemperature = MaxTemperature - MinTemperature;
}

enum SensorSeverity
{
    Safe,
    Warning,
    Danger,
    Death
}

record struct SensingResult()
{
    public SensorSeverity DeltaTemperature = SensorSeverity.Safe;
    public SensorSeverity LowTemperature = SensorSeverity.Safe;
    public SensorSeverity HighTemperature = SensorSeverity.Safe;

    public SensorSeverity DeltaPressure = SensorSeverity.Safe;
    public SensorSeverity LowPressure = SensorSeverity.Safe;
    public SensorSeverity HighPressure = SensorSeverity.Safe;
}
