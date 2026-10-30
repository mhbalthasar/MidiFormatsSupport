using System.Xml.Linq;
using MidiCore;

namespace DawCore.Parsers;

/// Reads a Tracktion/Waveform edit (.tracktionedit) into a Song: one Lane per track, its MIDI clips merged.
public static class TracktionParser
{
    public static Song Parse(string xml)
    {
        var doc = XDocument.Parse(Sanitize(xml));
        var edit = doc.Root ?? throw new FormatException("not a Tracktion edit (empty XML)");
        if (edit.Name.LocalName != "EDIT")
            throw new FormatException("not a Tracktion edit (missing <EDIT>)");

        var song = new Song { SourceFormat = "Tracktion edit", TicksPerQuarter = DawXml.Ppq };

        var tempoSeq = edit.Element("TEMPOSEQUENCE");
        foreach (var t in tempoSeq?.Elements("TEMPO") ?? Enumerable.Empty<XElement>())
            song.Tempos.Add(new TempoEvent(DawXml.Ticks(DawXml.Attr(t, "startBeat")), DawXml.Attr(t, "bpm", 120)));
        if (song.Tempos.Count == 0)
            song.Tempos.Add(new TempoEvent(0, 120));

        var sig = tempoSeq?.Elements("TIMESIG").FirstOrDefault();
        song.TimeSignatures.Add(new TimeSignatureEvent(
            0,
            (int)DawXml.Attr(sig, "numerator", 4),
            (int)DawXml.Attr(sig, "denominator", 4)));

        foreach (var track in edit.Elements("TRACK"))
        {
            string? name = DawXml.AttrText(track, "name");
            string? instrument = VstName(track);

            var lane = DawXml.NewLane(name, instrument);
            foreach (var clip in track.Elements("MIDICLIP"))
            {
                double clipStart = DawXml.Attr(clip, "start");
                var sequence = clip.Element("SEQUENCE") ?? clip.Descendants("SEQUENCE").FirstOrDefault();
                if (sequence == null)
                {
                    if (name == null) name = DawXml.AttrText(clip, "name");
                    continue;
                }
                if (name == null) name = DawXml.AttrText(clip, "name");

                foreach (var n in sequence.Elements("NOTE"))
                {
                    int pitch = (int)DawXml.Attr(n, "p", -1);
                    double beat = clipStart + DawXml.Attr(n, "b");
                    DawXml.AddNote(lane, DawXml.Ticks(beat), DawXml.Ticks(DawXml.Attr(n, "l")), pitch);
                }
            }

            if (lane.Notes.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(lane.Name)) lane.Name = name ?? "";
                lane.Notes.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
                song.AddLane(lane);
            }
        }
        return song;
    }

    /// Tracktion writes binary plugin state as an attribute with an undeclared `base64:` prefix,
    /// which is not well-formed XML; renaming the prefix keeps the document parseable.
    private static string Sanitize(string xml) => xml.Replace("base64:", "base64_");

    /// Instrument plugin name from a VST/AU filename, e.g. "...\\Zebra2(x64).vst3" -> "Zebra2".
    private static string? VstName(XElement track)
    {
        foreach (var p in track.Elements("PLUGIN"))
        {
            string? file = DawXml.AttrText(p, "filename");
            if (file == null) continue;
            string baseName = Path.GetFileNameWithoutExtension(file);
            int paren = baseName.IndexOf('(');
            if (paren > 0) baseName = baseName[..paren];
            if (baseName.Length > 0) return baseName.Trim();
        }
        return null;
    }
}
