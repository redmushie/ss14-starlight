using System.Linq;
using Content.Server.Atmos.Components;
using Content.Shared._Starlight.Doors.Components;
using Content.Shared._Starlight.Doors.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Doors.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockGroupSystem
{
    [Dependency] private IGameTiming _timing = default!;

    private void UpdateSensing(float frameTime, Entity<FirelockGroupComponent> groupEnt)
    {
    }

    private void RecalculateSensingIslands(Entity<FirelockGroupComponent> group)
    {
        group.Comp.SensingIslandsDirty = false;
        group.Comp.SensingIslandsLastCalculated = _timing.CurTime;

        var airtightQuery = GetEntityQuery<AirtightComponent>();

        var sensingTiles = CollectSensingTiles(group, airtightQuery);
        var sensingIslands = CollectSensingIslands(sensingTiles);
    }

    /// <summary>
    /// Given a firelock group, collects all tiles eligible for sensing, grouped by coordinate.
    /// </summary>
    private Dictionary<Vector2i, SensingTile> CollectSensingTiles(
        Entity<FirelockGroupComponent> group,
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

            List<Vector2i> adjacentTiles = new(4);
            List<AtmosDirection> directions = new(4);

            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var dir = (AtmosDirection)(1 << i);
                directions.Add(dir);
                adjacentTiles.Add(pos.Offset(dir));
            }

            // Get or create the tile data.
            var firelockTile = tiles.GetValueOrDefault(pos, new SensingTile(pos));
            firelockTile.Firelock = memberId;
            firelockTile.FirelockDirection = (AtmosDirection)airtight.CurrentAirBlockedDirection;
            tiles[pos] = firelockTile;

            var firelockDirection = firelockTile.FirelockDirection;

            // TODO: Solid firelocks should only have non-diagonal islanding directions?

            // Detect edge firelocks.
            if (firelockDirection is not AtmosDirection.All and not AtmosDirection.Invalid)
            {
                // If the tile doesn't contain a full blocker, it becomes self-referencing in terms of sensing tiles.
                firelockTile.SensedTiles.Add(pos);
                firelockTile.SensingFirelocks.Add((AtmosDirection.Invalid, pos));

                /*
                 * Edge firelocks also have a unique sensing island pattern. Assuming a north-blocking edge firelock,
                 * the overlapping tile, which is thus implicitly 'south' of the edge firelock, will not be able to
                 * pair with north, north-east and north-west directions.
                 */
                var frontCone = GetDirectionCone45(firelockDirection);
                firelockTile.IslandingDirections &= ~frontCone; // Disable the obscured tile from islanding with the front.

                /*
                 * In addition, the tile directly obscured by the blocked side will have its south, south-east and
                 * south-west directions blocked. This is applied during the direction-loop.
                 */
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

                // TODO: this naively adds links between our tile and the adjacent tile, regardless of whether the IslandingDirections permit this. probably not an issue though
                adjacentTile.SensingFirelocks.Add((dir.GetOpposite(), pos));
                firelockTile.SensedTiles.Add(adjacentPos);
                tiles[adjacentPos] = adjacentTile;
            }
        }

        return tiles;
    }

    private List<FirelockSensingIsland> CollectSensingIslands(Dictionary<Vector2i, SensingTile> tiles)
    {
        HashSet<Vector2i> visitedTiles = new();
        HashSet<Vector2i> remainingTiles = new(tiles.Keys);
        Queue<Vector2i> toVisit = new();

        List<FirelockSensingIsland> completedIslands = new();
        var currentIsland = new FirelockSensingIsland();

        void QueueNextIsland()
        {
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
                currentIsland = new FirelockSensingIsland();
                QueueNextIsland();
                continue;
            }

            // Grab the next tile.
            var currentPos = toVisit.Dequeue();
            visitedTiles.Add(currentPos);
            remainingTiles.Remove(currentPos);
            var currentTile = tiles[currentPos];

            // Add it to our island.
            currentIsland.Tiles.Add(currentPos, tiles[currentPos]);

            // Queue adjacent that haven't already been visited.
            var adjacents = GetAdjacentTiles(currentPos);
            foreach (var (direction, adjacentPos) in adjacents)
            {
                if (visitedTiles.Contains(adjacentPos)) continue;
                if (!tiles.TryGetValue(adjacentPos, out var adjacentTile)) continue;

                // Check if current tile is allowed to island with the adjacent tile, and vice versa.
                if ((currentTile.IslandingDirections & direction) == 0) continue;
                if ((adjacentTile.IslandingDirections & direction.GetOpposite()) == 0) continue;

                // Add it to our islanding queue.
                toVisit.Enqueue(adjacentPos);
            }
        }

        completedIslands.Add(currentIsland);
        return completedIslands;
    }

    private (AtmosDirection, Vector2i)[] GetAdjacentTiles(Vector2i coord) => new[]
        {
            (AtmosDirection.North, coord + (0, -1)),
            (AtmosDirection.NorthEast, coord + (1, -1)),
            (AtmosDirection.East, coord + (1, 0)),
            (AtmosDirection.SouthEast, coord + (1, 1)),
            (AtmosDirection.South, coord + (0, 1)),
            (AtmosDirection.SouthWest, coord + (-1, 1)),
            (AtmosDirection.West, coord + (-1, 0)),
            (AtmosDirection.NorthWest, coord + (1, -1)),
        };

    private AtmosDirection GetDirectionCone45(AtmosDirection direction) =>
        direction | GetDirectionLeft(direction) | GetDirectionRight(direction);

    private AtmosDirection GetDirectionLeft(AtmosDirection direction) =>
        direction switch
        {
            AtmosDirection.North => AtmosDirection.NorthWest,
            AtmosDirection.NorthWest => AtmosDirection.West,
            AtmosDirection.West => AtmosDirection.SouthWest,
            AtmosDirection.SouthWest => AtmosDirection.South,
            AtmosDirection.South => AtmosDirection.SouthEast,
            AtmosDirection.SouthEast => AtmosDirection.East,
            AtmosDirection.East => AtmosDirection.NorthEast,
            AtmosDirection.NorthEast => AtmosDirection.North,
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

    private AtmosDirection GetDirectionRight(AtmosDirection direction) =>
        direction switch
        {
            AtmosDirection.North => AtmosDirection.NorthEast,
            AtmosDirection.NorthEast => AtmosDirection.East,
            AtmosDirection.East => AtmosDirection.SouthEast,
            AtmosDirection.SouthEast => AtmosDirection.South,
            AtmosDirection.South => AtmosDirection.SouthWest,
            AtmosDirection.SouthWest => AtmosDirection.West,
            AtmosDirection.West => AtmosDirection.NorthWest,
            AtmosDirection.NorthWest => AtmosDirection.North,
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

    private bool HasAirtightBlocker(IEnumerable<EntityUid> enumerable, AtmosDirection dir,
        EntityQuery<AirtightComponent> airtightQuery)
    {
        foreach (var ent in enumerable)
        {
            if (!airtightQuery.TryGetComponent(ent, out var airtight) || !airtight.AirBlocked)
                continue;

            if ((airtight.AirBlockedDirection & dir) == dir)
                return true;
        }

        return false;
    }
}

[Serializable, NetSerializable]
record SensingTile(
    Vector2i Tile,
    EntityUid? Firelock = null)
{
    public Vector2i Tile = Tile;

    /// <summary>
    /// The firelock contained in this tile, if any.
    /// </summary>
    public EntityUid? Firelock = Firelock;

    /// <summary>
    /// If this tile contains a firelock, contains the firelocks' air blocking direction when closed. Does NOT
    /// check if it is CURRENTLY closed.
    /// </summary>
    public AtmosDirection FirelockDirection = AtmosDirection.Invalid;

    /// <summary>
    /// Adjacent tiles that have firelocks in them that reference this tile.
    /// The <see cref="AtmosDirection"/> is Invalid for self-referencing tiles. For adjacent sensing tiles, the
    /// direction is facing from the sensing tile towards the firelock tile.
    /// </summary>
    public List<(AtmosDirection, Vector2i)> SensingFirelocks = new();

    /// <summary>
    /// Adjacent sensing tiles that reference this tile as containing an affected firelock.
    /// </summary>
    public List<Vector2i> SensedTiles = new();

    /// <summary>
    /// The directions that islands can be formed in. E.g. if a tile east of this one does not have the West direction
    /// enabled, then they cannot form a sensing island together.
    /// </summary>
    public AtmosDirection IslandingDirections = AtmosDirection.All;

    public bool IsFirelock => Firelock != null;
    public bool IsSensing => Firelock == null;
}

public record struct FirelockDirection(EntityUid Firelock, AtmosDirection Direction);

record FirelockSensingIsland
{
    public Dictionary<Vector2i, SensingTile> Tiles = new();
}
