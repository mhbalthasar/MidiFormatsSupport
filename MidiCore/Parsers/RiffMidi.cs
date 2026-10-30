using System.Buffers.Binary;
using System.Text;

namespace MidiCore.Parsers;

/// RIFF container support.
public static class RiffMidi
{
    public static bool IsRiff(ReadOnlySpan<byte> data) => data.Length >= 12 && data[..4].SequenceEqual(Encoding.ASCII.GetBytes("RIFF"));

    /// Returns the embedded SMF bytes, or null when not a RIFF/RMID/XMF with a data chunk.
    public static ReadOnlySpan<byte> ExtractSmf(ReadOnlySpan<byte> data)
    {
        if (!IsRiff(data)) return ReadOnlySpan<byte>.Empty;
        // RIFF <size> <form-type "RMID" | "XMF"> then chunks
        string form = Encoding.ASCII.GetString(data.Slice(8, 4));
        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            string id = Encoding.ASCII.GetString(data.Slice(pos, 4));
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 4, 4));
            int body = pos + 8;
            if (id == "data" && body + 4 <= data.Length)
            {
                // RMID: data chunk begins with "MThd" already. XMF: same.
                var inner = data.Slice(body, size);
                if (inner.Length >= 4 && inner[..4].SequenceEqual(Encoding.ASCII.GetBytes("MThd")))
                    return inner;
            }
            pos = body + size + (size & 1);   // chunks are word-aligned
        }
        return ReadOnlySpan<byte>.Empty;
    }

    /// Wraps an SMF into an RIFF/RMID (.rmi) container.
    public static byte[] WrapRmid(ReadOnlySpan<byte> smf)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        int riffSize = 4 /*"RMID"*/ + 8 /*"data"+size*/ + smf.Length;
        w.Write(BinaryPrimitives.ReverseEndianness(riffSize));
        w.Write(Encoding.ASCII.GetBytes("RMID"));
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(BinaryPrimitives.ReverseEndianness((int)smf.Length));
        w.Write(smf);
        w.Flush();
        return ms.ToArray();
    }
}
