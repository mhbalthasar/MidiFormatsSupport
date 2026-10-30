using System.Buffers.Binary;
using System.Text;

namespace MidiCore.Parsers;

/// Reads a Standard MIDI File (SMF, "MThd") into a Song.
public static class SmfParser
{
    public static Song Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 14 || !data[..4].SequenceEqual(Encoding.ASCII.GetBytes("MThd")))
            throw new FormatException("not a Standard MIDI File (missing MThd)");

        int format = (data[8] << 8) | data[9];
        int ntr = (data[10] << 8) | data[11];
        int division = (data[12] << 8) | data[13];
        bool smpte = (division & 0x8000) != 0;
        int ticksPerQuarter = smpte || division == 0 ? 480 : division;

        var song = new Song { SourceFormat = $"SMF format {format}", TicksPerQuarter = ticksPerQuarter };

        int pos = 14;
        for (int t = 0; t < ntr && pos + 8 <= data.Length; t++)
        {
            if (!data.Slice(pos, 4).SequenceEqual(Encoding.ASCII.GetBytes("MTrk")))
                throw new FormatException($"track {t}: missing MTrk chunk");
            int len = BinaryPrimitives.ReadInt32BigEndian(data.Slice(pos + 4, 4));
            var body = data.Slice(pos + 8, len);
            pos += 8 + len;
            ParseTrack(body, song, t, ticksPerQuarter);
        }

        song.Tempos.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        // the first tempo of a file is at tick 0 by convention; guarantee it
        if (song.Tempos.Count == 0 || song.Tempos[0].Tick > 0)
            song.Tempos.Insert(0, new TempoEvent(0, 120));

        return song;
    }

    private sealed class ChannelState
    {
        public int Program = -1;
        public int BankMsb = 0;
        public int BankLsb = 0;
        public int Bank => (BankMsb << 7) | BankLsb;
        public readonly List<(long start, int pitch, int velocity)> Open = new();
        public readonly List<Note> Notes = new();
        public readonly Dictionary<long, string> Lyrics = new();
        public bool HasNote => Notes.Count > 0 || Open.Count > 0;

        // controller / bend / pressure capture
        public int BendRange = 2;                                   // RPN 0 (semitones)
        public int RpnMsb = -1, RpnLsb = -1;                        // pending RPN selector
        public readonly List<CurvePoint> PitchBend = new();          // normalized -1..+1
        public readonly Dictionary<int, List<CurvePoint>> Controllers = new();
        public readonly List<CurvePoint> Aftertouch = new();

        public void AddController(int cc, double tick, double value)
        {
            if (!Controllers.TryGetValue(cc, out var list))
                Controllers[cc] = list = new List<CurvePoint>();
            list.Add(new CurvePoint(tick, value));
        }

        public bool HasAutomation => PitchBend.Count > 0 || Controllers.Count > 0 || Aftertouch.Count > 0;
    }

    private static void ParseTrack(ReadOnlySpan<byte> body, Song song, int trackIndex, int ticksPerQuarter)
    {
        var channels = new Dictionary<int, ChannelState>();
        ChannelState Ch(int i) => channels.TryGetValue(i, out var c) ? c : channels[i] = new ChannelState();

        string? trackName = null;
        string? instrumentName = null;
        long abs = 0;
        byte running = 0;

        int p = 0;
        while (p < body.Length)
        {
            // delta-time VLQ
            long delta = 0;
            while (true)
            {
                byte b = body[p++];
                delta = (delta << 7) | (b & 0x7Fu);
                if ((b & 0x80) == 0) break;
            }
            abs += delta;

            byte status = body[p];
            if (status < 0x80)
            {
                status = running;
            }
            else
            {
                p++;
                if (status < 0xF0) running = status;
            }

            if (status == 0xFF) // meta
            {
                byte metaType = body[p++];
                int mlen = 0;
                while (true)
                {
                    byte b = body[p++];
                    mlen = (int)((mlen << 7) | (uint)(b & 0x7Fu));
                    if ((b & 0x80) == 0) break;
                }
                var payload = body.Slice(p, mlen);
                p += mlen;

                switch (metaType)
                {
                    case 0x03:
                        trackName ??= Encoding.UTF8.GetString(payload);
                        break;
                    case 0x04:
                        instrumentName ??= Encoding.UTF8.GetString(payload);
                        break;
                    case 0x05:
                        // lyric: attach to whichever channel holds the note starting here
                        foreach (var c in channels.Values)
                            if (!c.Lyrics.ContainsKey(abs))
                            {
                                c.Lyrics[abs] = Encoding.UTF8.GetString(payload);
                                break;
                            }
                        break;
                    case 0x51:
                    {
                        int usPerQuarter = (payload[0] << 16) | (payload[1] << 8) | payload[2];
                        double bpm = usPerQuarter <= 0 ? 120 : 60_000_000.0 / usPerQuarter;
                        if (!song.Tempos.Exists(x => x.Tick == abs))
                            song.Tempos.Add(new TempoEvent(abs, bpm));
                        break;
                    }
                    case 0x58:
                    {
                        byte num = payload[0];
                        int den = 1 << payload[1];
                        long ticksPerBar = ticksPerQuarter * 4L * num / den;
                        long barIndex = ticksPerBar == 0 ? abs / (ticksPerQuarter * 4) : abs / ticksPerBar;
                        song.TimeSignatures.Add(new TimeSignatureEvent((int)barIndex, num, den));
                        break;
                    }
                }
                continue;
            }

            if (status is 0xF0 or 0xF7) // sysex
            {
                int sl = 0;
                while (true)
                {
                    byte b = body[p++];
                    sl = (int)((sl << 7) | (uint)(b & 0x7Fu));
                    if ((b & 0x80) == 0) break;
                }
                p += sl;
                continue;
            }

            int type = status & 0xF0;
            int chan = status & 0x0F;

            switch (type)
            {
                case 0x80 or 0x90: // note off / note on
                {
                    int pitch = body[p++];
                    byte vel = body[p++];
                    bool isOn = type == 0x90 && vel > 0;
                    var c = Ch(chan);
                    if (isOn)
                    {
                        c.Open.Add((abs, pitch, vel));
                    }
                    else
                    {
                        // find the oldest open note of this pitch and close it
                        int idx = -1;
                        for (int i = 0; i < c.Open.Count; i++)
                            if (c.Open[i].pitch == pitch) { idx = i; break; }
                        if (idx >= 0)
                        {
                            var (start, _, onVel) = c.Open[idx];
                            c.Open.RemoveAt(idx);
                            c.Notes.Add(new Note(start, Math.Max(1, abs - start), pitch) { Velocity = onVel });
                        }
                    }
                    break;
                }
                case 0xA0: // polyphonic key pressure: not carried on the model
                    p += 2;
                    break;
                case 0xE0: // pitch bend: 14-bit, centered at 8192 (normalized here, scaled to the RPN range on flush)
                {
                    int lsb = body[p++];
                    int msb = body[p++];
                    int raw = (msb << 7) | lsb;
                    Ch(chan).PitchBend.Add(new CurvePoint(abs, (raw - 8192) / 8192.0));
                    break;
                }
                case 0xB0:
                {
                    int controller = body[p++];
                    byte value = body[p++];
                    var cState = Ch(chan);
                    if (controller == 0) cState.BankMsb = value;          // Bank Select MSB
                    else if (controller == 32) cState.BankLsb = value;   // Bank Select LSB
                    else if (controller == 101) cState.RpnMsb = value;   // RPN MSB
                    else if (controller == 100) cState.RpnLsb = value;   // RPN LSB
                    else if (controller is 6 or 38 && cState.RpnMsb == 0 && cState.RpnLsb == 0)
                    {
                        // Data Entry on RPN 0 = Pitch Bend Sensitivity (semitones).
                        if (controller == 6 && value > 0) cState.BendRange = value;
                    }
                    // Everything else is a real controller curve (banks/RPN selectors excluded).
                    if (controller is not (0 or 32 or 100 or 101 or 6 or 38))
                        cState.AddController(controller, abs, value);
                    break;
                }
                case 0xC0:
                    Ch(chan).Program = body[p++];
                    break;
                case 0xD0: // channel pressure (aftertouch)
                    Ch(chan).Aftertouch.Add(new CurvePoint(abs, body[p++]));
                    break;
            }
        }

        // flush each channel that surfaced in this track as its own lane
        foreach (var kv in channels)
        {
            int chan = kv.Key;
            var c = kv.Value;

            // close dangling note-ons (malformed files): give them a 1-tick duration
            foreach (var (start, pitch, onVel) in c.Open)
                c.Notes.Add(new Note(start, 1, pitch) { Velocity = onVel });
            c.Open.Clear();

            if (c.Notes.Count == 0 && c.Program < 0 && c.Lyrics.Count == 0)   // pre-resolution check: nothing to say
                continue;   // a channel that contributed nothing (e.g. the bare conductor track)

            // GM rule: a channel that carries notes but never sent a Program Change is program 0 (Acoustic Grand Piano) — not "unknown".
            int program = c.Program >= 0 ? c.Program : 0;

            var lane = new Lane
            {
                Channel = chan,
                Program = program,
                Bank = c.Bank,
                IsDrum = chan == 9,
                Name = BuildName(chan, program, trackName, instrumentName),
                InstrumentNameHint = instrumentName,
            };
            foreach (var n in c.Notes)
                if (n.IsValid)
                {
                    if (c.Lyrics.TryGetValue(n.StartTick, out var lyric))
                        n.Lyric = lyric;
                    lane.AddNote(n);
                }

            // controller curves (already captured per channel); pitch bend is normalized, so scale it by the RPN range now.
            lane.PitchBendRange = c.BendRange;
            foreach (var bp in c.PitchBend)
                lane.AddPitchBend(bp.Tick, bp.Value * c.BendRange);
            foreach (var controller in c.Controllers)
                lane.Controllers[controller.Key] = controller.Value;
            lane.Aftertouch.AddRange(c.Aftertouch);

            song.AddLane(lane);
        }
    }

    private static string BuildName(int chan, int program, string? trackName, string? instrumentName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(instrumentName)) parts.Add(instrumentName);
        if (!string.IsNullOrWhiteSpace(trackName) && !parts.Contains(trackName!)) parts.Add(trackName);
        if (parts.Count == 0 && program >= 0 && program < GmInstruments.Names.Length)
            parts.Add(GmInstruments.Names[program]);
        return parts.Count > 0 ? string.Join(" - ", parts) : $"Channel {chan + 1}";
    }
}
