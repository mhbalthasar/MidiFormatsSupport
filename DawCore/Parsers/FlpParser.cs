using System.Text;
using MidiCore;

namespace DawCore.Parsers;

/// Reads an FL Studio project (.flp) into a Song.
/// FLP is a little-endian event stream grouped as channels (instruments) and patterns;
/// positions and lengths are in the project's PPQ (1 beat = ppq), and a zero-length note
/// holds until the next note in the same channel.
public static class FlpParser
{
    private sealed class Channel
    {
        public string Name = "";
        public string? Generator;
    }

    private sealed class Pattern
    {
        public string Name = "";
        // channel index -> its notes (absolute-in-pattern ticks)
        public readonly Dictionary<int, List<(long Pos, long Len, int Key)>> Notes = new();
    }

    private sealed class PatternClip
    {
        public long Pos;
        public long Len;
        public int Pattern;   // 0-based index
        public bool Muted;
    }

    public static Song Parse(byte[] data)
    {
        if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "FLhd")
            throw new FormatException("not an FL Studio project (missing FLhd)");

        using var ms = new MemoryStream(data, writable: false);
        using var r = new BinaryReader(ms);

        r.ReadBytes(4);
        if (r.ReadInt32() != 6) throw new FormatException("unsupported FLP header length");
        if (r.ReadInt16() != 0) throw new FormatException("unsupported FLP file type");
        int channelCount = r.ReadInt16();
        if (channelCount is < 1 or > 4096) throw new FormatException($"invalid FLP channel count {channelCount}");
        int ppq = r.ReadInt16();
        if (ppq <= 0) ppq = 96;

        // walk chunks until the FLdt event payload
        long dataLen = -1;
        while (ms.Position + 8 <= data.Length)
        {
            string id = Encoding.ASCII.GetString(r.ReadBytes(4));
            int len = r.ReadInt32();
            if (len < 0 || len > 0x10000000) throw new FormatException("invalid FLP chunk length");
            if (id == "FLdt") { dataLen = len; break; }
            ms.Position += len;
        }
        if (dataLen < 0) throw new FormatException("FLP has no FLdt event chunk");

        var song = new Song { SourceFormat = "FL Studio project", TicksPerQuarter = DawXml.Ppq };
        double tempo = 140;

        var channels = new List<Channel>();
        var patterns = new List<Pattern>();
        var clips = new List<PatternClip>();

        int curChannel = -1;
        int curPattern = -1;
        int versionMajor = 0;
        int timeSigNum = 4, timeSigBeat = 4;

        long end = ms.Position + dataLen;
        while (ms.Position < end)
        {
            int id = r.ReadByte();

            if (id < 64)                      // byte event
            {
                int d = r.ReadByte();
                if (id == 30) { /* play truncated notes flag */ }
            }
            else if (id < 128)                // word event
            {
                int d = r.ReadUInt16();
                switch (id)
                {
                    case 64: curChannel = d; Ensure(channels, curChannel); break;
                    case 65: curPattern = d - 1; Ensure(patterns, curPattern); break;
                    case 66: tempo = d; break;   // coarse tempo (BPM)
                    case 81: timeSigNum = d; break;   // ArrangementID TimeSigNum
                    case 82: timeSigBeat = d; break;  // ArrangementID TimeSigBeat
                }
            }
            else if (id < 192)                // dword event
            {
                uint d = r.ReadUInt32();
                if (id == 156) tempo = d / 1000.0;   // fine tempo (BPM x1000)
            }
            else
            {
                int n = ReadVarLen(r);
                var buf = r.ReadBytes(n);
                if (id < 208) HandleText(id, buf, channels, patterns, ref curChannel, ref curPattern, ref versionMajor);
                else HandleData(id, buf, patterns, clips, curPattern);
            }
        }

        if (tempo is <= 0 or > 1000) tempo = 140;
        song.Tempos.Add(new TempoEvent(0, tempo));
        song.TimeSignatures.Add(new TimeSignatureEvent(0, Math.Clamp(timeSigNum, 1, 64), Math.Clamp(timeSigBeat, 1, 64)));

