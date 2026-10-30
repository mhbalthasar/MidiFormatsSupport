using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MidiCore;

namespace DawCore.Parsers;

/// Reads a Rosegarden project (.rg, gzip-compressed XML) into a Song: one Lane per track.
/// Rosegarden's time unit is 1/960 of a quarter note, and note onsets are delta-encoded.
public static class RosegardenParser
{
    private const double TicksPerQuarter = 960.0;

    public static Song Parse(byte[] data) => ParseXml(Decode(data));

    public static Song ParseXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new FormatException("not a Rosegarden project (empty XML)");
        if (root.Name.LocalName != "rosegarden-data")
            throw new FormatException("not a Rosegarden project (missing <rosegarden-data>)");

        var composition = root.Element("composition")
            ?? throw new FormatException("not a Rosegarden project (missing <composition>)");

        var song = new Song { SourceFormat = "Rosegarden project", TicksPerQuarter = DawXml.Ppq };

        double defaultTempo = Attr(composition, "defaultTempo", 120);
        foreach (var t in composition.Elements("tempo"))
        {
            double bph = Attr(t, "bph", 0);
            double bpm = bph > 0 ? bph / 60.0 : defaultTempo;
            song.Tempos.Add(new TempoEvent(Beats(Attr(t, "time", 0)), bpm));
        }
        if (song.Tempos.Count == 0)
            song.Tempos.Add(new TempoEvent(0, defaultTempo));

        foreach (var ts in composition.Elements("timesignature"))
        {
            int num = (int)Attr(ts, "numerator", 4);
            int den = (int)Attr(ts, "denominator", 4);
            double beatsPerBar = den > 0 ? num * 4.0 / den : num;
            int bar = beatsPerBar > 0 ? (int)Math.Round(Attr(ts, "time", 0) / TicksPerQuarter / beatsPerBar) : 0;
            song.TimeSignatures.Add(new TimeSignatureEvent(bar, num, den));
        }
        if (song.TimeSignatures.Count == 0)
            song.TimeSignatures.Add(new TimeSignatureEvent(0, 4, 4));

        var names = new Dictionary<int, string>();
        foreach (var t in composition.Elements("track"))
            names[(int)Attr(t, "id", 0)] = DawXml.AttrText(t, "label") ?? "";

        var lanes = new Dictionary<int, Lane>();
        Lane LaneFor(int id)
        {
            if (!lanes.TryGetValue(id, out var lane))
                lanes[id] = lane = DawXml.NewLane(names.TryGetValue(id, out var n) ? n : null, null);
            return lane;
        }

        // Segments are flat siblings of <composition>; each carries its owning track id and start.
        foreach (var segment in root.Elements("segment"))
        {
            int trackId = (int)Attr(segment, "track", -1);
            if (trackId < 0) continue;
            double segmentStart = Attr(segment, "start", 0);
            var lane = LaneFor(trackId);

            double position = 0;
            foreach (var e in segment.Elements("event"))
            {
                double time = e.Attribute("absoluteTime") != null ? Attr(e, "absoluteTime") : position;
                double duration = Attr(e, "duration", 0);
                position = time + duration;

                if (!string.Equals(DawXml.AttrText(e, "type"), "note", StringComparison.OrdinalIgnoreCase)) continue;
                var pitchProp = e.Elements("property").FirstOrDefault(x => DawXml.AttrText(x, "name") == "pitch");
                if (!int.TryParse(pitchProp?.Attribute("int")?.Value, out int pitch) || pitch is < 0 or > 127) continue;

                DawXml.AddNote(lane, Beats(segmentStart + time), Beats(duration), pitch);
            }
        }

        foreach (var lane in lanes.Values)
            if (lane.Notes.Count > 0)
            {
                lane.Notes.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
                song.AddLane(lane);
            }
        return song;
    }

    private static long Beats(double rosegardenTicks) => DawXml.Ticks(rosegardenTicks / TicksPerQuarter);

    private static double Attr(XElement e, string name, double fallback = 0)
        => double.TryParse(e.Attribute(name)?.Value, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var v)
               ? v : fallback;

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
