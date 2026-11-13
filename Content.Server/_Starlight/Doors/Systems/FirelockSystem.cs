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
            Log.Debug($"Trying to close: {firelock.Owner}");
            TryClose(firelock);
            return;
        }

        if (targetState is ClosingState.Open)
        {
            Log.Debug($"Trying to open: {firelock.Owner}");
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
    /// <param name="reading">The reading</param>
    public void AcceptEnvironmentReadings(Entity<FirelockComponent> firelock, List<SensingReading> readings)
    {
        // var judgements = new List<SensingJudgements>();
        //
        // // Evaluate each reading, comparing it against thresholds.
        // foreach (var reading in readings)
        //     judgements.Add(JudgeReading(ref firelock.Comp.Thresholds, reading));
        //
        // // Aggregate all judgements into one 'net' reading for this firelock.
        // var judgement = AggregateJudgements(judgements);
        //
        // firelock.Comp.SensorDeltaPressure = judgement.DeltaPressure;
        // firelock.Comp.SensorLowPressure = judgement.LowPressure;
        // firelock.Comp.SensorHighPressure = judgement.HighPressure;
        //
        // firelock.Comp.SensorDeltaTemperature = judgement.DeltaTemperature;
        // firelock.Comp.SensorLowTemperature = judgement.LowTemperature;
        // firelock.Comp.SensorHighTemperature = judgement.HighTemperature;
        // Dirty(firelock);
        //
        // if (judgement.Closing != firelock.Comp.EnvironmentClosingState)
        //     Log.Debug($"Updated environment closing state for {firelock.Owner} to: {judgement.Closing}");
        //
        // firelock.Comp.EnvironmentClosingState = judgement.Closing;
        //
        // UpdateState(firelock);
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

    private (bool bolting, bool changed) StartOrContinueBoltingCountdown(Entity<FirelockComponent> firelock)
    {
        if (firelock.Comp.Bolted)
            return (false, false);

        // If end time is already set, we just have to wait.
        if (firelock.Comp.BoltCountdownEnd != null)
        {
            // If end time is reached, bolt for real.
            if (firelock.Comp.BoltCountdownEnd <= _gameTiming.CurTime)
            {
                DoBolt(firelock);
                return (false, true);
            }

            return (true, false);
        }

        firelock.Comp.BoltCountdownStart = _gameTiming.CurTime;
        firelock.Comp.BoltCountdownEnd = _gameTiming.CurTime + firelock.Comp.BoltCountdownDuration;
        _ambient.SetAmbience(firelock, true);
        return (true, false);
    }

    private void CancelCountdownOrUnboltNow(Entity<FirelockComponent> firelock)
    {
        if (!firelock.Comp.Bolted && firelock.Comp.BoltCountdownStart == null)
            return;

        if (firelock.Comp.Bolted)
        {
            _audio.PlayPvs(firelock.Comp.BoltUpSound, firelock);
            _appearance.SetData(firelock, DoorVisuals.ClosedLights, false);
            firelock.Comp.Bolted = false;
        }

        firelock.Comp.BoltCountdownStart = null;
        firelock.Comp.BoltCountdownEnd = null;
        _ambient.SetAmbience(firelock, false);
    }

    #region Door helpers

    private (bool open, bool opening) TryOpen(Entity<FirelockComponent> firelock)
    {
        if (!TryComp<DoorComponent>(firelock, out var door))
            return (false, false);

        if (door.State is DoorState.Open)
            return (true, false);
        if (door.State is DoorState.Opening)
            return (false, true);

        if (_doorSystem.TryOpen(firelock, door))
            return (false, true);

        _doorSystem.OnPartialOpen(firelock, door);
        return (false, true);
    }

    private (bool closed, bool closing) TryClose(Entity<FirelockComponent> firelock)
    {
        if (!TryComp<DoorComponent>(firelock, out var door))
            return (false, false);

        if (door.State is DoorState.Closed or DoorState.Welded or DoorState.Denying)
            return (true, false);
        if (door.State is DoorState.Closing)
            return (false, true);

        if (_doorSystem.TryClose(firelock, door))
            return (false, true);

        return (false, _doorSystem.OnPartialClose(firelock, door));
    }

    private void DoBolt(Entity<FirelockComponent> firelock)
    {
        firelock.Comp.Bolted = true;
        firelock.Comp.BoltCountdownStart = null;
        firelock.Comp.BoltCountdownEnd = null;
        _ambient.SetAmbience(firelock, false);
        _appearance.SetData(firelock, DoorVisuals.ClosedLights, true);
        _audio.PlayPvs(firelock.Comp.BoltDownSound, firelock, new AudioParams()
        {
            Volume = -3f,
            Variation = 0.05f
        });
        EmergencyPressureStop(firelock, firelock);
    }

    private bool IsClosed(Entity<FirelockComponent> firelock, DoorComponent? door)
    {
        if (!Resolve(firelock, ref door))
            return false;

        return door.State is DoorState.Closed or DoorState.Welded or DoorState.Denying;
    }

    private bool IsBolted(Entity<FirelockComponent> firelock, DoorComponent? door)
        => IsClosed(firelock, door) && firelock.Comp.Bolted;

    #endregion

    private FirelockVisuals GetTemperatureVisual(Entity<FirelockComponent> firelock, bool bolting)
    {
        // If there is no delta, we have nothing to show as both sides are equal.
        // if (!conditions.TempDelta)
        //     return FirelockVisuals.None;
        // TODO: implement and use delta?

        // TODO: for loop this shit
        if ((firelock.Comp.SensorHighTemperature & SensorSeverity.Death) != 0)
            return bolting ? FirelockVisuals.HighAlternating : FirelockVisuals.HighBlinking;
        if ((firelock.Comp.SensorLowTemperature & SensorSeverity.Death) != 0)
            return bolting ? FirelockVisuals.LowAlternating : FirelockVisuals.LowBlinking;
        if ((firelock.Comp.SensorHighTemperature & SensorSeverity.Danger) != 0)
            return bolting ? FirelockVisuals.HighAlternating : FirelockVisuals.HighBlinking;
        if ((firelock.Comp.SensorLowTemperature & SensorSeverity.Danger) != 0)
            return bolting ? FirelockVisuals.LowAlternating : FirelockVisuals.LowBlinking;
        if ((firelock.Comp.SensorHighTemperature & SensorSeverity.Warning) != 0)
            return FirelockVisuals.HighSolid;
        if ((firelock.Comp.SensorLowTemperature & SensorSeverity.Warning) != 0)
            return FirelockVisuals.LowSolid;

        return FirelockVisuals.None;
    }

    private FirelockVisuals GetPressureVisual(Entity<FirelockComponent> firelock, bool bolting)
    {
        // If there is no delta, we have nothing to show as both sides are equal.
        // if (!conditions.PressureDelta)
        //     return FirelockVisuals.None;
        // TODO: implement and use delta?

        // TODO: for loop this shit
        if ((firelock.Comp.SensorHighPressure & SensorSeverity.Death) != 0)
            return bolting ? FirelockVisuals.HighAlternating : FirelockVisuals.HighBlinking;
        if ((firelock.Comp.SensorLowPressure & SensorSeverity.Death) != 0)
            return bolting ? FirelockVisuals.LowAlternating : FirelockVisuals.LowBlinking;
        if ((firelock.Comp.SensorHighPressure & SensorSeverity.Danger) != 0)
            return bolting ? FirelockVisuals.HighAlternating : FirelockVisuals.HighBlinking;
        if ((firelock.Comp.SensorLowPressure & SensorSeverity.Danger) != 0)
            return bolting ? FirelockVisuals.LowAlternating : FirelockVisuals.LowBlinking;
        if ((firelock.Comp.SensorHighPressure & SensorSeverity.Warning) != 0)
            return FirelockVisuals.HighSolid;
        if ((firelock.Comp.SensorLowPressure & SensorSeverity.Warning) != 0)
            return FirelockVisuals.LowSolid;

        return FirelockVisuals.None;
    }

}