        Build(song, channels, patterns, clips, ppq, ref versionMajor);
        return song;
    }

    private static void Build(Song song, List<Channel> channels, List<Pattern> patterns,
                              List<PatternClip> clips, int ppq, ref int versionMajor)
    {
        double toTicks = (double)DawXml.Ppq / ppq;
        var lanes = new Lane[channels.Count];
        for (int i = 0; i < channels.Count; i++)
        {
            string name = channels[i].Name;
            string? gen = channels[i].Generator;
            if (string.IsNullOrWhiteSpace(name)) name = gen ?? $"Channel {i + 1}";
            var lane = DawXml.NewLane(name, gen);
            lane.InstrumentNameHint = !GmInstruments.LooksGeneric(name) ? name
                                    : gen is { Length: > 0 } && !GmInstruments.LooksGeneric(gen) ? gen
                                    : null;
            lanes[i] = lane;
        }

        var live = clips.Where(c => !c.Muted && c.Pattern >= 0 && c.Pattern < patterns.Count).ToList();
        if (live.Count > 0)
        {
            foreach (var clip in live)
            {
                var pat = patterns[clip.Pattern];
                foreach (var (ch, notes) in pat.Notes)
                {
                    if (ch < 0 || ch >= lanes.Length) continue;
                    var lane = lanes[ch];
                    long clipStart = clip.Pos;
                    long clipEnd = clip.Len > 0 ? clip.Pos + clip.Len : long.MaxValue;
                    AddOrdered(lane, notes, clipStart, clipEnd, toTicks, ppq);
                }
            }
        }
        else
        {
            // pattern-mode project: lay each pattern out in order so the material survives
            long cursor = 0;
            foreach (var pat in patterns)
            {
                long span = Math.Max(ppq * 4L, pat.Notes.Values.SelectMany(v => v).DefaultIfEmpty().Max(n => n.Pos + Math.Max(n.Len, 0)));
                foreach (var (ch, notes) in pat.Notes)
                {
                    if (ch < 0 || ch >= lanes.Length) continue;
                    AddOrdered(lanes[ch], notes, cursor, cursor + span, toTicks, ppq);
                }
                cursor += span;
            }
        }

        foreach (var lane in lanes)
        {
            if (lane.Notes.Count == 0) continue;
            lane.Notes.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
            song.AddLane(lane);
        }
    }

    /// Places a pattern's notes with `offset` and resolves zero-length (legato) notes.
    private static void AddOrdered(Lane lane, List<(long Pos, long Len, int Key)> notes,
                                   long offset, long end, double toTicks, int ppq)
    {
        var ordered = notes.OrderBy(n => n.Pos).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var (pos, len, key) = ordered[i];
            if (key is < 0 or > 127) continue;

            if (len <= 0)
                len = (i + 1 < ordered.Count ? ordered[i + 1].Pos : pos + ppq / 4) - pos;
            if (len <= 0) len = Math.Max(1, ppq / 4);

            long start = offset + pos;
            long stop = start + len;
            if (end != long.MaxValue)
            {
                if (start >= end) continue;
                stop = Math.Min(stop, end);
            }
            long dur = stop - start;
            if (dur <= 0) continue;
            DawXml.AddNote(lane, (long)Math.Round(start * toTicks), (long)Math.Round(dur * toTicks), key);
        }
    }

    private static void HandleText(int id, byte[] buf, List<Channel> channels, List<Pattern> patterns,
                                   ref int curChannel, ref int curPattern, ref int versionMajor)
    {
        if (id == 199)   // TextVersion is always ASCII
        {
            string v = Encoding.ASCII.GetString(buf).TrimEnd('\0');
            int dot = v.IndexOf('.');
            if (dot > 0 && int.TryParse(v[..dot], out var major)) versionMajor = major;
            return;
        }

        string text = DecodeText(buf, versionMajor);
        switch (id)
        {
            case 192: if (Ch(channels, curChannel) is { } c) c.Name = text; break;          // channel name
            case 193: if (Pat(patterns, curPattern) is { } p) p.Name = text; break;          // pattern name
            case 201: if (Ch(channels, curChannel) is { } g) g.Generator = text; break;       // generator/plugin name
        }
    }

    private static void HandleData(int id, byte[] buf, List<Pattern> patterns,
                                   List<PatternClip> clips, int curPattern)
    {
        switch (id)
        {
            case 224: AddPatternNotes(buf, patterns, curPattern); break;
            case 233: AddPlaylistItems(buf, clips); break;
        }
    }

    /// FL pattern notes: 24 bytes each in modern files (20 in very old ones); the fields we need
    /// (position o, channel o+6, length o+8, key o+12) sit at the same offsets in both.
    private static void AddPatternNotes(byte[] buf, List<Pattern> patterns, int curPattern)
    {
        if (Pat(patterns, curPattern) is not { } pat) return;
        int size = buf.Length % 24 == 0 ? 24 : buf.Length % 20 == 0 ? 20 : 24;
        for (int o = 0; o + size <= buf.Length; o += size)
        {
            long pos = BitConverter.ToInt32(buf, o);
            int ch = buf[o + 6];
            long len = BitConverter.ToInt32(buf, o + 8);
            int key = buf[o + 12];
            if (!pat.Notes.TryGetValue(ch, out var list)) pat.Notes[ch] = list = new();
            list.Add((pos, len, key));
        }
    }

    /// Playlist/pattern blocks: 60-byte records on FL 21+, 32 bytes on FL 12-20.
    private static void AddPlaylistItems(byte[] buf, List<PatternClip> clips)
    {
        int stride = buf.Length % 60 == 0 ? 60 : buf.Length % 32 == 0 ? 32 : buf.Length % 28 == 0 ? 28 : 32;
        for (int o = 0; o + stride <= buf.Length; o += stride)
        {
            long pos = BitConverter.ToUInt32(buf, o);
            int patternBase = BitConverter.ToUInt16(buf, o + 4);
            int itemIndex = BitConverter.ToUInt16(buf, o + 6);
            long len = BitConverter.ToUInt32(buf, o + 8);
            int flags = BitConverter.ToUInt16(buf, o + 18);
            bool muted = (flags & 0x2000) != 0;

            if (itemIndex <= patternBase) continue;   // audio/automation clip, not a pattern
            clips.Add(new PatternClip { Pos = pos, Len = len, Pattern = itemIndex - patternBase - 1, Muted = muted });
        }
    }

    private static Channel? Ch(List<Channel> list, int i) => i >= 0 && i < list.Count ? list[i] : null;
    private static Pattern? Pat(List<Pattern> list, int i) => i >= 0 && i < list.Count ? list[i] : null;

    private static void Ensure(List<Channel> list, int i)
    {
        while (list.Count <= i) list.Add(new Channel());
    }

    private static void Ensure(List<Pattern> list, int i)
    {
        while (list.Count <= i) list.Add(new Pattern());
    }

    private static int ReadVarLen(BinaryReader r)
    {
        int value = 0, shift = 0, b;
        do
        {
            b = r.ReadByte();
            value |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return value;
    }

    /// FL text is UTF-16LE in FL 11+ and single-byte ASCII before that; when the version event
    /// hasn't been seen yet, fall back to inspecting the byte pattern.
    private static string DecodeText(byte[] buf, int versionMajor)
    {
        bool utf16 = versionMajor >= 11
            || (versionMajor == 0 && buf.Length >= 4 && buf.Length % 2 == 0 && ZerosAtOdd(buf) >= Math.Max(2, buf.Length / 4));
        return utf16 ? Encoding.Unicode.GetString(buf).TrimEnd('\0')
                     : Encoding.ASCII.GetString(buf).TrimEnd('\0');
    }

    private static int ZerosAtOdd(byte[] buf)
    {
        int zeros = 0;
        for (int i = 1; i < buf.Length; i += 2) if (buf[i] == 0) zeros++;
        return zeros;
    }
}
