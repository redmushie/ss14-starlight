using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Starlight.Doors.Components;
using Content.Shared._Starlight.Maths;
using Content.Shared.Atmos.Components;
using Content.Shared.Doors.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Doors.Systems;

public sealed partial class FirelockLeaderSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;

    private readonly TimeSpan _sensingInterval = TimeSpan.FromSeconds(1);

    #region Phase 1 - Collect readings

    /// <summary>
    /// Sense one island.
    /// </summary>
    /// <param name="group"></param>
    /// <param name="island"></param>
    /// <returns></returns>
    private SensingReading SenseIsland(Entity<FirelockLeaderComponent> group, SensingIsland island)
    {
        var xform = Transform(group.Owner);
        var airtightQuery = GetEntityQuery<AirtightComponent>();
        var result = new SensingReading();

        if (!HasComp<GridAtmosphereComponent>(xform.ParentUid))
            return result;

        var grid = Comp<MapGridComponent>(xform.ParentUid);
        var (minMoles, maxMoles) = (float.MaxValue, float.MinValue);
        var (minPressure, maxPressure) = (float.MaxValue, float.MinValue);
        var (minTemperature, maxTemperature) = (float.MaxValue, float.MinValue);

        // We bulk request the gas mixtures in each sensing tile of this island.
        var positions = island.Tiles.Keys.ToList();
        var gases = _atmos.GetTileMixtures(xform.ParentUid, xform.MapUid, positions);
        if (gases == null)
            return result;

        for (var i = 0; i < positions.Count; i++)
        {
            var pos = positions[i];
            var tile = island.Tiles[pos];
            var gas = gases[i];

            // If there is nothing to sense, do nothing, I guess.
            if (gas == null)
                continue;

            // Sensing tiles can:
            // - Be external to any firelock. Direction points from the tile to the firelock.
            // - Be a tile containing one of our full firelocks. Direction is none.
            // - Be a tile containing one of our edge firelocks. Direction is the way the firelock faces.

            // If this tile is external to the firelock, and is not containing an edge firelock,
            // then we should check if the direction of this tile towards the sensing firelock(s) is blocked.
            // If at least one is not blocked, we accept the tile's atmospheric reading.
            var sensingDirections = tile.SensingFirelocks
                .Select(pair => pair.Item1)
                .Aggregate((dir1, dir2) => dir1 | dir2);

            // Of all our firelocks that can sense this tile, determine which directions are not blocked
            // by the contents of this tile. (E.g. a solid wall blocks all directions, but a directional window might
            // block the sensing for one firelock but not another).
            var unblockedDirections = GetUnblockedAirtightDirections(
                _map.GetAnchoredEntities(xform.ParentUid, grid, pos), sensingDirections, airtightQuery);

            // If zero sensing directions remain, we cannot sense this tile, so skip.
            if (unblockedDirections == Direction8.None)
                continue;

            // At this point we know for sure at least one firelock is able to sense this tile.
            // Time to sense!
            minMoles = Math.Min(minMoles, gas.TotalMoles);
            maxMoles = Math.Max(maxMoles, gas.TotalMoles);

            // We only sense things if at least one firelock that senses this tile supports sensing this.
            if (tile.SenseLowPressure)
                minPressure = Math.Min(minPressure, gas.Pressure);
            if (tile.SenseHighPressure)
                maxPressure = Math.Max(maxPressure, gas.Pressure);
            if (tile.SenseLowTemperature)
                minTemperature = Math.Min(minTemperature, gas.Temperature);
            if (tile.SenseHighTemperature)
                maxTemperature = Math.Max(maxTemperature, gas.Temperature);
        }

        result.MinMoles = minMoles;
        result.MaxMoles = maxMoles;
        result.MinPressure = minPressure;
        result.MaxPressure = maxPressure;
        result.MinTemperature = minTemperature;
        result.MaxTemperature = maxTemperature;

        return result;
    }

    /// <summary>
    /// Given a set of atmos directions, check if the enti
    /// </summary>
    /// <param name="tileEntities"></param>
    /// <param name="directions"></param>
    /// <param name="airtightQuery"></param>
    /// <returns></returns>
    private Direction8 GetUnblockedAirtightDirections(
        IEnumerable<EntityUid> tileEntities,
        Direction8 directions,
        EntityQuery<AirtightComponent> airtightQuery)
    {
        foreach (var ent in tileEntities)
        {
            if (!airtightQuery.TryGetComponent(ent, out var airtight) || !airtight.AirBlocked)
                continue;

            // Leaves only unblocked air directions.
            directions &= ~(airtight.AirBlockedDirection.ToDirection8());
        }

        return directions;
    }

    #endregion

    #region Phase 2 - Convert readings to judgements

    /// <summary>
    /// Phase 2. After collecting readings for a sensing island, we apply thresholds to determine
    /// where this ranks in severity.
    /// </summary>
    private SensingJudgements JudgeReading(ref FirelockThresholds thresholds, SensingReading reading)
    {
        var judgement = new SensingJudgements();

        // Judge the pressure and temperature hazard levels.
        JudgeReadingPressure(ref thresholds, reading, judgement);
        JudgeReadingTemperature(ref thresholds, reading, judgement);

        return judgement;
    }

    private void JudgeReadingPressure(
        ref FirelockThresholds thresholds,
        SensingReading reading,
        SensingJudgements judgement)
    {
        var permittedPressureDelta = 20 + (0.1f * reading.MaxPressure);
        // TODO: Also meet-in-the-middle for permitted delta maybe?
        // TODO: Debounce permittedPressureDelta / permittedTemperatureDelta, add activation and deactivation margins.

        // // If the delta is too small, we will not trigger.
        // if (reading.DeltaPressure < permittedPressureDelta)
        //     return;

        // TODO: For now, no direct delta states.
        judgement.DeltaPressure = SensorSeverity.Safe;

        judgement.LowPressure = JudgeSeverity(-reading.MinPressure,
            warn: -thresholds.LowPressureWarning,
            danger: -thresholds.LowPressureDanger);

        judgement.HighPressure = JudgeSeverity(reading.MaxPressure,
            warn: thresholds.HighPressureWarning,
            danger: thresholds.HighPressureDanger,
            death: thresholds.HighPressureDeath);
    }

    private void JudgeReadingTemperature(
        ref FirelockThresholds thresholds,
        SensingReading reading,
        SensingJudgements judgement)
    {
        var permittedTemperatureDelta = 0.2f * reading.MaxTemperature;
        // TODO: Also meet-in-the-middle for permitted delta maybe?
        // TODO: Debounce permittedPressureDelta / permittedTemperatureDelta, add activation and deactivation margins.

        // // If the delta is too small, we will not trigger.
        // if (reading.DeltaTemperature < permittedTemperatureDelta)
        //     return;

        // TODO: For now, no direct delta states.
        judgement.DeltaTemperature = SensorSeverity.Safe;

        // There's two preconditions for temperature readings; enough mols have to be present, and it has to be
        // far enough about the base temperature of 2.7K.
        if (reading.MinMoles < thresholds.TemperatureReadingMinMoles)
            return;
        if (reading.MinTemperature < thresholds.TemperatureReadingMinTemp)
            return;

        judgement.LowTemperature = JudgeSeverity(-reading.MinTemperature,
            warn: -thresholds.LowTemperatureWarning,
            danger: -thresholds.LowTemperatureDanger,
            death: -thresholds.LowTemperatureDeath);

        judgement.HighTemperature = JudgeSeverity(reading.MaxTemperature,
            warn: thresholds.HighTemperatureWarning,
            danger: thresholds.HighTemperatureDanger,
            death: thresholds.HighTemperatureDeath);
    }

    /// <summary>
    /// Given a reading and thresholds, checks if the reading exceeds each of the thresholds, and returns the
    /// corresponding severity. Tip: You can pass all-negative values to use for low and minimums rather than highs and
    /// maximums.
    /// </summary>
    private SensorSeverity JudgeSeverity(float reading, float? warn = null, float? danger = null, float? death = null)
    {
        if (reading > death)
            return SensorSeverity.Death;
        if (reading > danger)
            return SensorSeverity.Danger;
        if (reading > warn)
            return SensorSeverity.Warning;
        return SensorSeverity.Safe;
    }

    #endregion

    #region Phase 3 - Calculate inter-island deltas

    private Dictionary<(int, int), SensingJudgements> CalculateIslandDeltas(
        Dictionary<int, SensingIsland> islands,
        Dictionary<int, SensingJudgements> islandJudgements)
    {
        var deltas = new Dictionary<(int, int), SensingJudgements>();

        foreach (var a in islands.Keys)
        {
            foreach (var b in islands.Keys)
            {
                if (a == b) continue;
                if (!IsValidDelta(a, b)) continue;

                var left = islandJudgements[a];
                var right = islandJudgements[b];
                var delta = GetJudgementDelta(left, right);

                deltas.Add((a, b), delta);
            }
        }

        return deltas;
    }

    private SensingJudgements GetJudgementDelta(SensingJudgements a, SensingJudgements b) =>
        new()
        {
            DeltaPressure = GetSeverityDelta(a.DeltaPressure, b.DeltaPressure),
            DeltaTemperature = GetSeverityDelta(a.DeltaTemperature, b.DeltaTemperature),
            LowPressure = GetSeverityDelta(a.LowPressure, b.LowPressure),
            HighPressure = GetSeverityDelta(a.HighPressure, b.HighPressure),
            LowTemperature = GetSeverityDelta(a.LowTemperature, b.LowTemperature),
            HighTemperature = GetSeverityDelta(a.HighTemperature, b.HighTemperature),
        };

    private SensorSeverity GetSeverityDelta(SensorSeverity a, SensorSeverity b)
    {
        if (a == b)
            return SensorSeverity.Safe;

        var delta = Math.Abs(BitOperations.Log2((uint)a) - BitOperations.Log2((uint)b));
        return (SensorSeverity)(1 << (delta - 1)); // - 1 because the enum values starts at 0, not 1.
    }

    #endregion

    #region Phase 4 - Determine maximum severitiess

    private SensingJudgements AggregateMaxSeverities(List<SensingJudgements> judgements)
    {
        var aggregate = new SensingJudgements();
        foreach (var judgement in judgements)
        {
            aggregate.LowPressure = judgement.LowPressure > aggregate.LowPressure ? judgement.LowPressure : aggregate.LowPressure;
            aggregate.HighPressure = judgement.HighPressure > aggregate.HighPressure ? judgement.HighPressure : aggregate.HighPressure;
            aggregate.LowTemperature = judgement.LowTemperature > aggregate.LowTemperature ? judgement.LowTemperature : aggregate.LowTemperature;
            aggregate.HighTemperature = judgement.HighTemperature > aggregate.HighTemperature ? judgement.HighTemperature : aggregate.HighTemperature;
            aggregate.DeltaTemperature = judgement.DeltaTemperature > aggregate.DeltaTemperature ? judgement.DeltaTemperature : aggregate.DeltaTemperature;
            aggregate.DeltaPressure = judgement.DeltaPressure > aggregate.DeltaPressure ? judgement.DeltaPressure : aggregate.DeltaPressure;
        }
        return aggregate;
    }

    #endregion

}

