using System.IO.Compression;
using System.Xml.Linq;
using MidiCore;

namespace DawCore.Parsers;

/// Reads a Renoise song (.xrns) into a Song: one Lane per sequencer track, patterns unrolled along the arrangement.
public static class RenoiseParser
{
    public static Song Parse(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        var songXml = zip.Entries.FirstOrDefault(e => e.Name.Equals("Song.xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new FormatException("not a Renoise song (no Song.xml inside the .xrns)");
        using var reader = new StreamReader(songXml.Open());
        return ParseXml(reader.ReadToEnd());
    }

    public static Song ParseXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new FormatException("not a Renoise song (empty XML)");
        if (root.Name.LocalName != "RenoiseSong")
            throw new FormatException("not a Renoise song (missing <RenoiseSong>)");

        var global = root.Element("GlobalSongData");
        double bpm = Value(global, "BeatsPerMin", 120);
        int linesPerBeat = Math.Max(1, (int)Value(global, "LinesPerBeat", 4));

        var song = new Song { SourceFormat = "Renoise song", TicksPerQuarter = DawXml.Ppq };
        song.Tempos.Add(new TempoEvent(0, bpm));
        song.TimeSignatures.Add(new TimeSignatureEvent(
            0, (int)Value(global, "SignatureNumerator", 4), (int)Value(global, "SignatureDenominator", 4)));

        var trackNames = TrackNames(root);
        var lanes = new List<Lane>();
        void EnsureLane(int index)
        {
            while (lanes.Count <= index)
            {
                string? name = lanes.Count < trackNames.Count ? trackNames[lanes.Count] : null;
                lanes.Add(DawXml.NewLane(name, null));
            }
        }

        var patterns = (PatternsElement(root)?.Elements("Pattern") ?? Enumerable.Empty<XElement>()).ToList();
        var sequence = SequencePatternIndices(root);

        double positionBeats = 0;
        foreach (int patternIndex in sequence)
        {
            var pattern = Resolve(patterns, patternIndex);
            if (pattern == null) continue;

            int lines = Math.Max(1, (int)Value(pattern, "NumberOfLines", 64));
            var patternTracks = (pattern.Element("Tracks")?.Elements("PatternTrack") ?? Enumerable.Empty<XElement>()).ToList();

            for (int trackIndex = 0; trackIndex < patternTracks.Count; trackIndex++)
            {
                EnsureLane(trackIndex);
                UnrollTrack(patternTracks[trackIndex], lanes[trackIndex], positionBeats, linesPerBeat);
            }

            positionBeats += (double)lines / linesPerBeat;
        }

        foreach (var lane in lanes)
            if (lane.Notes.Count > 0)
            {
                lane.Notes.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
                song.AddLane(lane);
            }
        return song;
    }

    /// Unrolls one pattern track: a note lasts until the next note/off in the same note-column.
    private static void UnrollTrack(XElement patternTrack, Lane lane, double startBeats, int linesPerBeat)
    {
        var open = new Dictionary<int, (long Tick, int Pitch)>();
        var lines = patternTrack.Element("Lines")?.Elements("Line") ?? Enumerable.Empty<XElement>();
        int lastIndex = 0;

        foreach (var line in lines)
        {
            int lineIndex = (int)DawXml.Attr(line, "index", lastIndex);
            lastIndex = lineIndex + 1;
            long tick = DawXml.Ticks(startBeats + (double)lineIndex / linesPerBeat);

            var columns = line.Element("NoteColumns")?.Elements("NoteColumn") ?? Enumerable.Empty<XElement>();
            int column = 0;
            foreach (var col in columns)
            {
                string? note = DawXml.Text(col.Element("Note"));
                if (note != null && !note.StartsWith('-'))
                {
                    if (note.Equals("OFF", StringComparison.OrdinalIgnoreCase))
                    {
                        if (open.Remove(column, out var prev))
                            DawXml.AddNote(lane, prev.Tick, tick - prev.Tick, prev.Pitch);
                    }
                    else if (ParseNote(note) is { } pitch)
                    {
                        if (open.Remove(column, out var prev))
                            DawXml.AddNote(lane, prev.Tick, tick - prev.Tick, prev.Pitch);
                        open[column] = (tick, pitch);
                    }
                }
                column++;
            }
        }

        long end = DawXml.Ticks(startBeats + (double)lastIndex / linesPerBeat);
        foreach (var (_, prev) in open)
            DawXml.AddNote(lane, prev.Tick, end - prev.Tick, prev.Pitch);
    }

    /// Ordered pattern indices of the arrangement. Newer Renoise uses <SequenceEntries>/<SequenceEntry>;
    /// older versions nest the ordered list in an inner <PatternSequence>.
    private static List<int> SequencePatternIndices(XElement root)
    {
        var ps = root.Element("PatternSequence");
        if (ps == null) return new List<int>();

        var entries = ps.Element("SequenceEntries");
        if (entries != null)
            return entries.Elements("SequenceEntry")
                          .Select(e => (int)Value(e, "Pattern", 0))
                          .ToList();

        var inner = ps.Element("PatternSequence") ?? ps;
        return inner.Elements("Pattern")
                    .Select(p => int.TryParse(DawXml.Text(p), out var v) ? v : 0)
                    .ToList();
    }

    /// Renoise keeps the pattern list under <PatternPool>/<Patterns> in some versions.
    private static XElement? PatternsElement(XElement root)
        => root.Element("Patterns") ?? root.Element("PatternPool")?.Element("Patterns");

    private static XElement? Resolve(List<XElement> patterns, int index)
    {
        if (index < 0 || index >= patterns.Count) return null;
        int alias = (int)DawXml.AttrValue(patterns[index].Element("AliasPatternIndex"), "-1");
        return alias >= 0 && alias < patterns.Count ? patterns[alias] : patterns[index];
    }

    private static List<string> TrackNames(XElement root)
        => (root.Element("Tracks")?.Elements("SequencerTrack") ?? Enumerable.Empty<XElement>())
               .Select(t => DawXml.Text(t.Element("Name")) ?? "")
               .ToList();

    private static double Value(XElement? parent, string child, double fallback)
    {
        string? v = DawXml.Text(parent?.Element(child));
        return double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
               ? d : fallback;
    }

    /// "C-4" / "C#4" / "48" -> MIDI note; anything else -> null.
    private static int? ParseNote(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        if (int.TryParse(s, out var numeric)) return numeric is >= 0 and <= 127 ? numeric : null;

        int semi = char.ToUpperInvariant(s[0]) switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => -1,
        };
        if (semi < 0 || s.Length < 2) return null;

        int i = 1;
        if (s[i] == '#') { semi++; i++; }
        else if (s[i] == '-') { i++; }
        if (i >= s.Length || !int.TryParse(s[i..], out var octave)) return null;

        int note = octave * 12 + semi;
        return note is >= 0 and <= 127 ? note : null;
    }
}
