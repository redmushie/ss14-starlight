// ReSharper disable once CheckNamespace
namespace Content.Shared.Storage.Components;

public sealed partial class MagnetPickupComponent
{
    /// <summary>
    /// Controlling whether the magnet needs to be equipped to work. This means worn in an equipment slot, not directly
    /// or indirectly carried.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("requiresEquipping")]
    public bool RequiresEquipping = true;

    /// <summary>
    /// Controlling whether the magnet needs to be carried to work. This applies to nested carrying, like in bags
    /// or other containers.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("requiresCarrying")]
    public bool RequiresCarrying = true;
}
