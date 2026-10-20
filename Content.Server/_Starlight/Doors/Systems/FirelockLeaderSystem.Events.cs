using Content.Shared._Starlight.Doors.Components;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Doors.Components;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockLeaderSystem
{

    /// <summary>
    /// When a follower receives an air alarm status change, it is relayed to the leader. Here we propagate it
    /// from the leader to each individual firelock that is controlled by this leader.
    /// </summary>
    /// <param name="leader"></param>
    /// <param name="type"></param>
    [SubscribeLocalEvent]
    public void OnFollowerAtmosAlarm(Entity<FirelockLeaderComponent> leader, ref FollowerAtmosAlarmEvent type)
    {
        foreach (var follower in leader.Comp.Members)
        {
            if (!IsValid(follower)) continue;
            if (!TryComp<FirelockComponent>(follower, out var firelock)) continue;
            _firelock.AcceptAirAlarmState((follower, firelock), type.AlarmType);
        }
    }

}
