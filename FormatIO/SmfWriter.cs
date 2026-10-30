using System.Buffers.Binary;
using System.Text;
using TuneLab.SDK;
using TuneLab.Foundation;

namespace MidiFormatsSupport;

/// Writes a Standard MIDI File (format 1) from a TuneLab ProjectInfo: track 0 = conductor (tempo/time signature), one track per ProjectInfo track.
internal static class SmfWriter
{
    public static byte[] Write(ProjectInfo info, bool includeLyrics)
    {
        var tracks = new List<byte[]> { BuildConductor(info) };
        foreach (var track in info.Tracks)
            tracks.Add(BuildNoteTrack(track, includeLyrics));

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("MThd"));
        WriteBe32(w, 6);
        WriteBe16(w, 1);                       // format 1
        WriteBe16(w, (ushort)tracks.Count);
        WriteBe16(w, 480);                      // PPQ = 480 (same as TuneLab's)
        foreach (var t in tracks)
        {
            w.Write(Encoding.ASCII.GetBytes("MTrk"));
            WriteBe32(w, (uint)t.Length);
            w.Write(t);
        }
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildConductor(ProjectInfo info)
    {
        var events = new List<(long tick, int order, byte[] payload)>();

        foreach (var tempo in info.Tempos.OrderBy(t => t.Pos))
        {
            int usPerQuarter = (int)Math.Round(60_000_000.0 / Math.Clamp(tempo.Bpm, 1, 1000));
            byte[] meta = Meta(0x51, new byte[]
            {
                (byte)((usPerQuarter >> 16) & 0xFF), (byte)((usPerQuarter >> 8) & 0xFF), (byte)(usPerQuarter & 0xFF),
            });
            events.Add(((long)Math.Max(0, tempo.Pos), 1, meta));
        }

        foreach (var ts in info.TimeSignatures.OrderBy(t => t.BarIndex))
        {
            long tick = (long)Math.Max(0, ts.BarIndex) * 4 * 480 * Math.Clamp(ts.Numerator, 1, 64)
                        / Math.Clamp(ts.Denominator, 1, 64);
            int dd = (int)Math.Round(Math.Log2(Math.Clamp(ts.Denominator, 1, 64)));
            byte[] meta = Meta(0x58, new byte[] { (byte)Math.Clamp(ts.Numerator, 1, 64), (byte)dd, 24, 8 });
            events.Add((tick, 1, meta));
        }

        return Assemble(events);
    }

    private static byte[] BuildNoteTrack(TrackInfo track, bool includeLyrics)
    {
        var events = new List<(long tick, int order, byte[] payload)>();
        if (!string.IsNullOrEmpty(track.Name))
            events.Add((0, 1, Meta(0x03, Encoding.UTF8.GetBytes(track.Name))));

        foreach (var part in track.Parts)
        {
            if (part is not MidiPartInfo midi) continue;

            // Restore the GM/GS identity from the part name ("<instrument> [bank_program]") as real Bank Select + Program Change events, so a re-import round-trips the instrument instead of everything collapsing onto the piano default.
            long baseTick = (long)Math.Round(midi.Pos);
            long firstNoteTick = long.MaxValue;
            foreach (var n in midi.Notes)
                firstNoteTick = Math.Min(firstNoteTick, baseTick + (long)Math.Round(n.Pos));
            if (firstNoteTick == long.MaxValue) firstNoteTick = Math.Max(0, baseTick);

            if (TryReadBankProgram(part.Name, out int bank, out int program))
            {
                if (bank is > 0 and < 16384)
                {
                    events.Add((firstNoteTick, 1, new byte[] { 0xB0, 0x00, (byte)((bank >> 7) & 0x7F) }));   // Bank Select MSB
                    events.Add((firstNoteTick, 1, new byte[] { 0xB0, 0x20, (byte)(bank & 0x7F) }));        // Bank Select LSB
                }
                events.Add((firstNoteTick, 1, new byte[] { 0xC0, (byte)Math.Clamp(program, 0, 127) }));     // Program Change
            }

            foreach (var note in midi.Notes)
            {
                long on = baseTick + (long)Math.Round(note.Pos);
                long off = on + (long)Math.Max(1, Math.Round(note.Dur));
                if (includeLyrics && !string.IsNullOrEmpty(note.Lyric))
                    events.Add((on, 1, Meta(0x05, Encoding.UTF8.GetBytes(note.Lyric))));
                byte pitch = (byte)Math.Clamp(note.Pitch, 0, 127);
                events.Add((on, 2, new byte[] { 0x90, pitch, 100 }));
                events.Add((off, 0, new byte[] { 0x80, pitch, 0 }));
            }
        }
        return Assemble(events);
    }

    /// Reads the "[bank_program]" tag this plugin writes into part names.
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
    private static byte[] Meta(byte type, byte[] data)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)0xFF);
        w.Write(type);
        WriteVlq(w, data.Length);
        w.Write(data);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] Assemble(List<(long tick, int order, byte[] payload)> events)
    {
        var sorted = events.OrderBy(e => e.tick).ThenBy(e => e.order).ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        long last = 0;
        foreach (var e in sorted)
        {
            WriteVlq(w, (int)(e.tick - last));
            w.Write(e.payload);
            last = e.tick;
        }
        WriteVlq(w, 0);
        w.Write(new byte[] { 0xFF, 0x2F, 0x00 });   // End of track
        w.Flush();
        return ms.ToArray();
    }

    private static void WriteVlq(BinaryWriter w, int value)
    {
        if (value < 0) value = 0;
        var buffer = new Stack<byte>();
        buffer.Push((byte)(value & 0x7F));
        value >>= 7;
        while (value > 0) { buffer.Push((byte)((value & 0x7F) | 0x80)); value >>= 7; }
        foreach (var b in buffer) w.Write(b);
    }

    private static void WriteBe32(BinaryWriter w, uint v)
    {
        w.Write((byte)((v >> 24) & 0xFF));
        w.Write((byte)((v >> 16) & 0xFF));
        w.Write((byte)((v >> 8) & 0xFF));
        w.Write((byte)(v & 0xFF));
    }

    private static void WriteBe16(BinaryWriter w, ushort v)
    {
        w.Write((byte)((v >> 8) & 0xFF));
        w.Write((byte)(v & 0xFF));
    }
}
