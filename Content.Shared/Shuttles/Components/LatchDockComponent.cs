namespace Content.Shared.Shuttles.Components;

/// <summary>
/// Marks a docking port as a latchdock.
/// </summary>
[RegisterComponent]
public sealed partial class LatchDockComponent : Component
{
    [ViewVariables]
    public bool IsLatched;

    [ViewVariables]
    public TimeSpan? LatchGraceEnd;

    [ViewVariables]
    public float LatchBreakpoint;

    /// <summary>
    /// The accelleration threshold before the latchdock breaks connection.
    /// </summary>
    [DataField]
    public float BreakAccel = 15f;

    /// <summary>
    /// How long after docking before the latchdock's breakpoint is enforced.
    /// </summary>
    [DataField]
    public float GraceSeconds = 1.5f;
}

