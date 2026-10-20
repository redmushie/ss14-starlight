using Content.Shared._Starlight.Doors.Systems;
using Content.Shared._Starlight.Maths;
using Content.Shared.Atmos;
using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Doors.Components;

/// <summary>
/// Added to a firelock that belongs to a group of directly-connected firelocks spanning a
/// multi-tile opening. Every firelock in a group is expected to operate together.
/// Populated by <see cref="SharedFirelockGroupSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true)]
// [AutoGenerateComponentPause]
public sealed partial class FirelockLeaderComponent : Component
{
    #region Groups

    /// <summary>
    /// Every firelock in this group, including the entity this component is on.
    /// </summary>
    [DataField, AutoNetworkedField] public HashSet<EntityUid> Members = new();

    #endregion

    #region Sensing (Server only)

    /*
     * A sensing island is effectively a conjoined set of tiles. For example, if a set of 3 adjacent firelocks
     * splits a vertical hallway in two, you will end up with two external sensing islands, the 3 tiles north of the
     * firelocks, and the 3 tiles south of the firelocks. The internal sensing island is always the firelocks themselves.
     */

    /// <summary>
    /// Whether sensing island should be redetermined before sensing.
    /// </summary>
    public bool IslandsInitialized = false;
    public bool IslandsPending = false;

    [DataField] public TimeSpan SensingIslandsLastCalculated = TimeSpan.Zero;

    /// <summary>
    /// When the sensing islands last sensed.
    /// </summary>
    [DataField] public TimeSpan SensingIslandsLastSensed = TimeSpan.Zero;

    /// <summary>
    /// Server-only list of sensing islands. Each island is a list of tile indices that together form one unit.
    /// These are used to scan for temperature and pressure.
    /// </summary>
    public List<SensingIsland> ExternalSensingIslands = new();

    /// <summary>
    /// Server-only list of tile indices that the firelock group occupies.
    /// </summary>
    public SensingIsland InternalSensingIsland = new() { Internal = true };

    #endregion
}

public record SensingIsland
{
    public Dictionary<Vector2i, SensingTile> Tiles = new();
    public bool Internal;
}

public record SensingTile(
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
    public Direction8 FirelockDirection = Direction8.None;

    /// <summary>
    /// Adjacent tiles that have firelocks in them that reference this tile.
    /// The <see cref="AtmosDirection"/> is Invalid for self-referencing tiles. For adjacent sensing tiles, the
    /// direction is facing from the sensing tile towards the firelock tile.
    /// </summary>
    public List<(Direction8, EntityUid, Vector2i)> SensingFirelocks = new();

    /// <summary>
    /// Adjacent sensing tiles that reference this tile as containing an affected firelock.
    /// </summary>
    public List<Vector2i> SensedTiles = new();

    /// <summary>
    /// The directions that islands can be formed in. E.g. if a tile east of this one does not have the West direction
    /// enabled, then they cannot form a sensing island together.
    /// </summary>
    public Direction8 IslandingDirections = Direction8.All;

    public bool IsFirelock => Firelock != null;
    public bool IsSensing => Firelock == null;
}
