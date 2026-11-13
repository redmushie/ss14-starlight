using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Doors.Components;

/// <summary>
/// Marks an entity as being part of a firelock group. Every member of the group has this component,
/// including the 'main' entity that holds the <see cref="FirelockLeaderComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class FirelockFollowerComponent : Component
{
    /// <summary>
    /// The entity that holds the <see cref="FirelockLeaderComponent"/>.
    /// </summary>
    [DataField, AutoNetworkedField] public EntityUid Leader = EntityUid.Invalid;

    /// <summary>
    /// The IDs of the islands sensed by this follower.
    /// </summary>
    public HashSet<int> SensedIslands = new();

}
