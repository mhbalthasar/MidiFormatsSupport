using System.Globalization;

namespace MidiCore.Parsers;

/// ABC music notation reader (the widely-implemented 2.1 subset): X/T/L/M/K headers, V: voices -> lanes, notes with accidentals, dotted lengths and rests.
public static class AbcParser
{
    public static Song Parse(string text)
    {
        var song = new Song { SourceFormat = "ABC notation", TicksPerQuarter = 480 };

        string? title = null;
        int defaultLength = 8;                       // L: 1/8
        double tempoBpm = 120;
        int meterNum = 4, meterDen = 4;

        var voices = new Dictionary<string, State>(StringComparer.Ordinal);
        State current = new();                        // notes before any V: land here
        current.Lane.Name = "ABC";

        // split headers from body: the body starts at the first K: field
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int bodyStart = lines.Length;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.Length >= 2 && t[1] == ':' && (t[0] == 'K' || t[0] == 'k'))
            {
                bodyStart = i;
                break;
            }
        }

        // headers
        for (int i = 0; i < bodyStart; i++)
        {
            string t = lines[i].Trim();
            if (t.Length < 2 || t[1] != ':') continue;
            char f = t[0];
            string v = t.Substring(2).Trim();
            switch (f)
            {
                case 'T': title ??= v; break;
                case 'L': defaultLength = ParseFraction(v, defaultLength); break;
                case 'M': ParseMeter(v, ref meterNum, ref meterDen); break;
                case 'Q': tempoBpm = ParseQ(v, tempoBpm); break;
            }
        }

        song.TimeSignatures.Add(new TimeSignatureEvent(0, meterNum, meterDen));
        song.Tempos.Add(new TempoEvent(0, tempoBpm));

        long abs = 0;
        int octave = 4;

        for (int i = bodyStart; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '%') continue;

            if (line.Length >= 2 && line[1] == ':' && line[0] == 'V')
            {
                string id = line.Substring(2).Split(' ')[0].Trim();
                if (id.Length == 0) id = "1";
                if (!voices.TryGetValue(id, out var found))
                {
                    found = new State { Id = id, Lane = new Lane { Name = $"Voice {id}" } };
                    voices[id] = found;
                }
                current = found;
                // strip the field, keep the rest of the line as music
                line = line.Substring(2).TrimStart();
                int sp = line.IndexOf(' ');
                line = sp >= 0 ? line.Substring(sp + 1) : "";
            }

            abs = ScanLine(line, ref octave, ref abs, defaultLength, current);
        }

        foreach (var v in voices.Values)
        {
            if (v.Lane.Notes.Count == 0) continue;
            song.AddLane(v.Lane);
        }
        if (current.Lane.Notes.Count > 0)
        {
            current.Lane.Name = title ?? "ABC";
            song.AddLane(current.Lane);
        }

        return song;
    }

    private static long ScanLine(string line, ref int octave, ref long abs, int defaultLength, State state)
    {
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (char.IsWhiteSpace(c) || c is '|' or '[' or ']' or ':' or '(' or ')')
            {
                i++;
                continue;
            }
            if (c == '%') break;
            if (c == '^' || c == '_' || c == '=')
            {
                i++;
                continue;
            }
            if (char.IsDigit(c) || c == '/')
            {
                ReadNum(line, ref i);   // trailing length digits handled with the note
                continue;
            }

            if (c is 'z' or 'x')   // rest
            {
                i++;
                long ticks = ReadTicks(line, ref i, defaultLength);
                abs += ticks;
                continue;
            }

            if (c is 'C' or 'D' or 'E' or 'F' or 'G' or 'A' or 'B'
                or 'c' or 'd' or 'e' or 'f' or 'g' or 'a' or 'b')
            {
                int alter = 0;
                if (c is '^' or '_' or '=') { }
                // accidentals can precede the note letter
                while (i < line.Length && (line[i] == '^' || line[i] == '_' || line[i] == '='))
                {
                    alter += line[i] == '^' ? 1 : -1;
                    i++;
                }
                if (i >= line.Length) break;
                char noteChar = line[i];
                if (!"CDEFGABcdefgab".Contains(noteChar))
                {
                    i++;
                    continue;
                }
                int semitone = char.ToUpperInvariant(noteChar) switch
                {
                    'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, _ => 11,
                };
                if (char.IsLower(noteChar)) octave += 1;
                i++;
                long ticks = ReadTicks(line, ref i, defaultLength);
                if (char.IsLower(noteChar)) octave -= 1;

                int midi = (octave + 1) * 12 + semitone + alter;
                state.Lane.Notes.Add(new Note(abs, Math.Max(1, ticks), Math.Clamp(midi, 0, 127)));
                abs += ticks;
                continue;
            }

            i++;
        }
        return abs;
    }

    /// Reads an optional numeric length (with dots and /2 style) and returns ticks (PPQ 480).
    private static long ReadTicks(string s, ref int i, int defaultLen)
    {
        long ticks = 480 * 4L / Math.Max(1, defaultLen);
        int start = i;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        int num = -1;
        if (i > start && int.TryParse(s.AsSpan(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            num = n;

        if (num > 0)
        {
            ticks = 480 * 4L / num;
        }
        else if (i < s.Length && s[i] == '/')
        {
            // "/" and "/2" halve the previous length
            i++;
            if (i < s.Length && char.IsDigit(s[i]))
            {
                int fs = i;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                if (int.TryParse(s.AsSpan(fs, i - fs), out int d) && d > 0)
                    ticks = 480 * 4L / (Math.Max(1, defaultLen) * d);
            }
            else
            {
                ticks = 480 * 4L / (Math.Max(1, defaultLen) * 2);
            }
        }

        // dotted: "c2." or "c2.."
        while (i < s.Length && s[i] == '.')
        {
            ticks = ticks + ticks / 2;
            i++;
        }
        return Math.Max(1, ticks);
    }

    private static void ReadNum(string s, ref int i)
    {
        while (i < s.Length && char.IsDigit(s[i])) i++;
    }

    private static int ParseFraction(string v, int fallback)
    {
        var parts = v.Split('/');
        if (parts.Length == 2 && int.TryParse(parts[0], out int n) && int.TryParse(parts[1], out int d) && d > 0)
            return Math.Max(1, n * 8 / d);
        if (int.TryParse(v, out int single) && single > 0) return single;
        return fallback;
    }

    private static void ParseMeter(string v, ref int num, ref int den)
    {
        if (v == "C") { num = 4; den = 4; return; }
        if (v == "C|") { num = 2; den = 2; return; }
        var p = v.Split('/');
        if (p.Length == 2 && int.TryParse(p[0], out int n) && int.TryParse(p[1], out int d))
        {
            num = n;
            den = d == 0 ? 4 : d;
        }
    }

    private static double ParseQ(string v, double fallback)
    {
        int eq = v.IndexOf('=');
        string num = eq >= 0 ? v.Substring(eq + 1) : v;
        string first = num.Trim().Split(' ')[0];
        if (double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) && bpm > 0)
            return bpm;
        return fallback;
    }

    private sealed class State
    {
        public string Id = "";
        public Lane Lane = new();
    }
}
