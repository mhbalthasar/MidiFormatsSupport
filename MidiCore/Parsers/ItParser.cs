using System.Buffers.Binary;
using System.Text;

namespace MidiCore.Parsers;

/// Impulse Tracker (.it) reader, per ITTECH.TXT: header at 0, orders at 0xC0, long offsets for instruments/samples/patterns.
public static class ItParser
{
    private const int TicksPerRow = 120;

    public static Song Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0xC0 || !data[..4].SequenceEqual(Encoding.ASCII.GetBytes("IMPM")))
            throw new FormatException("IT: bad signature");

        string moduleName = TrackerBinary.ReadFixedAscii(data, 4, 26);
        int ordNum = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0x20, 2));
        int insNum = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0x22, 2));
        int smpNum = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0x24, 2));
        int patNum = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0x26, 2));
        int cwt = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0x28, 2));
        int cmwt = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0x2A, 2));
        int flags = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0x2C, 2));
        int initialSpeed = data[0x33];
        int initialTempo = data[0x34];
        _ = smpNum; _ = flags;

        double bpm = initialTempo is > 0 and <= 255 ? initialTempo : 125;
        _ = initialSpeed;

        var song = new Song
        {
            SourceFormat = $"IT (Impulse Tracker {cwt >> 8}.{cwt & 0xFF}, cmwt={cmwt >> 8}.{cmwt & 0xFF:X2})",
            TicksPerQuarter = 480,
        };
        song.Tempos.Add(new TempoEvent(0, bpm));
        song.TimeSignatures.Add(new TimeSignatureEvent(0, 4, 4));

        int ordersOffset = 0xC0;
        int instOffsetTable = ordersOffset + ordNum;
        int smpOffsetTable = instOffsetTable + insNum * 4;
        int patOffsetTable = smpOffsetTable + smpNum * 4;

        var lanes = new Dictionary<int, Lane>();
        var open = new Dictionary<(int ch, int pitch), long>();
        var chInst = new Dictionary<int, int>();

        for (int p = 0; p < patNum; p++)
        {
            int off = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(patOffsetTable + p * 4, 4));
            if (off <= 0 || off >= data.Length) continue;    // offset 0 = 64-row empty pattern
            DecodePattern(data, off, p, lanes, open, chInst);
        }

        // instrument names for nicer labels
        var instNames = new string?[insNum];
        for (int i = 0; i < insNum; i++)
        {
            int off = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(instOffsetTable + i * 4, 4));
            if (off > 0 && off + 30 <= data.Length)
                instNames[i] = TrackerBinary.ReadFixedAscii(data, off + 4, 26);
        }

        // close dangling notes and finalize
        foreach (var (key, start) in open)
        {
            Lane lane = LaneFor(lanes, key.ch);
            lane.Notes.Add(new Note(start, TicksPerRow, key.pitch));
        }
        open.Clear();

        foreach (var kv in lanes)
        {
            Lane lane = kv.Value;
            if (lane.Notes.Count == 0) continue;
            int inst = chInst.TryGetValue(kv.Key, out var i) ? i : -1;
            lane.Name = inst >= 0 && inst < instNames.Length && !string.IsNullOrWhiteSpace(instNames[inst])
                ? instNames[inst]!
                : $"Ch {kv.Key + 1}";
            lane.Program = -1;    // tracker instruments are samples, not GM programs
            lane.Notes.RemoveAll(n => n.DurationTicks <= 0);
            song.AddLane(lane);
        }

        return song;
    }

    private static Lane LaneFor(Dictionary<int, Lane> lanes, int ch)
    {
        if (!lanes.TryGetValue(ch, out var lane))
            lanes[ch] = lane = new Lane { Name = $"Ch {ch + 1}", Channel = ch };
        return lane;
    }

    private static void DecodePattern(ReadOnlySpan<byte> data, int off, int patternIndex,
        Dictionary<int, Lane> lanes, Dictionary<(int ch, int pitch), long> open, Dictionary<int, int> chInst)
    {
        int len = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(off, 2));
        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(off + 2, 2));
        if (rowCount is <= 0 or > 256) rowCount = 64;
        ReadOnlySpan<byte> body = data.Slice(off + 4, Math.Min(len, data.Length - off - 4));

        int pos = 0;
        int chan = 0;    // running channel across rows (IT packs per row across channels)
        for (int row = 0; row < rowCount && pos < body.Length; row++)
        {
            long tick = (long)patternIndex * rowCount * TicksPerRow + row * TicksPerRow;
            chan = 0;
            while (pos < body.Length)
            {
                byte flag = body[pos++];
                if (flag == 0) break;                   // end of row

                if ((flag & 0x80) != 0)
                {
                    if (pos >= body.Length) break;
                    int inc = body[pos++];
                    chan += inc;                        // channel skip
                }
                int channel = Math.Clamp(chan, 0, 63);
                _ = channel;

                int note = -1, instrument = -1, volume = -1, command = -1, cmdValue = -1;
                if ((flag & 0x01) != 0 && pos < body.Length) note = body[pos++];
                if ((flag & 0x02) != 0 && pos < body.Length) instrument = body[pos++];
                if ((flag & 0x04) != 0 && pos < body.Length) volume = body[pos++];
                if ((flag & 0x08) != 0 && pos + 1 < body.Length) { command = body[pos++]; cmdValue = body[pos++]; }
                if ((flag & 0x10) != 0 && pos < body.Length) { note = body[pos++]; }    // last note on this channel, if nonzero

                // note 255 = note off (or release); 0 = no note on this cell
                if (note == 255)
                {
                    CloseNote(lanes, open, channel, tick);
                }
                else if (note > 0)
                {
                    CloseNote(lanes, open, channel, tick);
                    var lane = LaneFor(lanes, channel);
                    lane.Channel = channel;
                    int pitch = Math.Clamp(note + 11, 0, 127);   // IT notes are 1-based from C-0 (61 = C-5)
                    open[(channel, pitch)] = tick;
                }
                if (instrument > 0) chInst[channel] = instrument - 1;
                _ = volume; _ = command; _ = cmdValue;
            }
        }

        // close all still-open notes at the end of the pattern
        foreach (var (key, start) in open.ToArray())
        {
            Lane lane = LaneFor(lanes, key.ch);
            lane.Notes.Add(new Note(start, TicksPerRow, key.pitch));
            open.Remove(key);
        }
    }

    private static void CloseNote(Dictionary<int, Lane> lanes, Dictionary<(int ch, int pitch), long> open, int ch, long tick)
    {
        foreach (var (key, start) in open.ToArray())
        {
            if (key.ch != ch) continue;
            Lane lane = LaneFor(lanes, ch);
            lane.Notes.Add(new Note(start, Math.Max(1, tick - start), key.pitch));
            open.Remove(key);
        }
    }
}
