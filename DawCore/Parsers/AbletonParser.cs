using System.Text;
using System.Xml.Linq;
using MidiCore;

namespace DawCore.Parsers;

/// Reads an Ableton Live Set (.als) into a Song: one Lane per MIDI track, its clips merged.
public static class AbletonParser
{
    public static Song Parse(byte[] data)
    {
        var doc = XDocument.Parse(Decode(data));
        var root = doc.Root ?? throw new FormatException("not an Ableton Live Set (empty XML)");
        if (root.Name.LocalName != "Ableton")
            throw new FormatException("not an Ableton Live Set (missing <Ableton>)");
        var liveSet = root.Element("LiveSet") ?? root;

        int num = 4, den = 4;
        var firstSig = liveSet.Descendants("RemoteableTimeSignature").FirstOrDefault();
        if (firstSig != null)
        {
            num = (int)DawXml.AttrValue(firstSig.Element("Numerator"), "4");
            den = (int)DawXml.AttrValue(firstSig.Element("Denominator"), "4");
        }

        var song = new Song { SourceFormat = "Ableton Live Set", TicksPerQuarter = DawXml.Ppq };
        song.Tempos.Add(new TempoEvent(0, ProjectTempo(liveSet)));
        song.TimeSignatures.Add(new TimeSignatureEvent(0, num, den));

        foreach (var track in TrackElements(liveSet, "MidiTrack"))
        {
            string? name = DawXml.AttrText(track.Element("Name")?.Element("EffectiveName"), "Value");

            var lane = DawXml.NewLane(name, null);
            foreach (var clip in track.Descendants("MidiClip"))
            {
                double clipStart = DawXml.Attr(clip, "Time");
                if (clipStart < -1_000_000) continue;   // session-view clip: no arrangement position

                foreach (var keyTrack in clip.Descendants("KeyTrack"))
                {
                    int pitch = (int)DawXml.AttrValue(keyTrack.Element("MidiKey"), "-1");
                    if (pitch is < 0 or > 127) continue;

                    var notes = keyTrack.Element("Notes");
                    if (notes == null) continue;
                    foreach (var n in notes.Elements("MidiNoteEvent"))
                    {
                        if (string.Equals(DawXml.AttrText(n, "IsEnabled"), "false", StringComparison.OrdinalIgnoreCase)) continue;
                        double beat = clipStart + DawXml.Attr(n, "Time");
                        double dur = DawXml.Attr(n, "Duration");
                        DawXml.AddNote(lane, DawXml.Ticks(beat), DawXml.Ticks(dur), pitch);
                    }
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

    private static double ProjectTempo(XElement liveSet)
    {
        var main = liveSet.Element("MainTrack") ?? liveSet.Element("MasterTrack");
        string? v = main?.Descendants("Tempo").FirstOrDefault()?.Element("Manual")?.Attribute("Value")?.Value;
        return double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bpm)
               && bpm is > 20 and < 1000 ? bpm : 120;
    }

    private static IEnumerable<XElement> TrackElements(XElement liveSet, params string[] kinds)
        => (liveSet.Element("Tracks")?.Elements() ?? Enumerable.Empty<XElement>())
               .Where(e => kinds.Contains(e.Name.LocalName));

    private static string Decode(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
        {
            using var input = new MemoryStream(data);
            using var gz = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        return Encoding.UTF8.GetString(data);   // Live 12 sometimes writes plain XML
    }
}
