using System.Linq;
using Content.Shared._Starlight.Doors.Components;
using Content.Shared._Starlight.Maths;
using Content.Shared.Atmos;
using Orleans.Concurrency;
using Robust.Shared.Map.Components;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockLeaderSystem
{

    private readonly TimeSpan _recalculateIslandsInterval = TimeSpan.FromMinutes(2);
    private readonly Queue<Entity<FirelockLeaderComponent>> _recalculateIslandsQueue = new();

    /// <summary>
    /// Update method for the sensing islands subsection.
    /// </summary>
    private void UpdateIslands(float frameTime)
    {
        // Recalculate islands of one queued group per tick.
        if (_recalculateIslandsQueue.TryDequeue(out var group))
        {
            Log.Info($"Recalculating islands for group: {group.Owner}");
            RecalculateSensingIslands(group);
            group.Comp.IslandsInitialized = true;
            group.Comp.IslandsDirty = false;
        }
    }

    private void DirtySensingIslands(Entity<FirelockLeaderComponent> group)
    {
        if (group.Comp.IslandsDirty)
            return;

        group.Comp.IslandsDirty = true;
        _recalculateIslandsQueue.Enqueue(group);
    }

    private bool RequiresIslandRecalculation(Entity<FirelockLeaderComponent> group) =>
        !group.Comp.IslandsDirty && (!group.Comp.IslandsInitialized ||
                                     group.Comp.SensingIslandsLastCalculated + _recalculateIslandsInterval <
                                     _timing.CurTime);

    private void RecalculateSensingIslands(Entity<FirelockLeaderComponent> group)
    {
        group.Comp.IslandsDirty = false;
        group.Comp.SensingIslandsLastCalculated = _timing.CurTime;

        var sensingTiles = CollectSensingTiles(group);
        var (internalIsland, externalIslands) = CollectSensingIslands(sensingTiles);
        group.Comp.ExternalSensingIslands = externalIslands;
        group.Comp.InternalSensingIsland = internalIsland;

        UpdateFollowerIslands(group);
    }

    /// <summary>
    /// Given a firelock group, collects all tiles eligible for sensing, grouped by coordinate.
    /// </summary>
    private Dictionary<Vector2i, SensingTile> CollectSensingTiles(
        Entity<FirelockLeaderComponent> group)
    {
        Dictionary<Vector2i, SensingTile> tiles = new();

        foreach (var memberId in group.Comp.Members)
        {
            if (!_airtightQuery.TryComp(memberId, out var airtight) ||
                !_firelockQuery.TryComp(memberId, out var firelock))
                continue;

            var xform = Transform(memberId);
            var grid = Comp<MapGridComponent>(xform.ParentUid);
            var pos = _map.CoordinatesToTile(xform.ParentUid, grid, xform.Coordinates);

            // Get or create the tile data.
            var firelockTile = tiles.GetValueOrDefault(pos, new SensingTile(pos));
            firelockTile.Firelock = memberId;
            firelockTile.FirelockDirection = ((AtmosDirection)airtight.CurrentAirBlockedDirection).ToDirection8();
            firelockTile.SenseLowTemperature |= firelock.SensesLowTemperature;
            firelockTile.SenseHighTemperature |= firelock.SensesHighTemperature;
            firelockTile.SenseLowPressure |= firelock.SensesLowPressure;
            firelockTile.SenseHighPressure |= firelock.SensesHighPressure;
            tiles[pos] = firelockTile;

            var firelockDirection = firelockTile.FirelockDirection;
            var edgeFirelock = firelockDirection is not Direction8.All and not Direction8.None;

            firelockTile.SensedTiles.Add(pos);
            if (edgeFirelock)
            {
                // If the firelock direction is specific, that means we are dealing with an edge firelock.
                // Edge firelocks are able to detect the contents of their own tile under basically all conditions
                // except full tile blocking. Hence, the sensing direction we store is All.
                firelockTile.SensingFirelocks.Add((Direction8.All, memberId, pos));

                /*
                 * Edge firelocks also have a unique sensing island pattern. Assuming a north-blocking edge firelock,
                 * the overlapping tile, which is thus implicitly 'south' of the edge firelock, will not be able to
                 * pair with north, north-east and north-west directions.
                 */
                var frontCone = firelockDirection.TurnCounterClockwise() | firelockDirection |
                                firelockDirection.TurnClockwise();
                firelockTile.IslandingDirections &=
                    ~frontCone; // Disable the obscured tile from islanding with the front.

                /*
                 * In addition, the tile directly obscured by the blocked side will have its south, south-east and
                 * south-west directions blocked. This is applied during the direction-loop.
                 */
            }
            else
            {
                // Full firelock still senses its own tile, and works unless the tile is fully blocked.
                firelockTile.SensedTiles.Add(pos);
                firelockTile.SensingFirelocks.Add((Direction8.All, memberId, pos));
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
                    adjacentTile.IslandingDirections &=
                        ~(oppositeDir.TurnCounterClockwise() | oppositeDir.TurnClockwise());

                // TODO: this naively adds links between our tile and the adjacent tile, regardless of whether the IslandingDirections permit this. probably not an issue though
                adjacentTile.SensingFirelocks.Add((oppositeDir, memberId, pos));
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
        var id = 0;
        var internalIsland = new SensingIsland { Id = id++, Internal = true };
        var currentIsland = new SensingIsland { Id = id++ };

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
                if (currentIsland.Tiles.Count > 0) // TODO: this should not be necessary
                    completedIslands.Add(currentIsland);
                currentIsland = new SensingIsland { Id = id++ };
                QueueNextIsland();
                continue;
            }

            // Grab the next tile.
            var currentPos = toVisit.Dequeue();
            visitedTiles.Add(currentPos);
            remainingTiles.Remove(currentPos);
            var currentTile = tiles[currentPos];

            // If the tile is part of the internal sensing island, add it to that island, no need to floodfill.
            if (currentTile.Firelock.HasValue && currentTile.FirelockDirection == Direction8.All)
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

    private void UpdateFollowerIslands(Entity<FirelockLeaderComponent> leader)
    {
        // Wipe old follower islands.
        foreach (var followerId in leader.Comp.Members)
            if (_followerQuery.TryComp(followerId, out var follower))
                follower.SensedIslands.Clear();

        // Per island, we loop through the tiles and ensure that each follower that senses the tile is aware
        // of the island ID.
        foreach (var island in leader.Comp.SensingIslands)
        {
            foreach (var (_, tile) in island.Tiles)
            {
                foreach (var (_, sensingFirelockId, _) in tile.SensingFirelocks)
                {
                    if (!_followerQuery.TryComp(sensingFirelockId, out var follower))
                        continue;
                    follower.SensedIslands.Add(island.Id);
                }
            }
        }
    }

    private IEnumerable<(int, int)> CalculateValidDeltaIds(IReadOnlyCollection<int> ids)
    {
        foreach (var a in ids)
            foreach (var b in ids)
                if (a < b)
                    yield return (a, b);
    }

    private Direction8 GetDirectionCone45(Direction8 direction) =>
        direction.TurnCounterClockwise() | direction | direction.TurnClockwise();

}
