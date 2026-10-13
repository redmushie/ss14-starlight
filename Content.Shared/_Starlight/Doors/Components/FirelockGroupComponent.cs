using Content.Shared._Starlight.Doors.Systems;
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
public sealed partial class FirelockGroupComponent : Component
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
    public bool SensingIslandsDirty = true;

    [DataField] public TimeSpan SensingIslandsLastCalculated = TimeSpan.Zero;

    /// <summary>
    /// When the sensing islands last sensed.
    /// </summary>
    [DataField] public TimeSpan SensingIslandsLastSensed = TimeSpan.Zero;

    /// <summary>
    /// Server-only list of sensing islands. Each island is a list of tile indices that together form one unit.
    /// These are used to scan for temperature and pressure.
    /// </summary>
    [DataField] public List<List<Vector2i>> ExternalSensingIslands = new();

    /// <summary>
    /// Server-only list of tile indices that the firelock group occupies.
    /// </summary>
    [DataField] public List<Vector2i> InternalSensingIsland = new();

    #endregion
}
