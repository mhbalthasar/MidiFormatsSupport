using System.Globalization;
using System.Xml.Linq;

namespace MidiCore.Parsers;

/// MusicXML 4.0/3.1 reader covering the practical subset: part per instrument &rarr; lane, measure/voice &rarr; nothing (voices merge), divisions + note duration &rarr; ticks, backup/forward &rarr; cursor rewind/advance, pitch/alter/octave/duration/type/voice/chord, midi-instrument + program number, tempo &lt;sound tempo&gt;, time signature, accidental-free enharmonic handling.
public static class MusicXmlParser
{
    public static Song ParseXml(XDocument doc)
    {
        XElement root = doc.Root ?? throw new FormatException("MusicXML: empty document");

        // score-partwise (the common case) vs score-timewise
        if (root.Name.LocalName.Equals("score-partwise", StringComparison.OrdinalIgnoreCase))
            return ParsePartwise(root);

        if (root.Name.LocalName.Equals("score-timewise", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("MusicXML: score-timewise is supported by converting to partwise first (use MuseScore to re-save).");

        throw new FormatException($"MusicXML: unknown root element <{root.Name.LocalName}>");
    }

    private static Song ParsePartwise(XElement root)
    {
        var song = new Song { SourceFormat = "MusicXML" };

        // instrument id -> name / program
        var scoreParts = new Dictionary<string, (string name, int program, string? midiName, int bank)>();
        foreach (var sp in root.Elements().Where(e => e.Name.LocalName == "part-list"))
        {
            foreach (var scorePart in sp.Elements().Where(e => e.Name.LocalName == "score-part"))
            {
                string id = scorePart.Attribute("id")?.Value ?? "";
                string partName = scorePart.Elements().FirstOrDefault(e => e.Name.LocalName == "part-name")?.Value?.Trim() ?? id;
                int program = -1;
                int bank = 0;
                string? midiName = null;
                foreach (var mi in scorePart.Elements().Where(e => e.Name.LocalName == "midi-instrument"))
                {
                    // <midi-program> is an element, not an attribute; <midi-bank> likewise.
                    program = Int32(mi.Elements().FirstOrDefault(e => e.Name.LocalName == "midi-program")?.Value, program);
                    bank = Int32(mi.Elements().FirstOrDefault(e => e.Name.LocalName == "midi-bank")?.Value, bank);
                    midiName = mi.Elements().FirstOrDefault(e => e.Name.LocalName == "midi-name")?.Value?.Trim() ?? midiName;
                }
                scoreParts[id] = (partName, program, midiName, bank);
            }
        }

        foreach (var part in root.Elements().Where(e => e.Name.LocalName == "part"))
        {
            string partId = part.Attribute("id")?.Value ?? "";
            if (!scoreParts.TryGetValue(partId, out var meta)) meta = (partId, -1, null, 0);

            // divisions = divisions per quarter note; can change mid-part
            int divisions = 1;
            double scale = 480.0;
            var divisionsEl = part.Elements().Elements()
                .FirstOrDefault(e => e.Name.LocalName == "attributes")
                ?.Elements().FirstOrDefault(e => e.Name.LocalName == "divisions");
            if (divisionsEl != null)
            {
                divisions = Int32(divisionsEl.Value, 1);
                scale = 480.0 / Math.Max(1, divisions);
            }

            var lane = new Lane
            {
                Name = meta.name,
                Program = meta.program >= 0 ? meta.program : 0,   // GM default is the piano
                Bank = meta.bank,
                InstrumentNameHint = meta.midiName ?? meta.name,
                Channel = -1,
            };

            long cursor = 0;
            long lastChordStart = 0;

            foreach (var measure in part.Elements().Where(e => e.Name.LocalName == "measure"))
            {
                long measureStart = cursor;
                long cursorInMeasure = 0;

                foreach (var el in measure.Elements())
                {
                    string ln = el.Name.LocalName;
                    if (ln == "note")
                    {
                        bool isChord = el.Elements().Any(x => x.Name.LocalName == "chord");
                        bool isGrace = el.Elements().Any(x => x.Name.LocalName == "grace");
                        long duration = 0;
                        if (!isGrace)
                        {
                            duration = (long)Math.Round(Int64(el.Elements().FirstOrDefault(x => x.Name.LocalName == "duration")?.Value, 0) * scale);
                        }

                        long start = isChord ? lastChordStart : cursorInMeasure;
                        bool isRest = el.Elements().Any(x => x.Name.LocalName == "rest");

                        if (!isChord)
                            lastChordStart = start;

                        if (!isGrace && !isRest)
                        {
                            int pitch = ReadPitch(el);
                            if (pitch is >= 0 and <= 127)
                                lane.Notes.Add(new Note(measureStart + start, Math.Max(1, duration), pitch));
                        }

                        if (!isChord)
                            cursorInMeasure += duration;   // grace notes take no time
                    }
                    else if (ln == "backup")
                    {
                        cursorInMeasure -= (long)Math.Round(Int64(el.Elements().FirstOrDefault(x => x.Name.LocalName == "duration")?.Value, 0) * scale);
                        if (cursorInMeasure < 0) cursorInMeasure = 0;
                    }
                    else if (ln == "forward")
                    {
                        cursorInMeasure += (long)Math.Round(Int64(el.Elements().FirstOrDefault(x => x.Name.LocalName == "duration")?.Value, 0) * scale);
                    }
                    else if (ln == "attributes")
                    {
                        var d = el.Elements().FirstOrDefault(x => x.Name.LocalName == "divisions");
                        if (d != null)
                        {
                            divisions = Int32(d.Value, divisions);
                            scale = 480.0 / Math.Max(1, divisions);
                        }
                        var t = el.Elements().FirstOrDefault(x => x.Name.LocalName == "time");
                        if (t != null)
                            ReadTimeSignature(t, song);
                    }
                    else if (ln == "direction")
                    {
                        var sound = el.Elements().FirstOrDefault(x => x.Name.LocalName == "sound");
                        if (sound != null)
                        {
                            var tempoAttr = sound.Attribute("tempo");
                            if (tempoAttr != null && Double(tempoAttr.Value, out double bpm) && bpm > 0)
                                AddTempo(song, measureStart + cursorInMeasure, bpm);
                        }
                    }
                }

                long measureLen = Math.Max(1, cursorInMeasure);
                cursor = measureStart + measureLen;
            }

            if (lane.Notes.Count > 0)
            {
                if (lane.Program < 0) lane.Program = 0;
                song.AddLane(lane);
            }
        }

        if (song.Tempos.Count == 0)
            song.Tempos.Add(new TempoEvent(0, 120));
        else
            song.Tempos.Sort((a, b) => a.Tick.CompareTo(b.Tick));

        return song;
    }

    /// Reads MusicXML bytes: bare .musicxml XML, or compressed .mxl (zip) whose META-INF/container.xml names the root score file.
    public static Song ParseBytes(byte[] bytes)
    {
        // .mxl is a zip that starts with "PK\x03\x04"
        if (bytes.Length > 4 && bytes[0] == 'P' && bytes[1] == 'K')
            return ParseCompressed(bytes);
        using var ms = new MemoryStream(bytes);
        return ParseXml(XDocument.Load(ms));
    }

    private static Song ParseCompressed(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        System.IO.Compression.ZipArchiveEntry? rootEntry = null;

        var container = zip.GetEntry("META-INF/container.xml");
        if (container != null)
        {
            using var cs = container.Open();
            var doc = XDocument.Load(cs);
            string? fullPath = doc.Root?.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
            if (!string.IsNullOrEmpty(fullPath))
                rootEntry = zip.GetEntry(fullPath) ?? zip.GetEntry(fullPath.Replace('\\', '/'));
        }
        if (rootEntry == null)   // no usable container.xml: fall back to the first .xml that is not in META-INF
        {
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    && !e.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase))
                {
                    rootEntry = e;
                    break;
                }
            }
        }
        if (rootEntry == null)
            throw new FormatException("MusicXML compressed (.mxl): no score file found inside the archive");

