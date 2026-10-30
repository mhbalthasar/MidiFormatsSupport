namespace MidiCore.Parsers;

/// Shared helper for the binary tracker formats.
internal static class TrackerBinary
{
    public static ushort ReadU16(ReadOnlySpan<byte> d, int off) => (ushort)((d[off] | (d[off + 1] << 8)) & 0xFFFF);
    public static uint ReadU32(ReadOnlySpan<byte> d, int off) =>
        (uint)((uint)d[off] | ((uint)d[off + 1] << 8) | ((uint)d[off + 2] << 16) | ((uint)d[off + 3] << 24));
    public static short ReadS16(ReadOnlySpan<byte> d, int off) => (short)(d[off] | (d[off + 1] << 8));
    public static int ReadS32(ReadOnlySpan<byte> d, int off) =>
        (int)((uint)d[off] | ((uint)d[off + 1] << 8) | ((uint)d[off + 2] << 16) | ((uint)d[off + 3] << 24));

    public static string ReadFixedAscii(ReadOnlySpan<byte> d, int off, int len)
    {
        int end = off + len;
        int stop = off;
        while (stop < end && d[stop] != 0) stop++;
        return System.Text.Encoding.ASCII.GetString(d.Slice(off, stop - off));
    }

    /// Amiga period -> semitone offset from C.
    public static int PeriodToPianoKey(int period)
    {
        if (period <= 0) return 0;
        // p = a * 2^(k/12); invert with the standard Amiga table head
        int[] table = { 1712, 1616, 1524, 1440, 1356, 1280, 1208, 1140, 1076, 1016, 960, 906 };
        for (int octave = 0; octave < 6; octave++)
            for (int i = 0; i < 12; i++)
            {
                int target = table[i] >> octave;
                if (period == target)
                    return octave * 12 + i;
                // fall through: some modules use slightly detuned periods; nearest wins
            }
        // nearest-period fallback
        int best = 0xFFFF, bestKey = 0;
        for (int octave = 0; octave < 6; octave++)
            for (int i = 0; i < 12; i++)
            {
                int target = table[i] >> octave;
                int diff = Math.Abs(period - target);
                if (diff < best) { best = diff; bestKey = octave * 12 + i; }
            }
        return bestKey;
    }
}
