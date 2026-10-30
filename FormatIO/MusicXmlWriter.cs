using System.Globalization;
using System.Text;
using MidiCore;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace MidiFormatsSupport;

/// Writes MusicXML 4.0 partwise: one part per TuneLab track, divisions=480, notes laid into measures by the project's time signature (default 4/4), forward gaps instead of backup for polyphony-free tracks.
internal static class MusicXmlWriter
{
    private const int Divisions = 480;

    public static byte[] Write(ProjectInfo info)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n");
        sb.Append("<!DOCTYPE score-partwise PUBLIC \"-//Recordare//DTD MusicXML 4.0 Partwise//EN\" \"http://www.musicxml.org/dtds/partwise.dtd\">\n");
        sb.Append("<score-partwise version=\"4.0\">\n");

        // part list
        sb.Append("  <part-list>\n");
        for (int i = 0; i < info.Tracks.Count; i++)
        {
            string id = "P" + (i + 1);
            sb.Append($"    <score-part id=\"{id}\">\n");
            sb.Append($"      <part-name>{Esc(NonEmpty(info.Tracks[i].Name, $"Track {i + 1}"))}</part-name>\n");
            if (TryReadBankProgram(PartInstrumentOf(info, i), out int bank, out int program))
            {
                sb.Append($"      <midi-instrument id=\"{id}-I1\">\n");
                sb.Append($"        <midi-bank>{bank}</midi-bank>\n");
                sb.Append($"        <midi-program>{program}</midi-program>\n");
                sb.Append($"        <midi-name>{System.Security.SecurityElement.Escape(GmInstruments.NameOf(program))}</midi-name>\n");
                sb.Append("      </midi-instrument>\n");
            }
            else
            {
                sb.Append($"      <midi-instrument id=\"{id}-I1\"><midi-program>1</midi-program></midi-instrument>\n");
            }
            sb.Append("    </score-part>\n");
        }
        sb.Append("  </part-list>\n");

        int numerator = 4, denominator = 4;
        if (info.TimeSignatures.Count > 0)
        {
            numerator = Math.Clamp(info.TimeSignatures[0].Numerator, 1, 32);
            denominator = Math.Clamp(info.TimeSignatures[0].Denominator, 1, 32);
        }
        double barTicks = Divisions * 4.0 * numerator / denominator;
        double firstTempo = info.Tempos.Count > 0 ? Math.Clamp(info.Tempos[0].Bpm, 1, 1000) : 120;

        for (int ti = 0; ti < info.Tracks.Count; ti++)
        {
            string id = "P" + (ti + 1);
            var notes = new List<(double start, long dur, int pitch, string lyric)>();
            foreach (var part in info.Tracks[ti].Parts)
            {
                if (part is not MidiPartInfo midi) continue;
                double basePos = midi.Pos;
                foreach (var n in midi.Notes)
                {
                    if (n.Dur <= 0) continue;
                    notes.Add((basePos + n.Pos, (long)Math.Max(1, Math.Round(n.Dur)), Math.Clamp(n.Pitch, 0, 127), n.Lyric ?? ""));
                }
            }
            notes.Sort((a, b) => a.start.CompareTo(b.start) is 0 ? b.dur.CompareTo(a.dur) : a.start.CompareTo(b.start));

            long totalTicks = notes.Count == 0
                ? (long)barTicks
                : (long)Math.Max(barTicks, Math.Ceiling(notes.Max(n => n.start + n.dur) / barTicks) * barTicks);

            sb.Append($"  <part id=\"{id}\">\n");
            long barStart = 0;
            int barNumber = 1;
            while (barStart < totalTicks)
            {
                long barEnd = barStart + (long)barTicks;
                sb.Append($"    <measure number=\"{barNumber}\">\n");
                if (barNumber == 1)
                {
                    sb.Append("      <attributes>\n");
                    sb.Append($"        <divisions>{Divisions}</divisions>\n");
                    sb.Append("        <key><fifths>0</fifths></key>\n");
                    sb.Append($"        <time><beats>{numerator}</beats><beat-type>{denominator}</beat-type></time>\n");
                    sb.Append("        <clef><sign>G</sign><line>2</line></clef>\n");
                    sb.Append("      </attributes>\n");
                    sb.Append("      <direction placement=\"above\">\n");
                    sb.Append("        <direction-type>\n");
                    sb.Append($"          <metronome><beat-unit>quarter</beat-unit><per-minute>{firstTempo.ToString(CultureInfo.InvariantCulture)}</per-minute></metronome>\n");
                    sb.Append("        </direction-type>\n");
                    sb.Append($"        <sound tempo=\"{firstTempo.ToString(CultureInfo.InvariantCulture)}\"/>\n");
                    sb.Append("      </direction>\n");
                }

                long cursor = barStart;
                bool wroteAny = false;
                foreach (var n in notes.Where(n => n.start >= barStart && n.start < barEnd).OrderBy(n => n.start))
                {
                    long gap = (long)n.start - cursor;
                    if (gap > 0)
                    {
                        sb.Append($"      <forward><duration>{gap.ToString(CultureInfo.InvariantCulture)}</duration></forward>\n");
                        cursor += gap;
                    }
                    sb.Append(BuildNote(n.pitch, n.dur, n.lyric));
                    cursor = (long)n.start + n.dur;
                    wroteAny = true;
                }
                if (!wroteAny || cursor < barEnd)
                {
                    long gap = Math.Max(0, barEnd - cursor);
                    if (gap > 0)
                        sb.Append($"      <forward><duration>{gap.ToString(CultureInfo.InvariantCulture)}</duration></forward>\n");
                }
                sb.Append("    </measure>\n");
                barStart = barEnd;
                barNumber++;
            }
            sb.Append("  </part>\n");
        }