        using var entryStream = rootEntry.Open();
        return ParseXml(XDocument.Load(entryStream));
    }

    private static int ReadPitch(XElement note)
    {
        var pitchEl = note.Elements().FirstOrDefault(x => x.Name.LocalName == "pitch");
        if (pitchEl == null) return -1;
        string step = pitchEl.Elements().FirstOrDefault(x => x.Name.LocalName == "step")?.Value?.Trim()?.ToUpperInvariant() ?? "";
        int octave = Int32(pitchEl.Elements().FirstOrDefault(x => x.Name.LocalName == "octave")?.Value, 4);
        int alter = Int32(pitchEl.Elements().FirstOrDefault(x => x.Name.LocalName == "alter")?.Value, 0);

        int semitone = step switch
        {
            "C" => 0, "D" => 2, "E" => 4, "F" => 5, "G" => 7, "A" => 9, "B" => 11,
            _ => -1,
        };
        if (semitone < 0) return -1;
        return (octave + 1) * 12 + semitone + alter;
    }

    private static void ReadTimeSignature(XElement time, Song song)
    {
        int beats = Int32(time.Elements().FirstOrDefault(x => x.Name.LocalName == "beats")?.Value, 4);
        string beatType = time.Elements().FirstOrDefault(x => x.Name.LocalName == "beat-type")?.Value ?? "4";
        int denom = Int32(beatType, 4);
        // Only the first time signature we ever saw belongs at bar 0; later ones appear mid-score but MusicXML gives no bar number here — the first measure is where <time> shows up in a well-formed file, so bar 0 is the right (and only) default.
        int barIndex = song.TimeSignatures.Count == 0 ? 0 : song.TimeSignatures.Max(t => t.BarIndex) + 1;
        song.TimeSignatures.Add(new TimeSignatureEvent(barIndex, beats, denom));
    }

    private static void AddTempo(Song song, long tick, double bpm)
    {
        if (!song.Tempos.Exists(x => x.Tick == tick))
            song.Tempos.Add(new TempoEvent(tick, bpm));
    }

    private static int DefaultOf(XElement measure, string name, int fallback)
    {
        var v = measure.Elements().FirstOrDefault(x => x.Name.LocalName == name);
        return v == null ? fallback : Int32(v.Value, fallback);
    }

    private static int Int32(string? s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static long Int64(string? s, long fallback) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static bool Double(string? s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