public record SensingReading
{
    public float MinMoles = float.MaxValue;
    public float MaxMoles = float.MinValue;

    public float MinPressure = float.MaxValue;
    public float MaxPressure = float.MinValue;
    public float MinTemperature = float.MaxValue;
    public float MaxTemperature = float.MinValue;

    public float DeltaPressure => MaxPressure - MinPressure;
    public float DeltaTemperature => MaxTemperature - MinTemperature;

    public static SensingReading Combine(SensingReading left, SensingReading right) =>
        new()
        {
            MinMoles = Math.Min(left.MinMoles, right.MinMoles),
            MaxMoles = Math.Max(left.MaxMoles, right.MaxMoles),
            MinPressure = Math.Min(left.MinPressure, right.MinPressure),
            MaxPressure = Math.Max(left.MaxPressure, right.MaxPressure),
            MinTemperature = Math.Min(left.MinTemperature, right.MinTemperature),
            MaxTemperature = Math.Max(left.MaxTemperature, right.MaxTemperature)
        };
}

/// <summary>
/// All sensor's measured severities.
/// </summary>
public record SensingJudgements
{
    public SensorSeverity DeltaTemperature = SensorSeverity.Safe;
    public SensorSeverity LowTemperature = SensorSeverity.Safe;
    public SensorSeverity HighTemperature = SensorSeverity.Safe;

