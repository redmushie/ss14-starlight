using Content.Server._Starlight.Doors.Systems;
using Content.Server.Audio;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

// ReSharper disable once CheckNamespace
namespace Content.Server.Doors.Systems;

public sealed partial class FirelockSystem
{
    [Dependency] private AmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Update(float frameTime)
    {
    }

    private void UpdateState(Entity<FirelockComponent> firelock)
    {
        var targetState = CalculateTargetState(firelock);
        firelock.Comp.TargetState = targetState;

        UpdateAppearance(firelock);

        if (targetState == ClosingState.None) return;
        if (!TryComp<DoorComponent>(firelock, out var door)) return;

        // Try to realize the state in order of effectiveness.
        if (targetState is ClosingState.SoftClose or ClosingState.HardClose)
        {
            TryClose(firelock);
            return;
        }

        if (targetState is ClosingState.Open)
        {
            TryOpen(firelock);
            return;
        }

        if (targetState == ClosingState.Bolt)
        {
            TryClose(firelock);
            StartOrContinueBoltingCountdown(firelock);
        }
    }

    private void UpdateAppearance(Entity<FirelockComponent> firelock)
    {
        var bolting = !firelock.Comp.Bolted && firelock.Comp.TargetState == ClosingState.Bolt;
        if (!TryComp<AppearanceComponent>(firelock, out var appearance)) return;
        if (!TryComp<DoorComponent>(firelock, out var door)) return;

        _appearance.SetData(firelock, DoorVisuals.ClosedLights,
            door.State is DoorState.Closed or DoorState.Denying or DoorState.Welded &&
            firelock.Comp.TargetState >= ClosingState.HardClose &&
            !firelock.Comp.Bolted, appearance);
        _appearance.SetData(firelock, DoorVisuals.BoltLights, firelock.Comp.Bolted, appearance);

        _appearance.SetData(firelock, FirelockVisuals.TemperatureWarning, GetTemperatureVisual(firelock, bolting),
                appearance);
        _appearance.SetData(firelock, FirelockVisuals.PressureWarning, GetPressureVisual(firelock, bolting),
                appearance);
    }

    private ClosingState CalculateTargetState(Entity<FirelockComponent> firelock)
    {
        var now = _gameTiming.CurTime;
        var comp = firelock.Comp;

        // Baseline: Environmental parameters.
        var targetState = firelock.Comp.EnvironmentClosingState;

        // Apply the atmos alarm state if it's within the active window.
        if (comp.AirAlarmState > targetState &&
            comp.AirAlarmStateSince + comp.AirAlarmStateTimeout > now)
        {
            targetState = comp.AirAlarmState;
        }

        // Apply the decompression trigger if it's within the active window.
        if (targetState < ClosingState.SoftClose &&
            comp.DecompressionTriggerLastFired + comp.DecompressionTriggerWindow > now)
        {
            targetState = ClosingState.SoftClose;
        }

        // Apply the manual override, if set.
        // TODO: maybe.

        return targetState;
    }

    /// <summary>
    /// Accept environmental sensor readings.
    /// </summary>
    /// <param name="firelock">The firelock to which this reading pertains</param>
    /// <param name="judgement">The judgements</param>
    /// <param name="closing">The ClosingState derived from the judgements</param>
    public void AcceptState(Entity<FirelockComponent> firelock, SensingJudgements judgement, ClosingState closing)
    {
        firelock.Comp.SensorDeltaPressure = judgement.DeltaPressure;
        firelock.Comp.SensorLowPressure = judgement.LowPressure;
        firelock.Comp.SensorHighPressure = judgement.HighPressure;

        firelock.Comp.SensorDeltaTemperature = judgement.DeltaTemperature;
        firelock.Comp.SensorLowTemperature = judgement.LowTemperature;
        firelock.Comp.SensorHighTemperature = judgement.HighTemperature;
        Dirty(firelock);

        if (closing != firelock.Comp.EnvironmentClosingState)
            Log.Debug($"Updated environment closing state for {firelock.Owner} to: {closing}");

        firelock.Comp.EnvironmentClosingState = closing;
        UpdateState(firelock);
    }

    public void AcceptAirAlarmState(Entity<FirelockComponent> firelock, AtmosAlarmType type)
    {
        firelock.Comp.AirAlarmStateSince = _gameTiming.CurTime;
        var closingState = type switch
        {
            AtmosAlarmType.Warning => ClosingState.SoftClose,
            AtmosAlarmType.Danger => ClosingState.HardClose,
            _ => ClosingState.None
        };

        // If the state is unchanged, we don't need to recalculate anything.
        if (firelock.Comp.AirAlarmState == closingState)
            return;

        firelock.Comp.AirAlarmState = closingState;
        Log.Debug($"Updated air alarm state for {firelock.Owner} to: {closingState}");
        UpdateState(firelock);
    }


}
