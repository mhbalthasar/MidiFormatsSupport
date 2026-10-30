using System.Text;
using System.Xml.Linq;
using MidiCore;
using MidiCore.Parsers;

namespace DawCore.Parsers;

/// Reads an Ardour session (.ardour, XML) into a Song.
/// Ardour keeps MIDI note data in external .mid files under interchange/&lt;session&gt;/midifiles,
/// so the session's own path is required to resolve them.
public static class ArdourParser
{
    private const int ArdourTicksPerBeat = 1920;

    public static Song Parse(byte[] session, string? sessionPath)
    {
        var doc = XDocument.Load(new MemoryStream(session));
        var root = doc.Root ?? throw new FormatException("not an Ardour session (empty XML)");
        if (root.Name.LocalName != "Session")
            throw new FormatException("not an Ardour session (missing <Session>)");

        double sampleRate = Attr(root, "sample-rate", 48000);
        string? sessionName = (string?)root.Attribute("name");

        var song = new Song { SourceFormat = "Ardour session", TicksPerQuarter = DawXml.Ppq };
        double bpm = 120;
        double beatsPerBar = 4;
        var tempoMap = root.Element("TempoMap");
        var tempo = tempoMap?.Element("Tempo");
        if (tempo != null) bpm = Attr(tempo, "beats-per-minute", 120);
        var meter = tempoMap?.Element("Meter");
        if (meter != null)
            beatsPerBar = Attr(meter, "divisions-per-bar", 4) * 4.0 / Math.Max(1, Attr(meter, "note-type", 4));
        song.Tempos.Add(new TempoEvent(0, bpm));
        song.TimeSignatures.Add(new TimeSignatureEvent(0, (int)Math.Max(1, beatsPerBar), 4));

        var sources = (root.Element("Sources")?.Elements("Source") ?? Enumerable.Empty<XElement>())
            .Where(s => s.Attribute("id") != null)
            .ToDictionary(s => (string)s.Attribute("id")!, s => s);
        var playlists = (root.Element("Playlists")?.Elements("Playlist") ?? Enumerable.Empty<XElement>()).ToList();

        foreach (var route in root.Element("Routes")?.Elements("Route") ?? Enumerable.Empty<XElement>())
        {
            if (!string.Equals((string?)route.Attribute("default-type"), "midi", StringComparison.OrdinalIgnoreCase))
                continue;

            string routeName = (string?)route.Attribute("name") ?? "MIDI";
            string? playlistRef = (string?)route.Element("Diskstream")?.Attribute("playlist");
            var playlist = playlists.FirstOrDefault(p => (string?)p.Attribute("name") == playlistRef)
                           ?? playlists.FirstOrDefault(p => (string?)p.Attribute("id") == playlistRef);
            if (playlist == null) continue;

            var lane = DawXml.NewLane(routeName, null);
            foreach (var region in playlist.Elements("Region"))
            {
                string? sourceId = (string?)region.Attribute("source-0") ?? (string?)region.Attribute("master-source-0");
                if (sourceId == null || !sources.TryGetValue(sourceId, out var source)) continue;
                if (!string.Equals((string?)source.Attribute("type"), "midi", StringComparison.OrdinalIgnoreCase)) continue;

                byte[]? midi = ResolveSource((string?)source.Attribute("name"), sessionPath, sessionName);
                if (midi == null) continue;

                Song sourceSong;
                try { sourceSong = SmfParser.Parse(midi); }
                catch (FormatException) { continue; }

                double regionStart = RegionStartBeats(region, sampleRate, bpm, beatsPerBar);
                double sourceOffset = SamplesToBeats(Attr(region, "start", 0), sampleRate, bpm);
                double scale = sourceSong.TicksPerQuarter > 0 ? 1.0 / sourceSong.TicksPerQuarter : 0;
                double baseBeats = regionStart - sourceOffset;
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

    /// The region's musical start: prefer Ardour's bar|beat|tick position, fall back to sample time.
    private static double RegionStartBeats(XElement region, double sampleRate, double bpm, double beatsPerBar)
    {
        string? bbt = (string?)region.Attribute("bbt-position");
        if (bbt != null)
        {
            var parts = bbt.Split('|');
            if (parts.Length == 3 && int.TryParse(parts[0], out int bar) && int.TryParse(parts[1], out int beat)
                && int.TryParse(parts[2], out int tick))
                return (bar - 1) * beatsPerBar + (beat - 1) + (double)tick / ArdourTicksPerBeat;
        }
        return SamplesToBeats(Attr(region, "position", 0), sampleRate, bpm);
    }

    private static double SamplesToBeats(double samples, double sampleRate, double bpm)
        => sampleRate > 0 ? samples / sampleRate * (bpm / 60.0) : 0;

    /// Ardour stores MIDI beside the session: interchange/&lt;session&gt;/midifiles/&lt;name&gt;.
    private static byte[]? ResolveSource(string? sourceName, string? sessionPath, string? sessionName)
    {
        if (string.IsNullOrEmpty(sourceName)) return null;
        if (string.IsNullOrEmpty(sessionPath))
            throw new FormatException("Ardour session needs its file path to load MIDI sources; reopen it from disk.");

        string dir = Path.GetDirectoryName(sessionPath) ?? ".";
        string name = sessionName ?? Path.GetFileNameWithoutExtension(sessionPath);
        var candidates = new[]
        {
            Path.Combine(dir, "interchange", name, "midifiles", sourceName),
            Path.Combine(dir, "interchange", name, sourceName),
            Path.Combine(dir, "interchange", sourceName),
            Path.Combine(dir, sourceName),
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return File.ReadAllBytes(candidate);
        return null;
    }

    private static double Attr(XElement? e, string name, double fallback = 0)
    {
        string? v = e?.Attribute(name)?.Value;
        return double.TryParse(v, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }
}
