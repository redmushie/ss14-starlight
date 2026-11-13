using Content.Shared.Atmos;
using Robust.Shared.Serialization;
using Robust.Shared.Audio;

// ReSharper disable once CheckNamespace
namespace Content.Shared.Doors.Components;

public sealed partial class FirelockComponent
{
    /// <summary>
    /// The thresholds used for when this firelock should react to sensor reading.
    /// </summary>
    [DataField, AutoNetworkedField] public FirelockThresholds Thresholds = new();

    #region Settings

    [DataField, AutoNetworkedField] public bool SensesLowPressure = true;
    [DataField, AutoNetworkedField] public bool SensesHighPressure = true;

    [DataField, AutoNetworkedField] public bool SensesLowTemperature = true;
    [DataField, AutoNetworkedField] public bool SensesHighTemperature = true;

    #endregion
    #region Environmental inputs

    [AutoNetworkedField] public SensorSeverity SensorDeltaTemperature = SensorSeverity.Safe;
    [AutoNetworkedField] public SensorSeverity SensorLowTemperature = SensorSeverity.Safe;
    [AutoNetworkedField] public SensorSeverity SensorHighTemperature = SensorSeverity.Safe;

    [AutoNetworkedField] public SensorSeverity SensorDeltaPressure = SensorSeverity.Safe;
    [AutoNetworkedField] public SensorSeverity SensorLowPressure = SensorSeverity.Safe;
    [AutoNetworkedField] public SensorSeverity SensorHighPressure = SensorSeverity.Safe;

    /// <summary>
    /// The closing state as determined by the environmental parameters.
    /// </summary>
    [AutoNetworkedField] public ClosingState EnvironmentClosingState = ClosingState.None;

    #endregion

    #region External inputs

    /* Decompression */

    public TimeSpan DecompressionTriggerLastFired = TimeSpan.Zero;
    public TimeSpan DecompressionTriggerWindow = TimeSpan.FromSeconds(2);

    /* Air alarms */

    public ClosingState AirAlarmState = ClosingState.None;
    public TimeSpan AirAlarmStateSince = TimeSpan.Zero;

    public TimeSpan AirAlarmStateTimeout = TimeSpan.FromSeconds(2);

    #endregion

    #region Manual inputs

    /// <summary>
    /// The closing state as requested result of manual player interaction (pressing E, clicking, alt-clicking).
    /// This can override both
    /// </summary>
    public ClosingState ManualState = ClosingState.None;

    public TimeSpan ManualStateSince = TimeSpan.Zero;

    /// <summary>
    /// For how long we will respect a player manually opening the firelock.
    /// </summary>
    public TimeSpan? ManualStateOpenTimeout = TimeSpan.FromSeconds(3);

    #endregion

    #region Resulting state

    public ClosingState TargetState = ClosingState.None;

    // Bolting however is scheduled ahead of time, so the client can definitely use this info.

    [DataField, AutoNetworkedField] public TimeSpan BoltCountdownDuration = TimeSpan.FromSeconds(10);
    [DataField, AutoNetworkedField] public TimeSpan? BoltCountdownStart;
    [DataField, AutoNetworkedField] public TimeSpan? BoltCountdownEnd;
    [DataField, AutoNetworkedField] public bool Bolted;

    /// <summary>
    /// Sound to play when the bolts on the airlock go up.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier BoltUpSound = new SoundPathSpecifier("/Audio/Machines/boltsup.ogg");

    /// <summary>
    /// Sound to play when the bolts on the airlock go down.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier BoltDownSound = new SoundPathSpecifier("/Audio/Machines/boltsdown.ogg");

    #endregion
}

public enum ClosingState : byte
{
    None = 0,
    Open = 1,
    SoftClose = 2,
    HardClose = 4,
    Bolt = 8
}


/// <summary>
/// Sensor types for a firelock. Corresponds to the fields in a SensingJudgements record.
/// Note: These are sorted from "most important" to "least important" in terms of visuals.
/// </summary>
public enum Sensor
{
    HighPressure,
    HighTemperature,

    LowTemperature,
    LowPressure,

    DeltaTemperature,
    DeltaPressure
}

/// <summary>
/// Represents the severity of a single metric, such as "low temperature"
/// </summary>
[Flags]
public enum SensorSeverity
{
    Safe = 0,
    Warning = 1,
    Danger = 2,
    Death = 4
}

[Serializable, NetSerializable]
public record struct FirelockThresholds()
{
    /// <summary>
    /// Amount of mols required before accepting temperature readings.
    /// Particularly helpful against spacing registering as 'extremely cold'.
    /// </summary>
    public float TemperatureReadingMinMoles = 5;

    /// <summary>
    /// Minimum temperature to detect. Offset by 5K above cosmic background (2.7K).
    /// </summary>
    public float TemperatureReadingMinTemp = Atmospherics.TCMB + 5;

    public float LowPressureDanger = 40;
    public float LowPressureWarning = 70;

    public float HighPressureWarning = 250;
    public float HighPressureDanger = 500;
    public float HighPressureDeath = 1000;

    public float LowTemperatureWarning = Atmospherics.T0C - 10;
    public float LowTemperatureDanger = Atmospherics.T0C - 60;
    public float LowTemperatureDeath = Atmospherics.T0C - 200;

    public float HighTemperatureWarning = Atmospherics.T0C + 45;
    public float HighTemperatureDanger = Atmospherics.T0C + 70;
    public float HighTemperatureDeath = Atmospherics.T0C + 500;

    // public float DeltaPressureWarning = 30;
    // public float DeltaPressureDanger = 200;
    // public float DeltaPressureDeath = 1000;
}
