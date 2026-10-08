// ReSharper disable once CheckNamespace

using Content.Shared.Doors.Components;

namespace Content.Shared.Doors.Systems;

public abstract partial class SharedDoorSystem
{

    public void PlayBoltSound(Entity<DoorBoltComponent> ent, bool down, bool predicted = false, EntityUid? user = null)
    {
        var sound = down ? ent.Comp.BoltDownSound : ent.Comp.BoltUpSound;
        if (predicted)
            Audio.PlayPredicted(sound, ent, user: user);
        else
            Audio.PlayPvs(sound, ent);
    }

}
