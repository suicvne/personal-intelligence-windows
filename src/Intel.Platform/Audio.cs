using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using Intel.Core;
namespace Intel.Platform;
public sealed class AudioFactory : IAudioFactory {
    public IAudioOutput Open(WavFormat format) => OperatingSystem.IsWindows() ? new WaveOut(format) : OperatingSystem.IsMacOS() ? new MacAudioQueue(format) : throw new PlatformNotSupportedException("Audio is supported on Windows and macOS.");
}
internal sealed class WaveOut : IAudioOutput {
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat {
        public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort Align, Bits, Extra;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Header {
        public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved;
    }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr device);
    private IntPtr device;
    public WaveOut(WavFormat format) {
        var wave = new WaveFormat { Tag = 1, Channels = (ushort)format.Channels, Rate = (uint)format.SampleRate, BytesPerSecond = (uint)(format.SampleRate * format.BytesPerFrame), Align = (ushort)format.BytesPerFrame, Bits = 16 };
        Check(waveOutOpen(out device, uint.MaxValue, ref wave, IntPtr.Zero, IntPtr.Zero, 0));
    }
    private readonly Queue<(IntPtr Header, IntPtr Data)> pending = new();
    public async Task WriteAsync(byte[] pcm, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        // Keep several buffers queued so network reads and polling cannot create gaps.
        if (pending.Count >= 8) await ReleaseFirstAsync(cancellationToken);
        var data = Marshal.AllocHGlobal(pcm.Length); var header = Marshal.AllocHGlobal(Marshal.SizeOf<Header>());
        var size = (uint)Marshal.SizeOf<Header>(); var prepared = false;
        try {
            Marshal.Copy(pcm, 0, data, pcm.Length);
            Marshal.StructureToPtr(new Header { Data = data, Length = (uint)pcm.Length }, header, false);
            Check(waveOutPrepareHeader(device, header, size)); prepared = true;
            Check(waveOutWrite(device, header, size));
            pending.Enqueue((header, data));
        } catch {
            if (prepared) waveOutUnprepareHeader(device, header, size);
            Marshal.FreeHGlobal(header); Marshal.FreeHGlobal(data); throw;
        }
    }
    private async Task ReleaseFirstAsync(CancellationToken ct) {
        var item = pending.Peek();
        while ((Marshal.PtrToStructure<Header>(item.Header).Flags & 1) == 0) await Task.Delay(10, ct);
        Free(pending.Dequeue());
    }
    private void Free((IntPtr Header, IntPtr Data) item) {
        waveOutUnprepareHeader(device, item.Header, (uint)Marshal.SizeOf<Header>());
        Marshal.FreeHGlobal(item.Header); Marshal.FreeHGlobal(item.Data);
    }
    public async Task DrainAsync(CancellationToken ct) { while (pending.Count > 0) await ReleaseFirstAsync(ct); }
    public void Dispose() {
        if (device == IntPtr.Zero) return;
        waveOutReset(device);
        while (pending.Count > 0) Free(pending.Dequeue());
        waveOutClose(device); device = IntPtr.Zero;
    }
    private static void Check(uint code) { if (code != 0) throw new InvalidOperationException($"Windows waveOut failed ({code})."); }
}
internal sealed class MacAudioQueue : IAudioOutput {
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    [StructLayout(LayoutKind.Sequential)] private struct Format {
        public double Rate; public uint Id, Flags, BytesPerPacket, FramesPerPacket, BytesPerFrame, Channels, Bits, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Buffer {
        public uint Capacity; public IntPtr Data; public uint Size; public IntPtr User;
        public uint PacketCapacity; public IntPtr PacketDescriptions; public uint PacketCount;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Callback(IntPtr user, IntPtr queue, IntPtr buffer);
    [DllImport(AudioToolbox)] private static extern int AudioQueueNewOutput(ref Format format, Callback callback, IntPtr user, IntPtr runLoop, IntPtr runMode, uint flags, out IntPtr queue);
    [DllImport(AudioToolbox)] private static extern int AudioQueueAllocateBuffer(IntPtr queue, uint size, out IntPtr buffer);
    [DllImport(AudioToolbox)] private static extern int AudioQueueEnqueueBuffer(IntPtr queue, IntPtr buffer, uint descriptions, IntPtr description);
    [DllImport(AudioToolbox)] private static extern int AudioQueueStart(IntPtr queue, IntPtr time);
    [DllImport(AudioToolbox)] private static extern int AudioQueueStop(IntPtr queue, [MarshalAs(UnmanagedType.I1)] bool immediate);
    [DllImport(AudioToolbox)] private static extern int AudioQueueFreeBuffer(IntPtr queue, IntPtr buffer);
    [DllImport(AudioToolbox)] private static extern int AudioQueueDispose(IntPtr queue, [MarshalAs(UnmanagedType.I1)] bool immediate);
    private IntPtr queue;
    private readonly Callback callback;
    private readonly ConcurrentDictionary<IntPtr, bool> completed = new();
    private readonly Queue<IntPtr> pending = new();
    private bool started;
    public MacAudioQueue(WavFormat format) {
        callback = (_, _, buffer) => completed[buffer] = true;
        var native = new Format { Rate = format.SampleRate, Id = 0x6c70636d, Flags = 12, BytesPerPacket = (uint)format.BytesPerFrame, FramesPerPacket = 1, BytesPerFrame = (uint)format.BytesPerFrame, Channels = (uint)format.Channels, Bits = 16 };
        Check(AudioQueueNewOutput(ref native, callback, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out queue));
    }
    public async Task WriteAsync(byte[] pcm, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (pending.Count >= 8) await ReleaseFirstAsync(cancellationToken);
        Check(AudioQueueAllocateBuffer(queue, (uint)pcm.Length, out var buffer));
        var queued = false;
        try {
            var native = Marshal.PtrToStructure<Buffer>(buffer);
            Marshal.Copy(pcm, 0, native.Data, pcm.Length);
            Marshal.WriteInt32(buffer, (int)Marshal.OffsetOf<Buffer>(nameof(Buffer.Size)), pcm.Length);
            Check(AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero));
            pending.Enqueue(buffer); queued = true;
            if (!started) { Check(AudioQueueStart(queue, IntPtr.Zero)); started = true; }
        } catch { if (!queued) AudioQueueFreeBuffer(queue, buffer); throw; }
    }
    private async Task ReleaseFirstAsync(CancellationToken ct) {
        var buffer = pending.Peek();
        while (!completed.ContainsKey(buffer)) await Task.Delay(10, ct);
        pending.Dequeue(); completed.TryRemove(buffer, out _); AudioQueueFreeBuffer(queue, buffer);
    }
    public async Task DrainAsync(CancellationToken ct) { while (pending.Count > 0) await ReleaseFirstAsync(ct); }
    public void Dispose() {
        if (queue == IntPtr.Zero) return;
        AudioQueueStop(queue, true);
        // Disposing the queue releases all remaining buffers and waits for callbacks.
        AudioQueueDispose(queue, true); queue = IntPtr.Zero; pending.Clear(); completed.Clear(); GC.KeepAlive(callback);
    }
    private static void Check(int code) { if (code != 0) throw new InvalidOperationException($"macOS AudioQueue failed ({code})."); }
}
