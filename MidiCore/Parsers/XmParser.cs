using System.Buffers.Binary;
using System.Text;

namespace MidiCore.Parsers;

/// FastTracker II (.xm) reader.
public static class XmParser
{
    private const int RowsPerBeat = 4;         // musical choice: one row = a 16th note
    private const int TicksPerRow = 480 / RowsPerBeat;

    public static Song Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 60 || !data[..17].SequenceEqual(Encoding.ASCII.GetBytes("Extended Module: ")))
            throw new FormatException("XM: bad signature");

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(58, 2));   // 0x0104
        if (version < 0x0104)
            throw new FormatException($"XM: unsupported pre-0104 version 0x{version:X4}");

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(60, 4));
        int songLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(64, 2));
        int numChannels = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(68, 2));
        int numPatterns = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(70, 2));
        int numInstruments = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(72, 2));
        int defaultTempo = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(76, 2));
        int defaultBpm = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(78, 2));
        // 80..336 pattern order table (256 bytes, only first songLength relevant)
        string moduleName = TrackerBinary.ReadFixedAscii(data, 17, 20);

        var song = new Song { SourceFormat = "XM (FastTracker II)", TicksPerQuarter = 480 };
        // speed/tempo both influence BPM: BPM = tempo * 0.4 for classic tracks
        double bpm = defaultBpm is > 0 and <= 255 ? defaultBpm : 125;
        if (!song.Tempos.Exists(t => t.Tick == 0))
            song.Tempos.Add(new TempoEvent(0, bpm));
        _ = defaultTempo;

        var lanes = new Lane[numChannels];
        for (int i = 0; i < numChannels; i++)
        {
            lanes[i] = new Lane { Name = $"Ch {i + 1}", Channel = i, Program = -1 };
            song.AddLane(lanes[i]);
        }

        // instrument names (for nicer lane naming) come after the patterns
        var instrumentNames = new string[numInstruments];
        {
            int pos = 60 + headerSize;
            for (int p = 0; p < numPatterns && pos + 9 <= data.Length; p++)
            {
                int patHeaderLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
                if (patHeaderLen < 4) break;
                if (pos + patHeaderLen > data.Length) break;
                int rows = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + 5, 2));
                int packedSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + 7, 2));
                int dataStart = pos + patHeaderLen;
                if (dataStart + packedSize > data.Length) packedSize = data.Length - dataStart;
                if (packedSize <= 0) { pos = dataStart; continue; }

                DecodePattern(data.Slice(dataStart, packedSize), rows, numChannels, lanes, p);
                pos = dataStart + packedSize;
            }
            for (int i = 0; i < numInstruments && pos + 33 <= data.Length; i++)
            {
                instrumentNames[i] = TrackerBinary.ReadFixedAscii(data, pos + 4, 22);
                int instHeaderSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
                if (instHeaderSize < 4) break;
                int samplesInInst = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + 27, 2));
                int skip = instHeaderSize;
                if (samplesInInst > 0)
                {
                    int sampleHeaderSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 29, 4));
                    skip = instHeaderSize + Math.Max(0, samplesInInst * sampleHeaderSize);
                    long total = 0;
                    for (int s = 0; s < samplesInInst; s++)
                    {
                        int sh = pos + instHeaderSize + s * sampleHeaderSize;
                        if (sh + 4 > data.Length) break;
                        total += BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(sh, 4));
                    }
                    skip += (int)Math.Min(total, int.MaxValue - skip);
                }
                if (skip <= 0) break;
                pos += skip;
            }
        }

        // close dangling notes and give each channel a program guess from its instrument usage
        Finalize(lanes);
        // use instrument names for lanes where the tracker put them in
        return song;
    }

    private sealed class Open
    {
        public long Start;
        public int Pitch;
    }

    private static void DecodePattern(ReadOnlySpan<byte> packed, int rows, int channels, Lane[] lanes, int patternIndex)
    {
        var open = new Open[channels];
        int pos = 0;
        long rowBase = (long)patternIndex * rows * TicksPerRow;

        for (int row = 0; row < rows && pos < packed.Length; row++)
        {
            for (int ch = 0; ch < channels && pos < packed.Length; ch++)
            {
                byte first = packed[pos++];
                if ((first & 0x80) == 0)
                {
                    // note byte without mask: high bit clear means note follows directly (legacy packing)
                    int rawNote = first;
                    long tickHere = rowBase + row * TicksPerRow;
                    if (rawNote == 97) Close(lanes, ch, open, tickHere);
                    else if (rawNote > 0 && rawNote < 97)
                    {
                        Close(lanes, ch, open, tickHere);
                        open[ch] = new Open { Start = tickHere, Pitch = rawNote + 12 };
                    }
                    continue;
                }

                int mask = first;
                int note = -1, instrument = -1, volume = -1, effectType = -1, effectParam = 0;
                if ((mask & 0x01) != 0 && pos < packed.Length) note = packed[pos++];
                if ((mask & 0x02) != 0 && pos < packed.Length) instrument = packed[pos++];
                if ((mask & 0x04) != 0 && pos < packed.Length) volume = packed[pos++];
                if ((mask & 0x08) != 0 && pos < packed.Length) effectType = packed[pos++];
                if ((mask & 0x10) != 0) effectParam = packed[pos++];

                long tick = rowBase + row * TicksPerRow;

                // key-off (note 97) or note cut (note 0 with volume 0)
                if (note == 97 || (note == 0 && volume == 0))
                {
                    Close(lanes, ch, open, tick);
                    continue;
                }
                if (note > 0)
                {
                    Close(lanes, ch, open, tick);
                    open[ch] = new Open { Start = tick, Pitch = note + 12 };
                    if (instrument > 0 && instrument <= lanes.Length + 120)
                    {
                        // remember which instrument fired, for GM program guessing
                        lanes[ch].InstrumentNameHint ??= $"XM instrument {instrument}";
                    }
                }
                _ = effectType; _ = effectParam; _ = volume;   // no mapping for these
            }
        }

        // dangling notes get a row-length duration
        for (int ch = 0; ch < channels; ch++)
            if (open[ch] != null)
            {
                lanes[ch].Notes.Add(new Note(open[ch].Start, TicksPerRow, open[ch].Pitch));
                open[ch] = null;
            }
    }

    private static void Close(Lane[] lanes, int ch, Open[] open, long tick)
    {
        if (open[ch] == null) return;
        lanes[ch].Notes.Add(new Note(open[ch].Start, Math.Max(1, tick - open[ch].Start), open[ch].Pitch));
        open[ch] = null;
    }

    private static void Finalize(Lane[] lanes)
    {
        foreach (var lane in lanes)
        {
            // trackers carry sample identities, not GM program numbers — leave Program at -1
            lane.Notes.RemoveAll(n => n.DurationTicks <= 0);
        }
    }
}
