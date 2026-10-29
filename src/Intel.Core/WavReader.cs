using System.Buffers.Binary;
using System.Text;

namespace Intel.Core;
public sealed record WavFormat(int SampleRate, int Channels) { public int BytesPerFrame => Channels * 2; }
public sealed class WavReader {
    private readonly List<byte> buffer = [];
    private bool samples;
    private long? left;
    public WavFormat? Format { get; private set; }
    public byte[] Feed(ReadOnlySpan<byte> bytes) {
        buffer.AddRange(bytes.ToArray());
        if (!samples) {
            if (buffer.Count > 1024 * 1024) throw new InvalidDataException("WAV header exceeds 1 MiB.");
            if (buffer.Count < 12) return [];
            var raw = buffer.ToArray();
            if (Tag(raw, 0) != "RIFF" || Tag(raw, 8) != "WAVE") throw new InvalidDataException("The daemon did not send WAV audio.");
            var offset = 12;
            WavFormat? format = null;
            while (raw.Length >= offset + 8) {
                var tag = Tag(raw, offset);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(offset + 4));
                if (tag == "data") {
                    if (format == null) throw new InvalidDataException("WAV has no format before its samples.");
                    Format = format; samples = true; left = size is 0 or uint.MaxValue ? null : size;
                    if (left is { } length && length % format.BytesPerFrame != 0) throw new InvalidDataException("WAV sample length is not frame aligned.");
                    buffer.RemoveRange(0, offset + 8); break;
                }
                if (size > 1024 * 1024) throw new InvalidDataException("WAV header chunk exceeds 1 MiB.");
                var padded = checked((int)size + ((int)size & 1));
                if (raw.Length < offset + 8 + padded) return [];
                if (tag == "fmt ") {
                    if (size < 16) throw new InvalidDataException("Truncated WAV format.");
                    var f = raw.AsSpan(offset + 8);
                    int channels = BinaryPrimitives.ReadUInt16LittleEndian(f[2..]);
                    var rate = BinaryPrimitives.ReadUInt32LittleEndian(f[4..]);
                    if (BinaryPrimitives.ReadUInt16LittleEndian(f) != 1 || BinaryPrimitives.ReadUInt16LittleEndian(f[14..]) != 16 || channels is < 1 or > 2 || rate is < 8000 or > 192000 || BinaryPrimitives.ReadUInt16LittleEndian(f[12..]) != channels * 2)
                        throw new InvalidDataException("Only mono/stereo PCM16 WAV at 8–192 kHz is supported.");
                    format = new WavFormat((int)rate, channels);
                }
                offset += 8 + padded;
            }
        }
        if (!samples) return [];
        var count = (int)Math.Min(buffer.Count, left ?? buffer.Count);
        count -= count % Format!.BytesPerFrame;
        var result = buffer.GetRange(0, count).ToArray();
        buffer.RemoveRange(0, count);
        if (left != null) left -= count;
        return result;
    }
    public void Complete() {
        if (!samples || left > 0 || (left == null && buffer.Count != 0)) throw new InvalidDataException("The audio stream ended with a truncated WAV header or sample frame.");
    }
    private static string Tag(byte[] bytes, int offset) => Encoding.ASCII.GetString(bytes, offset, 4);
}
