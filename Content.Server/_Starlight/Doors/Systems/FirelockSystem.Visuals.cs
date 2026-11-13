using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;

// ReSharper disable once CheckNamespace
namespace Content.Server.Doors.Systems;

public sealed partial class FirelockSystem
{

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
