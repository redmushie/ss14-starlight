using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Timing;

namespace Content.Server.Shuttles.Systems;

public sealed partial class DockingSystem
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<Entity<DockingComponent>> _latchedDocks = [];

    private readonly List<Entity<DockingComponent>> _latchCheckBuffer = [];

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        CheckLatchedDocks();
    }

    [SubscribeLocalEvent]
    private void OnLatchDock(DockEvent args)
    {
        var hasLatchA = TryComp(args.DockA, out LatchDockComponent? latchA);
        var hasLatchB = TryComp(args.DockB, out LatchDockComponent? latchB);

        if (!hasLatchA && !hasLatchB)
            return;

        var latch = latchA ?? latchB!;

        var breakpoint = float.MaxValue;

        if (HasComp<PhysicsComponent>(args.GridAUid) &&
            HasComp<PhysicsComponent>(args.GridBUid) &&
            args.DockA.Comp.DockJoint != null)
        {
            var massA = Comp<PhysicsComponent>(args.GridAUid).Mass;
            var massB = Comp<PhysicsComponent>(args.GridBUid).Mass;

            breakpoint = (massA + massB) * latch.BreakAccel;

            // Start unbreakable so the initial alignment dosent immediately snap the new joint.
            args.DockA.Comp.DockJoint.Breakpoint = float.MaxValue;
        }

        var graceEnd = _timing.CurTime + TimeSpan.FromSeconds(latch.GraceSeconds);

        if (latchA != null)
        {
            latchA.IsLatched = true;
            latchA.LatchGraceEnd = graceEnd;
            latchA.LatchBreakpoint = breakpoint;
        }

        if (latchB != null)
        {
            latchB.IsLatched = true;
            latchB.LatchGraceEnd = graceEnd;
            latchB.LatchBreakpoint = breakpoint;
        }

        _latchedDocks.Add(args.DockA);
        _latchedDocks.Add(args.DockB);
    }

    /// <summary>
    /// Fires whenever a latchdock physics joint breaks.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLatchJointBreak(ref JointBreakEvent args)
    {
        if (_latchedDocks.Count == 0)
            return;

        _latchCheckBuffer.Clear();
        _latchCheckBuffer.AddRange(_latchedDocks);

        foreach (var dock in _latchCheckBuffer)
        {
            if (dock.Comp.DockJointId != args.Joint.ID)
                continue;

            Undock(dock);
            return;
        }
    }

    [SubscribeLocalEvent]
    private void OnLatchUndock(UndockEvent args)
    {
        ResetLatchDock(args.DockA);
        ResetLatchDock(args.DockB);
    }

    private void ResetLatchDock(Entity<DockingComponent> dock)
    {
        _latchedDocks.Remove(dock);

        if (!TryComp(dock, out LatchDockComponent? latch))
            return;

        latch.IsLatched = false;
        latch.LatchGraceEnd = null;
        latch.LatchBreakpoint = 0f;
    }

    /// <summary>
    /// Runs every tick to handle latchdocks that do not have a physics joint,
    /// </summary>
    private void CheckLatchedDocks()
    {
        if (_latchedDocks.Count == 0)
            return;

        _latchCheckBuffer.Clear();
        _latchCheckBuffer.AddRange(_latchedDocks);

        foreach (var dock in _latchCheckBuffer)
        {
            if (!_latchedDocks.Contains(dock))
                continue;

            if (Deleted(dock.Owner) ||
                dock.Comp.DockedWith is not { } otherUid)
            {
                _latchedDocks.Remove(dock);
                continue;
            }

            if (!TryComp(dock, out LatchDockComponent? latch) ||
                !latch.IsLatched)
            {
                _latchedDocks.Remove(dock);
                continue;
            }

            // Leave the joint unbreakable until the alignment has finished.
            if (latch.LatchGraceEnd is { } graceEnd)
            {
                if (_timing.CurTime < graceEnd)
                    continue;

                dock.Comp.DockJoint?.Breakpoint = latch.LatchBreakpoint;

                latch.LatchGraceEnd = null;
                continue;
            }

            // real joint is handled by OnLatchJointBreak.
            if (dock.Comp.DockJoint != null)
                continue;

            if (!_xformQuery.TryGetComponent(dock.Owner, out var dockXform) ||
                !_xformQuery.TryGetComponent(otherUid, out var otherXform))
            {
                continue;
            }

            var (worldPosA, worldRotA) = _transform.GetWorldPositionRotation(dockXform);
            var (worldPosB, worldRotB) = _transform.GetWorldPositionRotation(otherXform);

            var stillGood = dockXform.MapID == otherXform.MapID &&
                            CanDock(
                                new MapCoordinates(worldPosA, dockXform.MapID),
                                worldRotA,
                                new MapCoordinates(worldPosB, otherXform.MapID),
                                worldRotB);

            if (!stillGood)
                Undock(dock);
        }
    }
}