        sb.Append("</score-partwise>\n");
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    private static string BuildNote(int pitch, long dur, string lyric)
    {
        int octave = Math.Clamp(pitch / 12 - 1, 0, 9);
        int semitone = pitch % 12;
        (string step, int alter) = PitchOf(semitone);

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(lyric) && System.Text.RegularExpressions.Regex.IsMatch(lyric, "^[-a-zA-Z']+$"))
        {
            sb.Append("      <lyric number=\"1\">\n");
            sb.Append($"        <text>{Esc(lyric)}</text>\n");
            sb.Append("      </lyric>\n");
        }
        else
        {
            sb.Append("      <lyric number=\"1\"><text>-</text></lyric>\n");
        }
        sb.Append("      <pitch>\n");
        sb.Append($"        <step>{step}</step>\n");
        if (alter != 0) sb.Append($"        <alter>{alter}</alter>\n");
        sb.Append($"        <octave>{octave}</octave>\n");
        sb.Append("      </pitch>\n");
        sb.Append($"      <duration>{dur.ToString(CultureInfo.InvariantCulture)}</duration>\n");
        sb.Append("      <voice>1</voice>\n");
        sb.Append($"      <type>{Durations.Name(dur)}</type>\n");
        return $"      <note>\n{sb}      </note>\n";
    }

    private static (string step, int alter) PitchOf(int semitone) => semitone switch
    {
        0 => ("C", 0), 1 => ("C", 1), 2 => ("D", 0), 3 => ("D", 1),
        4 => ("E", 0), 5 => ("F", 0), 6 => ("F", 1), 7 => ("G", 0),
        8 => ("G", 1), 9 => ("A", 0), 10 => ("A", 1), 11 => ("B", 0),
        _ => ("C", 0),
    };

    /// The part name of project track <paramref name="index"/>, or null.
    private static string? PartInstrumentOf(ProjectInfo info, int index) =>
        index >= 0 && index < info.Tracks.Count ? info.Tracks[index].Parts.FirstOrDefault()?.Name : null;

    private static bool TryReadBankProgram(string? name, out int bank, out int program)
    {
        bank = 0; program = -1;
        if (string.IsNullOrEmpty(name)) return false;
        var m = System.Text.RegularExpressions.Regex.Match(name, @"\[\s*(-?\d+)\s*_\s*(-?\d+)\s*\]\s*$");
        if (!m.Success) return false;
        bank = int.Parse(m.Groups[1].Value);
        program = int.Parse(m.Groups[2].Value);
        return program is >= 0 and <= 127;
    }

    private static string NonEmpty(string name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;

    private static string Esc(string s) => new System.Xml.Linq.XText(s).ToString();
}

/// Duration ticks -> MusicXML note-type name (plus dots when applicable).
internal static class Durations
{
    // (ticks, dots) thresholds, longest first
    private static readonly (long len, int dots, string name)[] Table =
    {
        (480 * 4, 0, "whole"),
        (480 * 3, 0, "half"),
        (480 * 2, 0, "quarter"),
        (480 * 3 / 2, 0, "eighth"),
        (480, 0, "quarter"),
        (240, 0, "eighth"),
        (120, 0, "16th"),
        (60, 0, "32nd"),
    };

    public static string Name(long ticks)
    {
        foreach (var t in Table)
            if (ticks >= t.len) return t.name;
        return "32nd";
    }
}
