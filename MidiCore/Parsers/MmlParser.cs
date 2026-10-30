using System.Globalization;

namespace MidiCore.Parsers;

/// Music Macro Language (MML) parser.
public static class MmlParser
{
    public static Song Parse(string text)
    {
        var song = new Song { SourceFormat = "MML", TicksPerQuarter = 480 };
        var parser = new Scanner(text, song);
        parser.Run();
        return song;
    }

    private sealed class Scanner
    {
        private readonly string _s;
        private readonly Song _song;
        private int _p;

        private Lane? _lane;
        private int _octave = 4;
        private int _defaultLen = 4;
        private long _abs;
        private readonly List<(long tick, int bpm)> _pendingTempos = new();

        public Scanner(string s, Song song) { _s = s; _song = song; }

        public void Run()
        {
            while (Peek() >= 0)
            {
                int before = _p;
                char c = (char)Peek()!;
                if (char.IsWhiteSpace(c)) { _p++; continue; }
                if (c is '#' or '/')
                {
                    SkipLine();
                    continue;
                }
                if (c == '@')
                {
                    _p++;
                    int ch = ReadInt();
                    if (ch < 0) ch = LaneCount();
                    _lane = new Lane { Name = $"MML Track {ch + 1}", Channel = ch };
                    _song.AddLane(_lane);
                    continue;
                }
                if (c is 't' or 'T')
                {
                    _p++;
                    int v = ReadInt();
                    if (v > 0) _pendingTempos.Add((_abs, v));
                    continue;
                }
                if (c is 'v' or 'V' or 'q' or 'Q' or 'w' or 'W' or 'x' or 'X' or 's' or 'S')
                {
                    // velocity / gate / transpose / misc expressives: not representable in our model, but the argument must still be consumed so the rest parses correctly.
                    _p++;
                    ReadInt();
                    ReadLengthNoDots();
                    continue;
                }
                if (c is 'o' or 'O')
                {
                    _p++;
                    int v = ReadInt();
                    if (v is >= 0 and <= 10) _octave = v;
                    continue;
                }
                if (c == '>') { _p++; _octave = Math.Min(10, _octave + 1); continue; }
                if (c == '<') { _p++; _octave = Math.Max(0, _octave - 1); continue; }
                if (c is 'l' or 'L') { _p++; int v = ReadInt(); if (v > 0) _defaultLen = v; continue; }
                if (c is 'n' or 'N')
                {
                    _p++;
                    int n = ReadInt();
                    if (n is < 0 or > 127) continue;
                    long ticks = ReadLength();
                    bool tied = ReadTie();
                    AddNote(n, ticks, tied);
                    continue;
                }
                if (c is 'r' or 'R')
                {
                    _p++;
                    ReadLength();
                    continue;
                }
                if ("cdefgab".IndexOf(char.ToLowerInvariant(c)) >= 0)
                {
                    int semitone = char.ToLowerInvariant(c) switch { 'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5, 'g' => 7, 'a' => 9, _ => 11 };
                    _p++;
                    while (Peek() is (int)'#' or (int)'+' or (int)'-')
                    {
                        semitone += _s[_p] == '-' ? -1 : 1;
                        _p++;
                    }
                    long ticks = ReadLength();
                    bool tied = ReadTie();
                    int midi = (_octave + 1) * 12 + semitone;
                    AddNote(Math.Clamp(midi, 0, 127), ticks, tied);
                    continue;
                }
                _p++;  // ignore anything we don't understand

                // Progress guard: every iteration must consume at least one character, so a directive with a missing argument cannot loop forever.
                if (_p == before) _p++;
            }

            FlushTempos();
        }

        // ---- helpers ----

        private Lane Lane()
        {
            if (_lane == null)
            {
                _lane = new Lane { Name = "MML Track 1", Channel = 0 };
                _song.AddLane(_lane);
            }
            return _lane;
        }

        private int LaneCount() => _song.Lanes.Count == 0 ? 0 : _song.Lanes.Count - 1;

        private int? Peek() => _p < _s.Length ? _s[_p] : null;

        private void SkipLine() { while (_p < _s.Length && _s[_p] != '\n') _p++; }

        private int ReadInt()
        {
            if (_p < _s.Length && (_s[_p] == '+' || _s[_p] == '-')) _p++;
            int start = _p;
            while (_p < _s.Length && char.IsDigit(_s[_p])) _p++;
            return _p == start ? -1 : int.Parse(_s.AsSpan(start, _p - start), NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private void ReadLengthNoDots() { while (_p < _s.Length && char.IsDigit(_s[_p])) _p++; }

        private long ReadLength()
        {
            int start = _p;
            while (_p < _s.Length && char.IsDigit(_s[_p])) _p++;
            int len = _p == start ? _defaultLen
                : int.Parse(_s.AsSpan(start, _p - start), NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (len <= 0) len = _defaultLen;
            long baseTicks = 480 * 4L / len;
            int dots = 0;
            while (_p < _s.Length && _s[_p] == '.') { dots++; _p++; }
            double value = baseTicks;
            double add = baseTicks / 2.0;
            for (int i = 0; i < dots; i++) { value += add; add /= 2; }
            return (long)Math.Round(value);
        }

        private bool ReadTie()
        {
            if (Peek() == '&')
            {
                _p++;
                return true;
            }
            return false;
        }

        private void AddNote(int pitch, long ticks, bool tied)
        {
            var lane = Lane();
            if (tied)
            {
                for (int i = lane.Notes.Count - 1; i >= 0; i--)
                {
                    if (lane.Notes[i].Pitch == pitch)
                    {
                        var prev = lane.Notes[i];
                        lane.Notes[i] = new Note(prev.StartTick, prev.DurationTicks + ticks, pitch, prev.Lyric);
                        _abs += ticks;
                        return;
                    }
                }
            }
            lane.Notes.Add(new Note(_abs, ticks, pitch));
            _abs += ticks;
        }

        private void FlushTempos()
        {
            if (_pendingTempos.Count == 0)
            {
                _song.Tempos.Add(new TempoEvent(0, 120));
                return;
            }
            foreach (var (tick, bpm) in _pendingTempos)
                _song.Tempos.Add(new TempoEvent(tick, bpm));
            if (_song.Tempos[0].Tick > 0)
                _song.Tempos.Insert(0, new TempoEvent(0, 120));
        }
    }
}
