namespace MidiCore;

/// A canonical, format-agnostic representation of a parsed song.
public sealed class Song
{
    public List<TempoEvent> Tempos { get; } = new();
    public List<TimeSignatureEvent> TimeSignatures { get; } = new();
    public List<Lane> Lanes { get; } = new();
    /// Source format label, for diagnostics only.
    public string SourceFormat { get; set; } = "";

    public int TicksPerQuarter { get; set; } = 480;

    public void AddLane(Lane lane)
    {
        // one lane per (source track/channel/part) — dedupe by name is the caller's job
        Lanes.Add(lane);
    }
}

/// A tempo change at an absolute tick.
public readonly record struct TempoEvent(long Tick, double Bpm);

/// A time signature taking effect at an absolute bar index.
public readonly record struct TimeSignatureEvent(int BarIndex, int Numerator, int Denominator);

/// One independently-editable musical lane (DAW track).
public sealed class Lane
{
    /// Display name; from the track name/instrument name if available.
    public string Name { get; set; } = "";

    /// MIDI channel this lane belongs to, or -1 if not meaningful (MusicXML/ABC/MML lanes have no channel concept).
    public int Channel { get; set; } = -1;

    /// General MIDI program number 0-127, or -1 if unknown.
    public int Program { get; set; } = -1;

    /// GS bank number (from Bank Select MSB/LSB, CC 0 / CC 32). 0 = the default GM bank.
    public int Bank { get; set; }

    /// Drum lane (MIDI channel 10).
    public bool IsDrum { get; set; }

    public List<Note> Notes { get; } = new();

    /// Optional GM instrument name hint (e.g. from MusicXML instrument-name).
    public string? InstrumentNameHint { get; set; }

    /// Pitch-bend control points in semitones, at absolute ticks. Empty when the source has none.
    public List<CurvePoint> PitchBend { get; } = new();

    /// Pitch-bend range in semitones (from RPN 0, default 2). Applied when PitchBend is filled.
    public int PitchBendRange { get; set; } = 2;

    /// Continuous-controller curves keyed by MIDI CC number (0-127), at absolute ticks.
    public Dictionary<int, List<CurvePoint>> Controllers { get; } = new();

    /// Channel-pressure (aftertouch) curve, 0-127, at absolute ticks.
    public List<CurvePoint> Aftertouch { get; } = new();

    public bool HasAutomation => PitchBend.Count > 0 || Controllers.Count > 0 || Aftertouch.Count > 0;

    public void AddNote(Note n) => Notes.Add(n);

    public void AddController(int cc, double tick, double value)
    {
        if (!Controllers.TryGetValue(cc, out var list))
            Controllers[cc] = list = new List<CurvePoint>();
        list.Add(new CurvePoint(tick, value));
    }

    public void AddPitchBend(double tick, double semitones) => PitchBend.Add(new CurvePoint(tick, semitones));

    public void AddAftertouch(double tick, double value) => Aftertouch.Add(new CurvePoint(tick, value));
}

/// <summary>A single control point of a controller / pitch-bend / aftertouch curve (tick, value).</summary>
public readonly record struct CurvePoint(double Tick, double Value);

/// A single note, in absolute ticks from the song start.
public sealed class Note
{
    public long StartTick { get; }
    public long DurationTicks { get; }
    public int Pitch { get; }
    /// Attached lyric text if the source carried any (KAR, MIDI meta).
    public string? Lyric { get; set; }

    /// Note-on velocity 0-127; 100 when the source has no velocity concept.
    public int Velocity { get; set; } = 100;

    public Note(long startTick, long durationTicks, int pitch, string? lyric = null)
    {
        StartTick = startTick;
        DurationTicks = durationTicks;
        Pitch = pitch;
        Lyric = lyric;
    }

    public long EndTick => StartTick + DurationTicks;
    public bool IsValid => DurationTicks > 0 && Pitch is >= 0 and <= 127;
}
