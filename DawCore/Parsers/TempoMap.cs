namespace DawCore.Parsers;

/// Converts between project time (seconds) and musical time (ticks) under a piecewise-constant tempo.
public sealed class TempoMap
{
    private readonly (double Seconds, double Bpm)[] _points;
    private readonly double[] _beatsAt;      // cumulative beats at each point

    public double Ppq { get; }

    /// points: (seconds, bpm) sorted ascending; an empty list means a constant 120 BPM.
    public TempoMap(double ppq, IReadOnlyList<(double Seconds, double Bpm)> points)
    {
        Ppq = ppq <= 0 ? 480 : ppq;

        var list = points.Where(p => p.Bpm > 0).OrderBy(p => p.Seconds).ToList();
        if (list.Count == 0 || list[0].Seconds > 0.0001)
            list.Insert(0, (0, list.Count > 0 ? list[0].Bpm : 120));
        _points = list.ToArray();

        _beatsAt = new double[_points.Length];
        for (int i = 1; i < _points.Length; i++)
            _beatsAt[i] = _beatsAt[i - 1] + (_points[i].Seconds - _points[i - 1].Seconds) * _points[i - 1].Bpm / 60.0;
    }

    public static TempoMap Constant(double bpm) => new(480, new[] { (0.0, bpm) });

    public double SecondsToTicks(double seconds)
    {
        double beats = 0, prev = 0, bpm = _points[0].Bpm;
        for (int i = 1; i < _points.Length; i++)
        {
            if (seconds <= _points[i].Seconds)
                return (beats + (seconds - prev) * bpm / 60.0) * Ppq;
            beats += (_points[i].Seconds - prev) * bpm / 60.0;
            prev = _points[i].Seconds;
            bpm = _points[i].Bpm;
        }
        return (beats + (seconds - prev) * bpm / 60.0) * Ppq;
    }

    public double TicksToSeconds(double ticks)
    {
        double beats = ticks / Ppq;
        for (int i = _points.Length - 1; i >= 0; i--)
            if (beats >= _beatsAt[i])
                return _points[i].Seconds + (beats - _beatsAt[i]) * 60.0 / _points[i].Bpm;
        return beats * 60.0 / _points[0].Bpm;
    }

    /// The tempo changes as (tick, bpm), for a Song's tempo map.
    public IEnumerable<(double Tick, double Bpm)> TempoEvents()
    {
        for (int i = 0; i < _points.Length; i++)
            yield return (_beatsAt[i] * Ppq, _points[i].Bpm);
    }
}
