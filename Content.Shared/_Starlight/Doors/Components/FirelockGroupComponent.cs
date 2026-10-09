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
public sealed partial class FirelockGroupComponent : Component
{

    /// <summary>
    /// Every firelock in this group, including the entity this component is on.
    /// </summary>
    [DataField, AutoNetworkedField] public HashSet<EntityUid> Members = new();
}
