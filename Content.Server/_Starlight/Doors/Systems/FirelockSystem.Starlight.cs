using Content.Server.Audio;
using Content.Shared.Atmos;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

// ReSharper disable once CheckNamespace
namespace Content.Server.Doors.Systems;

public sealed partial class FirelockSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    // ReSharper disable once InconsistentNaming
    private const double TCMBWithDelta = Atmospherics.TCMB + 2;
    private const double Low

    private (bool bolting, bool changed) StartOrContinueBoltingCountdown(Entity<FirelockComponent> firelock, DoorComponent door)
    {
        if (firelock.Comp.Bolted)
            return (false, false);

        // If end time is already set, we just have to wait.
        if (firelock.Comp.BoltCountdownEnd != null)
        {
            // If end time is reached, bolt for real.
            if (firelock.Comp.BoltCountdownEnd <= _timing.CurTime)
            {
                DoBolt(firelock);
                return (false, true);
            }
            return (true, false);
        }

        firelock.Comp.BoltCountdownStart = _timing.CurTime;
        firelock.Comp.BoltCountdownEnd = _timing.CurTime + firelock.Comp.BoltCountdownDuration;
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

    private void DoBolt(Entity<FirelockComponent> firelock)
    {
        firelock.Comp.Bolted = true;
        firelock.Comp.BoltCountdownStart = null;
        firelock.Comp.BoltCountdownEnd = null;
        _ambient.SetAmbience(firelock, false);
        _appearance.SetData(firelock, DoorVisuals.ClosedLights, true);
        _audio.PlayPvs(firelock.Comp.BoltDownSound, firelock);
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

    private FirelockVisuals GetTemperatureVisual(ClosingConditions conditions, bool bolting)
    {
        // If there is no delta, we have nothing to show as both sides are equal.
        if (!conditions.TempDelta)
            return FirelockVisuals.None;

        if (conditions.TempHighExtreme)
            return bolting ? FirelockVisuals.HighAlternating : FirelockVisuals.HighBlinking;
        if (conditions.TempLowExtreme)
            return bolting ? FirelockVisuals.LowAlternating : FirelockVisuals.LowBlinking;
        if (conditions.TempHigh)
            return FirelockVisuals.HighSolid;
        if (conditions.TempLow)
            return FirelockVisuals.LowSolid;

        return FirelockVisuals.None;
    }

    private FirelockVisuals GetPressureVisual(ClosingConditions conditions, bool bolting)
    {
        // If there is no delta, we have nothing to show as both sides are equal.
        if (!conditions.PressureDelta)
            return FirelockVisuals.None;

        if (conditions.PressureHighExtreme)
            return bolting ? FirelockVisuals.HighAlternating : FirelockVisuals.HighBlinking;
        if (conditions.PressureLowExtreme)
            return bolting ? FirelockVisuals.LowAlternating : FirelockVisuals.LowBlinking;
        if (conditions.PressureHigh)
            return FirelockVisuals.HighSolid;
        if (conditions.PressureLow)
            return FirelockVisuals.LowSolid;
        if (conditions.PressureDeltaExtreme)
            return bolting ? FirelockVisuals.DeltaAlternating : FirelockVisuals.DeltaBlinking;
        if (conditions.PressureDelta)
            return FirelockVisuals.DeltaSolid;

        return FirelockVisuals.None;
    }

    private ClosingConditions GetConditionsFromFirelock(FirelockComponent firelock) =>
        new()
        {
            TempDelta = firelock.TemperatureDelta,
            TempDeltaExtreme = firelock.TemperatureDeltaExtreme,
            TempLow = firelock.TemperatureLow,
            TempLowExtreme = firelock.TemperatureExtremelyLow,
            TempHigh = firelock.TemperatureHigh,
            TempHighExtreme = firelock.TemperatureExtremelyHigh,
            PressureDelta = firelock.PressureDelta,
            PressureDeltaExtreme = firelock.PressureDeltaExtreme,
            PressureLow = firelock.PressureLow,
            PressureLowExtreme = firelock.PressureLowExtreme,
            PressureHigh = firelock.PressureHigh,
            PressureHighExtreme = firelock.PressureHighExtreme,
        };

    private void ApplyConditionsToFirelock(FirelockComponent firelock, ClosingConditions conditions)
    {
        firelock.TemperatureDelta = conditions.TempDelta;
        firelock.TemperatureDeltaExtreme = conditions.TempDeltaExtreme;
        firelock.TemperatureLow = conditions.TempLow;
        firelock.TemperatureExtremelyLow = conditions.TempLowExtreme;
        firelock.TemperatureHigh = conditions.TempHigh;
        firelock.TemperatureExtremelyHigh = conditions.TempHighExtreme;

        firelock.PressureDelta = conditions.PressureDelta;
        firelock.PressureDeltaExtreme = conditions.PressureDeltaExtreme;
        firelock.PressureLow = conditions.PressureLow;
        firelock.PressureLowExtreme = conditions.PressureLowExtreme;
        firelock.PressureHigh = conditions.PressureHigh;
        firelock.PressureHighExtreme = conditions.PressureHighExtreme;
    }
}

public record ClosingConditions
{
    public bool PressureDelta;
    public bool PressureDeltaExtreme;
    public bool PressureLow;
    public bool PressureLowExtreme;
    public bool PressureHigh;
    public bool PressureHighExtreme;

    public bool TempDelta;
    public bool TempDeltaExtreme;
    public bool TempLow;
    public bool TempLowExtreme;
    public bool TempHigh;
    public bool TempHighExtreme;

    /// <summary>
    /// We soft-close (just shut, no access required to open) when there's a large enough delta and actually dangerous
    /// values, either for pressure or for temperature.
    /// </summary>
    public bool SoftClose => (TempDelta && (TempLow || TempHigh)) ||
                             (PressureDelta && (PressureLow || PressureHigh));

    /// <summary>
    /// Hard-closing is done when there is an
    /// </summary>
    public bool HardClose => (TempDelta && (TempLowExtreme || TempHighExtreme)) ||
                             (PressureDeltaExtreme && (PressureLowExtreme || PressureHighExtreme));

    public bool Close => SoftClose || HardClose;
}