    public SensorSeverity DeltaPressure = SensorSeverity.Safe;
    public SensorSeverity LowPressure = SensorSeverity.Safe;
    public SensorSeverity HighPressure = SensorSeverity.Safe;

    public SensorSeverity Combined => DeltaTemperature | HighTemperature | LowTemperature |
                                      DeltaPressure | LowPressure | HighPressure;

    public SensorSeverity GetSeverity(Sensor sensor) =>
        sensor switch
        {
            Sensor.LowTemperature => LowTemperature,
            Sensor.HighTemperature => HighTemperature,
            Sensor.DeltaTemperature => DeltaTemperature,
            Sensor.LowPressure => LowPressure,
            Sensor.HighPressure => HighPressure,
            Sensor.DeltaPressure => DeltaPressure,
            _ => throw new ArgumentOutOfRangeException(nameof(sensor), sensor, null)
        };
}

public static class SensingJudgementsExtensions
{
    extension(SensingJudgements a)
    {
        public (Sensor?, SensorSeverity) GetHighest()
        {
            Sensor? highestSensor = null;
            var highestSeverity = SensorSeverity.Safe;

            foreach (var sensor in Enum.GetValues<Sensor>())
            {
                var severity = a.GetSeverity(sensor);
                if (severity > highestSeverity)
                {
                    highestSensor = sensor;
                    highestSeverity = severity;
                }
            }

            return (highestSensor, highestSeverity);
        }

        public SensingJudgements Max(SensingJudgements b) =>
            new()
            {
                DeltaPressure = a.DeltaPressure > b.DeltaPressure ? a.DeltaPressure : b.DeltaPressure,
                DeltaTemperature =
                    a.DeltaTemperature > b.DeltaTemperature ? a.DeltaTemperature : b.DeltaTemperature,
                LowPressure = a.LowPressure > b.LowPressure ? a.LowPressure : b.LowPressure,
                HighPressure = a.HighPressure > b.HighPressure ? a.HighPressure : b.HighPressure,
                LowTemperature = a.LowTemperature > b.LowTemperature ? a.LowTemperature : b.LowTemperature,
                HighTemperature = a.HighTemperature > b.HighTemperature ? a.HighTemperature : b.HighTemperature
            };
    }
}


