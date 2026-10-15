using Content.Shared._Starlight.Doors.Components;
using Content.Shared.Doors.Components;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockGroupSystem
{

    public override void Update(float frameTime)
    {
        UpdateSensingGlobal(frameTime);

        var query = EntityQueryEnumerator<FirelockComponent, FirelockGroupComponent>();
        while (query.MoveNext(out var uid, out var firelock, out var groupComp))
        {
            UpdateSensing(frameTime, (uid, groupComp));
        }
    }

}
