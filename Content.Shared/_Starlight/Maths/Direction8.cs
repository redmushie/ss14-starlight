using Content.Shared.Atmos;

namespace Content.Shared._Starlight.Maths;

/// <summary>
/// Similar to <see cref="Robust.Shared.Maths.Direction"/>, but has separate bits specifically for all 8 directions.
/// </summary>
[Flags]
public enum Direction8 : byte
{
    None = 0,

    North = 1,
    NorthEast = 2,
    East = 4,
    SouthEast = 8,
    South = 16,
    SouthWest = 32,
    West = 64,
    NorthWest = 128,

    Cardinals = North | East | South | West,
    Diagonals = NorthEast | SouthEast | SouthWest | NorthWest,

    NorthFacing = NorthWest | North | NorthEast,
    EastFacing = NorthEast | East | SouthEast,
    SouthFacing = SouthEast | South | SouthWest,
    WestFacing = SouthWest | West | NorthWest,

    All = North | NorthEast | East | SouthEast | South | SouthWest | West | NorthWest
}

public static class Direction8Extensions
{

    public static Direction8 GetOpposite(this Direction8 direction)
        => (Direction8)(((byte)direction << 4) | ((byte)direction >> 4));

    public static Direction8 TurnClockwise(this Direction8 direction, int steps = 1)
    {
        steps %= 8;
        for (var i = 0; i < steps; i++)
        {
            var before = (byte)direction;
            var after = (byte)((before << 1) | (before >> 7));
            direction = (Direction8)after;
        }
        return direction;
    }

    public static Direction8 TurnCounterClockwise(this Direction8 direction, int steps = 1)
    {
        steps %= 8;
        for (var i = 0; i < steps; i++)
        {
            var before = (byte)direction;
            var after = (byte)((before >> 1) | (before << 7));
            direction = (Direction8)after;
        }
        return direction;
    }

    public static AtmosDirection ToAtmosDirection(this Direction8 direction) =>
        direction switch
        {
            Direction8.None => AtmosDirection.Invalid,
            Direction8.North => AtmosDirection.North,
            Direction8.NorthEast => AtmosDirection.NorthEast,
            Direction8.East => AtmosDirection.East,
            Direction8.SouthEast => AtmosDirection.SouthEast,
            Direction8.South => AtmosDirection.South,
            Direction8.SouthWest => AtmosDirection.SouthWest,
            Direction8.West => AtmosDirection.SouthWest,
            Direction8.NorthWest => AtmosDirection.NorthWest,
            Direction8.All => AtmosDirection.All,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };

    public static Direction8 ToDirection8(this AtmosDirection atmosDirection) =>
        atmosDirection switch
        {
            AtmosDirection.Invalid => Direction8.None,
            AtmosDirection.North => Direction8.North,
            AtmosDirection.NorthEast => Direction8.NorthEast,
            AtmosDirection.East => Direction8.East,
            AtmosDirection.SouthEast => Direction8.SouthEast,
            AtmosDirection.South => Direction8.South,
            AtmosDirection.SouthWest => Direction8.SouthWest,
            AtmosDirection.West => Direction8.West,
            AtmosDirection.NorthWest => Direction8.NorthWest,
            AtmosDirection.All => Direction8.All,
            _ => throw new ArgumentOutOfRangeException(nameof(atmosDirection), atmosDirection, null)
        };

    // ReSharper disable once InconsistentNaming
    public static Vector2i ToIntVec(this Direction8 direction) =>
        direction switch
        {
            Direction8.North => (0, -1),
            Direction8.NorthEast => (1, -1),
            Direction8.East => (1, 0),
            Direction8.SouthEast => (1, 1),
            Direction8.South => (0, 1),
            Direction8.SouthWest => (-1, 1),
            Direction8.West => (-1, 0),
            Direction8.NorthWest => (-1, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(Direction8), direction, null)
        };

    public static Vector2i Offset(this Vector2i pos, Direction8 dir) => pos + dir.ToIntVec();
}
