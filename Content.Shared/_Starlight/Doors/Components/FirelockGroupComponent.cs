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

    /// <summary>
    /// Server-only list of sensing islands. Each island is a list of tile indices that together form one unit.
    /// These are used to scan for temperature and pressure.
    /// </summary>
    public List<List<Vector2i>> ExternalSensingIslands = new();

    /// <summary>
    /// Server-only list of tile indices that the firelock group occupies.
    /// </summary>
    public List<Vector2i> InternalSensingIsland = new();

    #endregion
}
