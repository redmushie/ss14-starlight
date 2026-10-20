using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Guidebook;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Doors.Components;

/// <summary>
/// Companion component to <see cref="DoorComponent"/> that handles firelock-specific behavior, including
/// auto-closing on depressurization, air/fire alarm interactions, and preventing normal door functions when
/// retaining pressure..
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FirelockComponent : Component
{
    #region Settings

    /// <summary>
    /// Pry time modifier to be used when the firelock is currently closed due to fire or pressure.
    /// </summary>
    /// <returns></returns>
    [DataField("lockedPryTimeModifier"), ViewVariables(VVAccess.ReadWrite)]
    public float LockedPryTimeModifier = 1.5f;

    /// <summary>
    /// Maximum pressure difference before the firelock will refuse to open, in kPa.
    /// </summary>
    [DataField("pressureThreshold"), ViewVariables(VVAccess.ReadWrite)]
    [GuidebookData]
    public float PressureThreshold = 20;

    /// <summary>
    /// Maximum temperature difference before the firelock will refuse to open, in k.
    /// </summary>
    [DataField("temperatureThreshold"), ViewVariables(VVAccess.ReadWrite)]
    [GuidebookData]
    public float TemperatureThreshold = 330;
    // this used to check for hot-spots, but because accessing that data is a a mess this now just checks
    // temperature. This does mean a cold room will trigger hot-air pop-ups

    /// <summary>
    /// If true, and if this door has an <see cref="AtmosAlarmableComponent"/>, then it will only auto-close if the
    /// alarm is set to danger.
    /// </summary>
    [DataField("alarmAutoClose"), ViewVariables(VVAccess.ReadWrite)]
    public bool AlarmAutoClose = true;

    /// <summary>
    /// The cooldown duration before a firelock can automatically close due to a hazardous environment after it has
    /// been pried open. Measured in seconds.
    /// </summary>
    [DataField]
    public TimeSpan EmergencyCloseCooldownDuration = TimeSpan.FromMilliseconds(3000);

    #endregion
    #region Set by system

    /// <summary>
    /// When the firelock will be allowed to automatically close again due to a hazardous environment.
    /// </summary>
    [DataField]
    public TimeSpan? EmergencyCloseCooldown;

    /// <summary>
    /// Whether the firelock can open, or is locked due to its environment.
    /// </summary>
    public bool IsLocked => Pressure || Temperature;

    /// <summary>
    /// Whether the firelock is holding back a hazardous pressure.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Pressure;

    /// <summary>
    /// Whether the firelock is holding back extreme temperatures.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Temperature;

    /// <summary>
    /// Whether the airlock is powered.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Powered;

    #endregion

    #region Client animation

    /// <summary>
    /// The sprite state used to animate the airlock frame when the airlock opens.
    /// </summary>
    [DataField]
    public string OpeningLightSpriteState = "opening_unlit";

    /// <summary>
    /// The sprite state used to animate the airlock frame when the airlock closes.
    /// </summary>
    [DataField]
    public string ClosingLightSpriteState = "closing_unlit";

    /// <summary>
    /// The sprite state used to animate the airlock panel when the airlock opens.
    /// </summary>
    [DataField]
    public string OpeningPanelSpriteState = "panel_opening";

    /// <summary>
    /// The sprite state used to animate the airlock panel when the airlock closes.
    /// </summary>
    [DataField]
    public string ClosingPanelSpriteState = "panel_closing";

    /// <summary>
    /// The sprite state used for the open airlock lights.
    /// </summary>
    [DataField]
    public string OpenLightSpriteState = "open_unlit";

    /// <summary>
    /// The sprite state used for the closed airlock lights.
    /// </summary>
    [DataField]
    public string WarningLightSpriteState = "closed_unlit";

    /// <summary>
    /// The sprite state used for the 'access denied' lights animation.
    /// </summary>
    [DataField]
    public string DenySpriteState = "deny_unlit";

    #endregion
    #region Starlight

    /// <summary>
    /// The thresholds used for when this firelock should react to sensor reading.
    /// </summary>
    [DataField, AutoNetworkedField] public FirelockThresholds Thresholds = new();

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
    #endregion
}

#region Starlight

public enum ClosingState : byte
{
    None = 0,
    Open = 1,
    SoftClose = 2,
    HardClose = 4,
    Bolt = 8
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

#endregion
