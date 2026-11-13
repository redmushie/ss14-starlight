using Robust.Shared.Audio;
using Content.Shared.Doors.Components;

// ReSharper disable once CheckNamespace
namespace Content.Server.Doors.Systems;

public sealed partial class FirelockSystem
{

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

    private (bool open, bool opening) TryOpen(Entity<FirelockComponent> firelock)
    {
        if (!TryComp<DoorComponent>(firelock, out var door))
            return (false, false);

        if (door.State is DoorState.Open)
            return (true, false);
        if (door.State is DoorState.Opening)
            return (false, true);

        Log.Debug($"Trying to open: {firelock.Owner}");
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

        Log.Debug($"Trying to close: {firelock.Owner}");
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


}
