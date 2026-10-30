using System.Globalization;
using MidiCore;

namespace DawCore.Parsers;

/// Reads a REAPER .rpp project into a Song: one Lane per track, MIDI items merged.
public static class RppParser
{
    private const int Ppq = 480;   // TuneLab's ticks per quarter

    public static Song ParseFile(string path) => Parse(File.ReadAllText(path));

    public static Song Parse(string text)
    {
        var root = RppChunk.Parse(text);
        var project = root.Child("REAPER_PROJECT") ?? root;
        return ParseProject(project);
    }

    private static Song ParseProject(RppChunk project)
    {
        double bpm = 120;
        int num = 4, den = 4;
        if (project.Child("TEMPO") is { } tempo)
        {
            bpm = tempo.Number(0, 120);
            num = (int)tempo.Number(1, 4);
            den = (int)tempo.Number(2, 4);
        }

        var map = BuildTempoMap(project, bpm);

        var song = new Song { SourceFormat = "REAPER project", TicksPerQuarter = Ppq };
        foreach (var (tick, tbpm) in map.TempoEvents())
            song.Tempos.Add(new TempoEvent((long)Math.Round(tick), tbpm));
        if (song.Tempos.Count == 0)
            song.Tempos.Add(new TempoEvent(0, bpm));
        song.TimeSignatures.Add(new TimeSignatureEvent(0, num, den));

        int index = 0;
        foreach (var track in project.ChildrenNamed("TRACK"))
        {
            index++;
            if (ParseTrack(track, map, index) is { } lane)
                song.AddLane(lane);
        }
        return song;
    }

    /// Constant tempo from the project TEMPO line, refined by TEMPOENVEX points when they are anchored at time 0.
    private static TempoMap BuildTempoMap(RppChunk project, double bpm)
    {
        if (project.Child("TEMPOENVEX") is not { } env)
            return new TempoMap(Ppq, new[] { (0.0, bpm) });

        var points = new List<(double Seconds, double Bpm)>();
        foreach (var pt in env.ChildrenNamed("PT"))
        {
            if (pt.Args.Count < 2) continue;   // PT <seconds> <bpm> <shape> [<tension>]
            if (double.TryParse(pt.Arg(0), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
                && double.TryParse(pt.Arg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && v is > 1 and < 1000)
                points.Add((s, v));
        }
        points.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));

        // A first point far from 0 means the envelope is not time-anchored (beats); fall back to the base tempo.
        if (points.Count == 0 || points[0].Seconds > 0.5)
            return new TempoMap(Ppq, new[] { (0.0, bpm) });

        return new TempoMap(Ppq, points);
    }

    private static Lane? ParseTrack(RppChunk track, TempoMap map, int index)
    {
        string trackName = (track.Value("NAME") ?? "").Trim();
        string? instrument = InstrumentName(track);
        var (program, isDrum) = InstrumentGuesser.Guess(trackName, instrument);

        var lane = new Lane
        {
            Channel = -1,
            Program = isDrum ? -1 : program,
            Bank = 0,
            IsDrum = isDrum,
            Name = trackName.Length > 0 ? trackName : $"Track {index}",
            InstrumentNameHint = instrument,
        };

        foreach (var item in track.ChildrenNamed("ITEM"))
            if (MidiSource(item) is { } src)
                ReadMidiItem(item, src, map, lane);

        if (lane.Notes.Count == 0) return null;
        lane.Notes.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
        return lane;
    }

    /// Name of the first instrument plugin on the track (VSTi/AU i/CLAPi ...), ignoring effect plugins.
    private static string? InstrumentName(RppChunk track)
    {
        foreach (var fx in track.ChildrenNamed("FXCHAIN"))
            foreach (var vst in fx.ChildrenNamed("VST"))
            {
                string first = vst.Arg(0);
                int colon = first.IndexOf(':');
                if (colon < 0) continue;
                if (!first[..colon].Trim().EndsWith("i", StringComparison.OrdinalIgnoreCase)) continue;
                string name = first[(colon + 1)..].Trim();
                int paren = name.IndexOf(" (", StringComparison.Ordinal);
                if (paren > 0) name = name[..paren];
                if (name.Length > 0) return name.Trim();
            }
        return null;
    }

