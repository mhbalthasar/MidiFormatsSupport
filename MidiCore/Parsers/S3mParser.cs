using System.Buffers.Binary;
using System.Text;

namespace MidiCore.Parsers;

/// Scream Tracker 3 (.s3m) reader, per the ST3.20 spec: header, 32 channel-setting bytes, orders, instrument parapointers, pattern pointers.
public static class S3mParser
{
    private const int TicksPerRow = 120;    // 480/4, a 16th-note grid

    public static Song Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 96) throw new FormatException("S3M: file too short");
        string moduleName = TrackerBinary.ReadFixedAscii(data, 0, 28);
        if (data[28] != 0x1A) throw new FormatException("S3M: missing 0x1A type byte");
        int fileType = data[29];
        _ = fileType;
        int numOrders = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(32, 2));
        int numInstruments = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(34, 2));
        int numPatterns = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(36, 2));
        int cwt = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(40, 2));
        int ffv = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(42, 2));
        if (data[44..48].SequenceEqual(Encoding.ASCII.GetBytes("SCRM")) == false)
            throw new FormatException("S3M: missing SCRM signature");
        int initialSpeed = data[49];
        int initialTempo = data[50];
        _ = ffv;

        double bpm = initialTempo is > 0 and <= 255 ? initialTempo : 125;
        int speed = initialSpeed is > 0 and <= 255 ? initialSpeed : 6;

        var channels = new bool[32];
        for (int i = 0; i < 32; i++)
        {
            byte c = data[64 + i];
            channels[i] = c != 0xFF && (c & 0x80) == 0;   // 255 = unused, bit7 = disabled
        }
        int activeChannels = 0;
        for (int i = 0; i < 32; i++) if (channels[i]) activeChannels++;

        var song = new Song { SourceFormat = $"S3M (ST3 {cwt >> 12}.{cwt & 0xFFF:X2}, {activeChannels} ch)", TicksPerQuarter = 480 };
        song.Tempos.Add(new TempoEvent(0, bpm));
        song.TimeSignatures.Add(new TimeSignatureEvent(0, 4, 4));

        // orders at 96; instrument parapointers next; then pattern parapointers
        int ordersOffset = 96;
        int instPtrOffset = ordersOffset + numOrders;
        int patPtrOffset = instPtrOffset + numInstruments * 2;

        var patternPointers = new int[numPatterns];
        for (int i = 0; i < numPatterns; i++)
            patternPointers[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(patPtrOffset + i * 2, 2)) * 16;

        // instrument names for lane labelling (parapointer * 16 -> instrument header)
        var instNames = new string?[numInstruments];
        for (int i = 0; i < numInstruments; i++)
        {
            int para = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(instPtrOffset + i * 2, 2)) * 16;
            if (para + 76 <= data.Length)
                instNames[i] = TrackerBinary.ReadFixedAscii(data, para + 48, 28);
        }

        var open = new (long start, int pitch)?[32];
        var laneInstruments = new int[32];
        var lanes = new Lane[32];

        for (int p = 0; p < numPatterns; p++)
        {
            int pp = patternPointers[p];
            if (pp <= 0 || pp + 2 > data.Length) continue;
            int packedLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pp, 2));
            ReadOnlySpan<byte> body = data.Slice(pp + 2, Math.Min(packedLen, data.Length - pp - 2));

            int row = 0;
            int pos = 0;
            while (row < 64 && pos < body.Length)
            {
                byte first = body[pos++];
                if (first == 0) { row++; continue; }   // end of row marker

                int channel = first & 0x1F;
                if (channel >= 32 || !channels[channel]) { SkipEntry(body, ref pos, first); continue; }

                int note = -1, instrument = -1, volume = -1;
                if ((first & 0x20) != 0) note = body[pos++];
                if ((first & 0x40) != 0) instrument = body[pos++];
                if ((first & 0x80) != 0) volume = body[pos++];
                // effects: up to 3 (type,value) pairs
                SkipEffects(body, ref pos, first);

                if (!channels[channel]) continue;

                long tick = (long)p * 64 * TicksPerRow + row * TicksPerRow;

                if (note == 254)   // key off
                {
                    CloseNote(lanes, channel, open, tick);
                }
                else if (note is > 0 and < 254)
                {
                    CloseNote(lanes, channel, open, tick);
                    open[channel] = (tick, note + 12);
                }
                if (instrument > 0) laneInstruments[channel] = instrument - 1;
                _ = volume;
            }
        }

        // build lanes
        for (int ch = 0; ch < 32; ch++)
        {
            if (!channels[ch]) continue;
            if (open[ch] != null)
            {
                // dangling note
                var lane = LaneFor(lanes, ch);
                lane.Notes.Add(new Note(open[ch]!.Value.start, TicksPerRow, open[ch]!.Value.pitch));
                open[ch] = null;
            }
            var finalLane = LaneFor(lanes, ch);
            if (finalLane.Notes.Count == 0) continue;
            int inst = laneInstruments[ch];
            finalLane.Name = inst >= 0 && inst < instNames.Length && !string.IsNullOrWhiteSpace(instNames[inst])
                ? instNames[inst]!
                : $"Ch {ch + 1}";
            finalLane.Program = -1;    // sample instruments, not GM programs
            finalLane.Notes.RemoveAll(n => n.DurationTicks <= 0);
            song.AddLane(finalLane);
        }

        return song;
    }

    private static Lane LaneFor(Lane[] lanes, int ch) =>
        lanes[ch] ??= new Lane { Name = $"Ch {ch + 1}", Channel = ch };

    private static void CloseNote(Lane[] lanes, int ch, (long start, int pitch)?[] open, long tick)
    {
        if (open[ch] == null) return;
        var (start, pitch) = open[ch]!.Value;
        LaneFor(lanes, ch).Notes.Add(new Note(start, Math.Max(1, tick - start), pitch));
        open[ch] = null;
    }

    private static void SkipEntry(ReadOnlySpan<byte> body, ref int pos, int first)
    {
        if ((first & 0x20) != 0) pos++;
        if ((first & 0x40) != 0) pos++;
        if ((first & 0x80) != 0) pos++;
        SkipEffects(body, ref pos, first);
    }

    private static void SkipEffects(ReadOnlySpan<byte> body, ref int pos, int first)
    {
        int count = (first & 0x1F) switch
        {
            0x10 => 1, 0xA0 => 1, 0x50 => 1, 0x30 => 2, 0xB0 => 2, 0x70 => 2, 0xF0 => 2, 0x90 => 3,
            0xC0 => 3, 0xD0 => 3, 0xE0 => 3, 0x60 => 4, _ => 0,
        };
        pos += count * 2;
        _ = body;
    }

    /// Sample index -> rough GM program.
    private static int EstimateProgram(int instrumentIndex)
    {
        if (instrumentIndex < 0) return 0;
        // Early instrument slots in most S3Ms are melodic; later ones are often drums.
        return instrumentIndex < 60 ? 0 : 0;
    }
}
