namespace Content.Server.Atmos.Components;

[RegisterComponent]
public sealed partial class HeatExchangerComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("inlet")]
    public string InletName { get; set; } = "inlet";

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("outlet")]
    public string OutletName { get; set; } = "outlet";

    /// <summary>
    /// Pipe conductivity (mols/kPa/sec).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("conductivity")]
    public float G { get; set; } = 1f;

    /// <summary>
    /// Thermal convection coefficient (J/degK/sec).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("convectionCoefficient")]
    public float K { get; set; } = 8000f;

    /// <summary>
    /// Thermal radiation coefficient. Number of "effective" tiles this
    /// radiator radiates compared to superconductivity tile losses.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("radiationCoefficient")]
    public float alpha { get; set; } = 140f;

    /// <summary>
    /// Exponent of the radiation power law (ΔT^n). Physical blackbody radiation
    /// is 4, but lower values flatten the curve: the power is pinned at the
    /// <see cref="tileLoss"/> rating at 20 degC and lower exponents give more
    /// cooling below that and less runaway above it. Must be greater than 1.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("radiationExponent")]
    public float n { get; set; } = 4f;

    /// <summary>
    /// Coefficient of an additional linear radiation term (ΔT^1), in the same
    /// "effective tiles" units as <see cref="alpha"/>. It lifts the low-ΔT end
    /// of the curve without inflating the high end, since it fades relative to
    /// the steep <see cref="n"/> term as ΔT grows. Vanishes at ΔT = 0, so it
    /// never cools past the radiating temperature. Set to 0 to disable.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("radiationLinearCoefficient")]
    public float beta { get; set; } = 1000f;
}