    /// The MIDI source of an item: a direct SOURCE MIDI, or one inside a SOURCE SECTION (takes).
    private static RppChunk? MidiSource(RppChunk item)
    {
        foreach (var c in item.ChildrenNamed("SOURCE"))
            if (IsMidi(c)) return c;
        if (item.Child("SOURCE SECTION") is { } section)
            foreach (var c in section.ChildrenNamed("SOURCE"))
                if (IsMidi(c)) return c;
        return null;
    }

    private static bool IsMidi(RppChunk source) =>
        string.Equals(source.Arg(0), "MIDI", StringComparison.OrdinalIgnoreCase);

    private static void ReadMidiItem(RppChunk item, RppChunk src, TempoMap map, Lane lane)
    {
        int ppq = 960;
        if (src.Child("HASDATA") is { Args.Count: > 1 } has
            && int.TryParse(has.Arg(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
            && p > 0)
            ppq = p;

        double itemSeconds = item.Number("POSITION");
        double slipSeconds = item.Number("SOFFS");
        long baseTicks = (long)Math.Round(map.SecondsToTicks(itemSeconds - slipSeconds));
        double scale = (double)Ppq / ppq;

        long cursor = 0;
        var open = new List<(int Channel, int Pitch, int Velocity, long Tick)>();
        var bends = new List<(long Tick, double Normalized)>();
        int bendRange = 2;
        int rpnMsb = -1, rpnLsb = -1;

        foreach (var e in src.Children)
        {
            if (e.Name != "E" && e.Name != "e") continue;      // "E" selected / "e" not; anything else (sysex X) is skipped
            if (e.Args.Count < 2) continue;
            if (!long.TryParse(e.Arg(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta)) continue;
            cursor += delta;

            if (!TryHex(e.Arg(1), out int status)) continue;
            int type = status & 0xF0;
            int channel = status & 0x0F;
            int data1 = TryHex(e.Arg(2), out var a) ? a : 0;
            int data2 = TryHex(e.Arg(3), out var b) ? b : 0;

            long tick = baseTicks + (long)Math.Round(cursor * scale);

            if (type == 0x90 && data2 > 0)
            {
                open.Add((channel, data1, data2, tick));
            }
            else if (type == 0x80 || (type == 0x90 && data2 == 0))
            {
                int idx = open.FindIndex(o => o.Channel == channel && o.Pitch == data1);
                if (idx >= 0)
                {
                    var (_, pitch, vel, start) = open[idx];
                    open.RemoveAt(idx);
                    var note = new Note(start, Math.Max(1, tick - start), pitch) { Velocity = vel };
                    if (note.IsValid) lane.AddNote(note);
                }
            }
            else if (type == 0xB0)
            {
                if (data1 == 101) rpnMsb = data2;
                else if (data1 == 100) rpnLsb = data2;
                else if (data1 == 6 && rpnMsb == 0 && rpnLsb == 0 && data2 > 0) bendRange = data2;
                if (data1 is not (0 or 32 or 100 or 101 or 6 or 38))
                    lane.AddController(data1, tick, data2);
            }
            else if (type == 0xE0)
            {
                int raw = (data2 << 7) | data1;
                bends.Add((tick, (raw - 8192) / 8192.0));
            }
            else if (type == 0xD0)
            {
                lane.AddAftertouch(tick, data1);
            }
        }

        // malformed: close any dangling note-ons with a 1-tick note
        foreach (var (_, pitch, vel, start) in open)
        {
            var note = new Note(start, 1, pitch) { Velocity = vel };
            if (note.IsValid) lane.AddNote(note);
        }

        lane.PitchBendRange = bendRange;
        foreach (var (tick, norm) in bends)
            lane.AddPitchBend(tick, norm * bendRange);
    }

    private static bool TryHex(string s, out int value) =>
        int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
