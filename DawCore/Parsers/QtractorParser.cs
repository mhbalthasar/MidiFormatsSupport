using System.Xml.Linq;
using MidiCore;
using MidiCore.Parsers;

namespace DawCore.Parsers;

/// Reads a Qtractor session (.qtr, XML) into a Song.
/// Qtractor keeps MIDI note data in external .mid files referenced by each clip's
/// <midi-clip><filename>, so the session's own path is required to resolve them.
public static class QtractorParser
{
    public static Song Parse(byte[] session, string? sessionPath)
    {
        var doc = XDocument.Load(new MemoryStream(session));
        var root = doc.Root ?? throw new FormatException("not a Qtractor session (empty XML)");
        if (root.Name.LocalName != "session")
            throw new FormatException("not a Qtractor session (missing <session>)");

        var props = root.Element("properties");
        double bpm = Text(props, "tempo", 120);
        double sampleRate = Text(props, "sample-rate", 44100);
        int beatsPerBar = (int)Text(props, "beats-per-bar", 4);

        var song = new Song { SourceFormat = "Qtractor session", TicksPerQuarter = DawXml.Ppq };
        song.Tempos.Add(new TempoEvent(0, bpm));
        song.TimeSignatures.Add(new TimeSignatureEvent(0, Math.Max(1, beatsPerBar), 4));

        foreach (var track in root.Element("tracks")?.Elements("track") ?? Enumerable.Empty<XElement>())
        {
            if (!string.Equals((string?)track.Attribute("type"), "midi", StringComparison.OrdinalIgnoreCase))
                continue;

            var trackProps = track.Element("properties");
            bool drums = IsTrue(Child(trackProps, "midi-drums"));
            int program = (int)Text(trackProps, "midi-program", -1);
            int bank = (int)Text(trackProps, "midi-bank", 0);

            var lane = DawXml.NewLane((string?)track.Attribute("name"), null);
            lane.IsDrum = drums;
            lane.Program = drums ? -1 : program;
            lane.Bank = bank;

            foreach (var clip in track.Element("clips")?.Elements("clip") ?? Enumerable.Empty<XElement>())
            {
                var midiClip = clip.Element("midi-clip");
                string? filename = midiClip?.Element("filename")?.Value?.Trim();
                if (string.IsNullOrEmpty(filename)) continue;

                byte[]? data = ResolveSource(filename, sessionPath);
                if (data == null) continue;

                Song sourceSong;
                try { sourceSong = SmfParser.Parse(data); }
                catch (FormatException) { continue; }

                var clipProps = clip.Element("properties");
                double startBeat = FramesToBeats(Text(clipProps, "start", 0), sampleRate, bpm);
                double offsetBeat = FramesToBeats(Text(clipProps, "offset", 0), sampleRate, bpm);
                double scale = sourceSong.TicksPerQuarter > 0 ? 1.0 / sourceSong.TicksPerQuarter : 0;
                double baseBeats = startBeat - offsetBeat;

                foreach (var srcLane in sourceSong.Lanes)
                {
                    foreach (var note in srcLane.Notes)
                        DawXml.AddNote(lane, DawXml.Ticks(baseBeats + note.StartTick * scale),
                                       DawXml.Ticks(note.DurationTicks * scale), note.Pitch, note.Velocity);
                    DawXml.AddCurves(lane, srcLane, baseBeats, scale);
                }
            }

            if (lane.Notes.Count > 0)
            {
                lane.Notes.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
                song.AddLane(lane);
            }
        }
        return song;
    }

    private static double FramesToBeats(double frames, double sampleRate, double bpm)
        => sampleRate > 0 ? frames / sampleRate * (bpm / 60.0) : 0;

    /// Qtractor stores the .mid beside (or under) the session, as a path relative to the session file.
    private static byte[]? ResolveSource(string filename, string? sessionPath)
    {
        if (string.IsNullOrEmpty(sessionPath))
            throw new FormatException("Qtractor session needs its file path to load MIDI clips; reopen it from disk.");

        if (Path.IsPathRooted(filename) && File.Exists(filename))
            return File.ReadAllBytes(filename);

        string dir = Path.GetDirectoryName(sessionPath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(sessionPath);
        var candidates = new[]
        {
            Path.Combine(dir, filename),
            Path.Combine(dir, name, filename),
            Path.Combine(dir, name, Path.GetFileName(filename)),
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return File.ReadAllBytes(candidate);
        return null;
    }

    private static string? Child(XElement? e, string child) => e?.Element(child)?.Value?.Trim();

    private static double Text(XElement? e, string child, double fallback)
        => double.TryParse(Child(e, child), System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static bool IsTrue(string? s)
        => s is not null && (s.Equals("true", StringComparison.OrdinalIgnoreCase)
                             || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
                             || s == "1");
}
