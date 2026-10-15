using Content.Shared._Starlight.Doors.Components;
using Content.Shared.Doors.Components;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockGroupSystem
{

    public override void Update(float frameTime)
    {
        // Update global sensing things. This recalculates islands, one group per tick.
        UpdateSensingGlobal(frameTime);

        var query = EntityQueryEnumerator<FirelockComponent, FirelockGroupComponent>();
        while (query.MoveNext(out var uid, out var firelock, out var groupComp))
        {
            if (LifeStage(uid) >= EntityLifeStage.Terminating)
                continue;

            UpdateSensing(frameTime, (uid, groupComp));
        }
    }

}
