using System.IO.Compression;
using System.Xml.Linq;
using MidiCore;

namespace DawCore.Parsers;

/// Reads a DAWproject bundle (.dawproject: a ZIP holding project.xml) into a Song.
/// This is the open project-interchange standard exported by Bitwig, Waveform/Studio One
/// and others; only the MIDI notes of the arrangement are imported.
public static class DawProjectParser
{
    public static Song Parse(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("project.xml", StringComparison.OrdinalIgnoreCase));
        if (entry == null) throw new FormatException("not a DAWproject bundle (no project.xml inside)");

        using var stream = entry.Open();
        XDocument doc;
        try { doc = XDocument.Load(stream); }
        catch (Exception e) { throw new FormatException($"DAWproject project.xml is not valid XML: {e.Message}"); }
        return ParseXml(doc);
    }

    public static Song ParseXml(XDocument doc)
    {
        var root = doc.Root ?? throw new FormatException("not a DAWproject (empty project.xml)");
        if (root.Name.LocalName != "Project") throw new FormatException("not a DAWproject (missing <Project>)");

        var song = new Song { SourceFormat = "DAWproject", TicksPerQuarter = DawXml.Ppq };

        double tempo = 120;
        var transport = root.Element("Transport");
        var tempoEl = transport?.Element("Tempo");
        if (tempoEl != null) tempo = DawXml.AttrValue(tempoEl, "120");
        if (tempo is <= 0 or > 1000) tempo = 120;
        song.Tempos.Add(new TempoEvent(0, tempo));

        var sig = transport?.Element("TimeSignature");
        if (sig != null)
            song.TimeSignatures.Add(new TimeSignatureEvent(0, (int)DawXml.Attr(sig, "numerator", 4), (int)DawXml.Attr(sig, "denominator", 4)));

        // <Structure><Track id name> each holding a Channel/Devices tree; the instrument device names the sound.
        var trackInfo = new Dictionary<string, (string Name, string? Instr)>();
        foreach (var tr in root.Element("Structure")?.Elements("Track") ?? Enumerable.Empty<XElement>())
        {
            string? id = tr.Attribute("id")?.Value;
            if (id == null) continue;
            trackInfo[id] = (DawXml.AttrText(tr, "name") ?? "", Instrument(tr));
        }

        var top = root.Element("Arrangement")?.Element("Lanes");
        if (top == null) return song;

        double lanesScale = TimeScale(DawXml.AttrText(top, "timeUnit"), tempo);
        var lanes = new Dictionary<string, Lane>();

        foreach (var laneEl in top.Elements("Lanes"))
        {
            string? trackId = laneEl.Attribute("track")?.Value;
            if (trackId == null) continue;
            trackInfo.TryGetValue(trackId, out var info);
            var lane = GetLane(lanes, trackId, info.Name, info.Instr);
            foreach (var clip in laneEl.Element("Clips")?.Elements("Clip") ?? Enumerable.Empty<XElement>())
                AddClip(lane, clip, lanesScale, tempo);
        }

        foreach (var lane in lanes.Values)
        {
            if (lane.Notes.Count == 0) continue;
            lane.Notes.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
            song.AddLane(lane);
        }
        return song;
    }

    /// A clip's note times are relative to its content start; the clip's playStart is the offset
    /// into that content, so a note lands at clip.time + note.time - playStart.
    private static void AddClip(Lane lane, XElement clip, double lanesScale, double tempo)
    {
        var notes = clip.Element("Notes");
        if (notes == null) return;

        double contentScale = TimeScale(DawXml.AttrText(clip, "contentTimeUnit"), tempo);
        double clipBeats = DawXml.Attr(clip, "time");
        double playStartBeats = DawXml.Attr(clip, "playStart");
        double originBeats = clipBeats - playStartBeats;

        foreach (var n in notes.Elements("Note"))
        {
            int key = (int)DawXml.Attr(n, "key", -1);
            if (key is < 0 or > 127) continue;
            double startBeats = originBeats + DawXml.Attr(n, "time") * contentScale;
            double durBeats = DawXml.Attr(n, "duration") * contentScale;
            DawXml.AddNote(lane, DawXml.Ticks(startBeats * lanesScale), DawXml.Ticks(durBeats * lanesScale), key);
        }
    }

    private static Lane GetLane(Dictionary<string, Lane> lanes, string id, string name, string? instr)
    {
        if (lanes.TryGetValue(id, out var lane)) return lane;
        var created = DawXml.NewLane(name.Length > 0 ? name : null, instr);
        lanes[id] = created;
        return created;
    }

    /// The first instrument-role device's display name (BuiltinDevice / VST3 / VST2 / CLAP / AU).
    private static string? Instrument(XElement track)
    {
        var device = track.Descendants()
            .FirstOrDefault(d => (string?)d.Attribute("deviceRole") == "instrument");
        if (device == null) return null;
        return DawXml.AttrText(device, "name") ?? DawXml.AttrText(device, "deviceName");
    }

    /// Beats per unit; DAWproject defaults to beats, and only seconds needs a tempo to convert.
    private static double TimeScale(string? unit, double tempo) => unit?.ToLowerInvariant() switch
    {
        "seconds" => tempo / 60.0,
        _ => 1.0,
    };
}
