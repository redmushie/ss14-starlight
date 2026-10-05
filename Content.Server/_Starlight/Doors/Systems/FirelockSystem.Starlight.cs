using Content.Shared.Doors.Components;

namespace Content.Server._Starlight.Doors.Systems;

public record ClosingConditions
{
    public bool PressureDelta;
    public bool LowTemperature;
    public bool HighTemperature;

    public bool ExtremePressureDelta;
    public bool ExtremeLowTemp;
    public bool ExtremeHighTemp;

    public bool SoftClose => PressureDelta ||  LowTemperature || HighTemperature;
    public bool HardClose => ExtremePressureDelta || ExtremeLowTemp || ExtremeHighTemp;

    public bool Close => SoftClose || HardClose;

    public static ClosingConditions FromFirelock(FirelockComponent firelock) =>
        new()
        {
            LowTemperature = firelock.TemperatureLow,
            ExtremeLowTemp = firelock.TemperatureExtremelyLow,
            HighTemperature = firelock.TemperatureHigh,
            ExtremeHighTemp = firelock.TemperatureExtremelyHigh,
            PressureDelta = firelock.PressureDelta,
            ExtremePressureDelta = firelock.ExtremePressureDelta
        };
}
