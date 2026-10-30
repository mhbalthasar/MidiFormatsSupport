using System.Buffers.Binary;
using System.Text;

namespace MidiCore.Parsers;

/// ProTracker / Noisetracker (.mod) reader.
public static class ModParser
{
    public static Song Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1084) throw new FormatException("MOD: file too short");

        string magic = Encoding.ASCII.GetString(data.Slice(1080, 4));
        int channels = magic switch
        {
            "4CHN" or "CD81" => 4,
            "6CHN" => 6,
            "8CHN" or "OKTA" or "FLT4" => 8,
            "16CN" => 16,
            "32CN" => 32,
            "M.K." or "M!K!" or "M&K!" or "N.T." or "FLT8" => 4,
            _ => 0,
        };
        if (channels == 0)
            throw new FormatException("MOD: unknown signature (expected M.K./4CHN/6CHN/8CHN/...)");

        // 20 name + 31*(30 sample header) + 1 songLength + 1 restart + 128 order = 1080
        int songLength = Math.Clamp((int)data[950], 1, 128);
        const int patternTableOffset = 952;

        const int sampleHeaderSize = 30;
        const int sampleCount = 31;
        int patternDataStart = 1084;
        for (int i = 0; i < sampleCount; i++)
        {
            int off = 20 + i * sampleHeaderSize + 22;   // big-endian length word
            int len = ((data[off] << 8) | data[off + 1]) * 2;   // MOD words are big-endian
            patternDataStart += len;
        }

        var sampleNames = new string[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            sampleNames[i] = TrackerBinary.ReadFixedAscii(data, 20 + i * sampleHeaderSize, 22);

        var song = new Song { SourceFormat = $"MOD ({magic}, {channels} ch)", TicksPerQuarter = 480 };

        const int rows = 64;
        const long ticksPerRow = 120;              // 4 rows per quarter note
        const int cellSize = 4;

        int maxPattern = 0;
        for (int i = 0; i < songLength; i++)
            maxPattern = Math.Max(maxPattern, data[patternTableOffset + i]);
        int bytesPerPattern = rows * channels * cellSize;
        int patternsInFile = Math.Max(0, (data.Length - patternDataStart) / bytesPerPattern);
        maxPattern = Math.Min(maxPattern, patternsInFile - 1);

        var lanes = new Lane[channels];
        for (int ch = 0; ch < channels; ch++)
        {
            lanes[ch] = new Lane { Name = $"Channel {ch + 1}", Channel = ch, Program = -1, IsDrum = false };
            song.AddLane(lanes[ch]);
        }

        var openStart = new long[channels];
        var openPitch = new int[channels];
        var openSample = new int[channels];

        for (int pat = 0; pat <= maxPattern; pat++)
        {
            int patternOffset = patternDataStart + pat * bytesPerPattern;
            if (patternOffset + bytesPerPattern > data.Length) break;
            ReadOnlySpan<byte> pattern = data.Slice(patternOffset, bytesPerPattern);

            for (int row = 0; row < rows; row++)
            {
                long tick = (long)pat * rows * ticksPerRow + row * ticksPerRow;
                for (int ch = 0; ch < channels; ch++)
                {
                    ReadOnlySpan<byte> c = pattern.Slice((row * channels + ch) * cellSize, cellSize);
                    int period = ((c[0] & 0x0F) << 8) | c[1];
                    int sample = (c[0] & 0xF0) | ((c[2] & 0xF0) >> 4);
                    int effect = c[2] & 0x0F;
                    _ = effect;

                    if (period > 0)
                    {
                        // close previous note on this channel
                        if (openPitch[ch] > 0)
                            lanes[ch].Notes.Add(new Note(openStart[ch], Math.Max(1, tick - openStart[ch]), openPitch[ch]));

                        int key = TrackerBinary.PeriodToPianoKey(period);
                        openStart[ch] = tick;
                        openPitch[ch] = Math.Clamp(key + 12, 0, 127);
                        openSample[ch] = sample > 0 && sample <= sampleCount ? sample - 1 : -1;
                    }
                }
            }
        }

        for (int ch = 0; ch < channels; ch++)
        {
            if (openPitch[ch] > 0)
                lanes[ch].Notes.Add(new Note(openStart[ch], ticksPerRow, openPitch[ch]));

            // name the lane after the sample that carried most of its notes
            if (openSample[ch] >= 0)
            {
                var counts = new Dictionary<int, int>();
                lanes[ch].Notes.RemoveAll(n => n.DurationTicks <= 0);
                if (lanes[ch].Notes.Count > 0)
                {
                    string sampleName = sampleNames[openSample[ch]].Trim();
                    if (!string.IsNullOrEmpty(sampleName))
                        lanes[ch].Name = sampleName;
                }
            }

            if (lanes[ch].Notes.Count == 0)
            {
                song.Lanes.Remove(lanes[ch]);
            }
        }

        if (!song.Tempos.Exists(t => t.Tick == 0))
            song.Tempos.Add(new TempoEvent(0, 125));
        return song;
    }
}
