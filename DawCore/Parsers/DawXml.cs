using System.Globalization;
using System.Xml.Linq;
using MidiCore;

namespace DawCore.Parsers;

/// Shared helpers for the XML-based DAW project parsers.
internal static class DawXml
{
    public const int Ppq = 480;   // TuneLab's ticks per quarter

    /// Beats -> ticks at 480 PPQ.
    public static long Ticks(double beats) => (long)Math.Round(beats * Ppq);

    public static double Attr(XElement? e, string name, double fallback = 0)
        => e != null && double.TryParse(e.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
               ? v
               : fallback;

    public static double AttrValue(XElement? e, string fallback = "0")
        => double.TryParse(e?.Attribute("Value")?.Value ?? fallback, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
               ? v
               : 0;

    public static string? AttrText(XElement? e, string name)
    {
        string? v = e?.Attribute(name)?.Value;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    public static string? Text(XElement? e)
    {
        string? v = e?.Value;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    /// A lane named/instrumented from source metadata, with a best-effort GM guess.
    public static Lane NewLane(string? trackName, string? instrumentHint)
    {
        var (program, isDrum) = InstrumentGuesser.Guess(trackName, instrumentHint);
        return new Lane
        {
            Channel = -1,
            Program = isDrum ? -1 : program,
            Bank = 0,
            IsDrum = isDrum,
            Name = trackName ?? "",
            InstrumentNameHint = instrumentHint,
        };
    }

    public static void AddNote(Lane lane, long startTick, long durationTicks, int pitch, int velocity = 100)
    {
        var note = new Note(startTick, Math.Max(1, durationTicks), pitch) { Velocity = velocity };
        if (note.IsValid) lane.AddNote(note);
    }

    /// Copies a source lane's controller / pitch-bend / aftertouch curves into the destination lane,
    /// shifting each point by the clip's start (in beats) and converting source ticks at the given
    /// source-PPQ scale. Used by the parsers that read notes from an embedded/adjacent SMF.
    public static void AddCurves(Lane lane, Lane source, double baseBeats, double tickScale)
    {
        foreach (var (cc, points) in source.Controllers)
            foreach (var p in points)
                lane.AddController(cc, Ticks(baseBeats + p.Tick * tickScale), p.Value);

        foreach (var p in source.PitchBend)
            lane.AddPitchBend(Ticks(baseBeats + p.Tick * tickScale), p.Value);

        foreach (var p in source.Aftertouch)
            lane.AddAftertouch(Ticks(baseBeats + p.Tick * tickScale), p.Value);
    }
}
