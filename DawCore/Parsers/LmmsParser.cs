using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MidiCore;

namespace DawCore.Parsers;

/// Reads an LMMS project (.mmp, XML; .mmpz is the gzipped twin) into a Song.
/// LMMS time is 48 ticks per quarter note; Beat/Bassline clips loop to fill their length.
public static class LmmsParser
{
    private const double TicksPerQuarter = 48.0;
    private const long TicksPerBar = 192;

    public static Song Parse(byte[] data) => ParseXml(Decode(data));

    public static Song ParseXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new FormatException("not an LMMS project (empty XML)");
        if (root.Name.LocalName != "lmms-project")
            throw new FormatException("not an LMMS project (missing <lmms-project>)");

        var head = root.Element("head");
        var song = new Song { SourceFormat = "LMMS project", TicksPerQuarter = DawXml.Ppq };
        song.Tempos.Add(new TempoEvent(0, Attr(head, "bpm", 140)));
        song.TimeSignatures.Add(new TimeSignatureEvent(0, (int)Attr(head, "timesig_numerator", 4), (int)Attr(head, "timesig_denominator", 4)));

        var container = root.Element("song")?.Elements("trackcontainer")
                            .FirstOrDefault(tc => DawXml.AttrText(tc, "type") == "song");
        if (container == null) return song;

        var tracks = container.Elements("track").ToList();
        var beatTracks = tracks.Where(t => DawXml.AttrText(t, "type") == "1").ToList();

        foreach (var track in tracks)
            if (DawXml.AttrText(track, "type") == "0")
                AddSongTrack(track, song);

        if (beatTracks.Count > 0) AddBeatTracks(beatTracks, song);
        return song;
    }

    private static void AddSongTrack(XElement track, Song song)
    {
        string? name = DawXml.AttrText(track, "name");
        string? instrument = Instrument(track);
        var lane = DawXml.NewLane(name, instrument);
        lane.InstrumentNameHint = name;   // LMMS instrument ids are generic; the track name reads better

        // song-view instrument clips carry an absolute song position
        foreach (var pattern in track.Elements("pattern"))
            AddPattern(lane, pattern, Attr(pattern, "pos", 0), 0, long.MaxValue);

        Finish(lane, song);
    }

    /// The Beat/Bassline editor is one shared instrument rack whose tracks each hold one step
    /// pattern per BB track (pattern k sits at pos k*192). A BB clip loops its pattern to fill.
    private static void AddBeatTracks(List<XElement> beatTracks, Song song)
    {
        var rack = beatTracks.Select(t => t.Element("bbtrack")?.Element("trackcontainer"))
                             .FirstOrDefault(c => c != null && c.Elements("track").Any());
        if (rack == null) return;

        var instruments = rack.Elements("track").ToList();
        var lanes = instruments.Select(t =>
        {
            string? name = DawXml.AttrText(t, "name");
            var lane = DawXml.NewLane(name, Instrument(t));
            lane.InstrumentNameHint = name;   // LMMS instrument ids are generic; the track name reads better
            return lane;
        }).ToList();

        for (int bb = 0; bb < beatTracks.Count; bb++)
        {
            var patterns = instruments.Select(t => PatternAt(t, bb)).ToList();
            long loop = Math.Max(TicksPerBar, patterns.Max(p => p == null ? 0 : (long)Attr(p, "len", 0)));

            foreach (var tco in beatTracks[bb].Elements("bbtco"))
            {
                long clipStart = (long)Attr(tco, "pos", 0);
                long clipEnd = clipStart + (long)Attr(tco, "len", 0);
                for (long rep = clipStart; rep < clipEnd; rep += loop)
                    for (int i = 0; i < patterns.Count; i++)
                        if (patterns[i] != null)
                            AddPattern(lanes[i], patterns[i]!, rep, clipStart, clipEnd);
            }
        }

        foreach (var lane in lanes) Finish(lane, song);
    }

    /// The pattern belonging to BB index `bb`; LMMS spaces them a bar apart (k*192).
    private static XElement? PatternAt(XElement instrument, int bb)
    {
        long pos = bb * TicksPerBar;
        var patterns = instrument.Elements("pattern").ToList();
        return patterns.FirstOrDefault(p => (long)Attr(p, "pos", -1) == pos)
               ?? (bb < patterns.Count ? patterns[bb] : null);
    }

    /// Adds a pattern's notes at `offset`; when bounded by [clipStart, clipEnd) the loop tiling is clipped.
    /// A negative LMMS note length means the note sounds until the end of its pattern.
    private static void AddPattern(Lane lane, XElement pattern, double offset, long clipStart, long clipEnd)
    {
        long patternLen = Math.Max(1, (long)Attr(pattern, "len", TicksPerQuarter));
        foreach (var note in pattern.Elements("note"))
        {
            int pitch = (int)Attr(note, "key", -1);
            if (pitch is < 0 or > 127) continue;
            double notePos = Attr(note, "pos", 0);
            double start = offset + notePos;
            double length = Attr(note, "len", TicksPerQuarter);
            if (length < 0) length = Math.Max(0, patternLen - notePos);
            if (clipEnd != long.MaxValue)
            {
                double end = Math.Min(start + length, clipEnd);
                length = end - start;
                if (start >= clipEnd || length <= 0) continue;
            }
            DawXml.AddNote(lane, Beats(start), Beats(length), pitch);
        }
    }

    private static void Finish(Lane lane, Song song)
    {
        if (lane.Notes.Count == 0) return;
        lane.Notes.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
        song.AddLane(lane);
    }

    private static string? Instrument(XElement track)
        => DawXml.AttrText(track.Element("instrumenttrack")?.Element("instrument"), "name");

    private static long Beats(double lmmsTicks) => DawXml.Ticks(lmmsTicks / TicksPerQuarter);

    private static double Attr(XElement? e, string name, double fallback)
    {
        string? v = e?.Attribute(name)?.Value;
        return double.TryParse(v, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }

    private static string Decode(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
        {
            using var input = new MemoryStream(data);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        return Encoding.UTF8.GetString(data);
    }
}
