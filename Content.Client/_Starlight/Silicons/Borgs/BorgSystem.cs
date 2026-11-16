using Content.Shared.Silicons.Borgs.Components;

// ReSharper disable once CheckNamespace
namespace Content.Client.Silicons.Borgs;

public sealed partial class BorgSystem
{
    /// <summary>
    /// Sets the sprite states and colors used for the borg's mind-status eye indication.
    /// </summary>
    /// <param name="borg">The entity and component to modify.</param>
    /// <param name="hasMindState">The state to use when a local mind is present.</param>
    /// <param name="noMindState">The state to use when no mind is present.</param>
    /// <param name="shuntMindState">The state to use when an AI mind is shunted in. Falls back to <paramref name="hasMindState"/>.</param>
    /// <param name="remoteControlState">The state to use when remotely controlled. Falls back to <paramref name="hasMindState"/>.</param>
    /// <seealso cref="BorgChassisComponent.HasMindState"/>
    /// <seealso cref="BorgChassisComponent.NoMindState"/>
    /// <seealso cref="BorgChassisComponent.ShuntMindState"/>
    /// <seealso cref="BorgChassisComponent.RemoteControlState"/>
    public void SetMindStates(
        Entity<BorgChassisComponent> borg,
        string hasMindState,
        string noMindState,
        string? shuntMindState = null,
        string? remoteControlState = null,
        Color? hasMindColor = null,
        Color? noMindColor = null,
        Color? shuntMindColor = null,
        Color? remoteControlColor = null,
        bool useMindColors = false)
    {
        borg.Comp.HasMindState = hasMindState;
        borg.Comp.NoMindState = noMindState;
        // Shunt and remote-control states fall back to the has-mind state.
        borg.Comp.ShuntMindState = shuntMindState ?? hasMindState;
        borg.Comp.RemoteControlState = remoteControlState ?? hasMindState;
        // Null = subtype doesn't override; keep the chassis component's default color.
        borg.Comp.HasMindColor = hasMindColor ?? borg.Comp.HasMindColor;
        borg.Comp.NoMindColor = noMindColor ?? borg.Comp.NoMindColor;
        borg.Comp.ShuntMindColor = shuntMindColor ?? borg.Comp.ShuntMindColor;
        borg.Comp.RemoteControlColor = remoteControlColor ?? borg.Comp.RemoteControlColor;
        // Only tint when the eye is an emission mask; stock eyes render untinted.
        borg.Comp.UseMindColors = useMindColors;
    }
}
