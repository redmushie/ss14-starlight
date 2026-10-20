using Content.Server.Atmos.Monitor.Systems;
using Content.Shared._Starlight.Doors.Components;
using Content.Shared.Atmos.Monitor;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockFollowerSystem : EntitySystem
{

    /// <summary>
    /// Forward atmos alarm states to the leader. Yes, this might result in the event firing like 20 times.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnFollowerAtmosAlarm(Entity<FirelockFollowerComponent> follower, ref AtmosAlarmEvent args)
    {
        var leader = follower.Comp.Leader;
        if (LifeStage(leader) >= EntityLifeStage.Terminating) return;

        var relay = new FollowerAtmosAlarmEvent() { AlarmType = args.AlarmType };
        RaiseLocalEvent(leader, ref relay);
    }

}

[ByRefEvent]
public record struct FollowerAtmosAlarmEvent
{
    public AtmosAlarmType AlarmType;
}
