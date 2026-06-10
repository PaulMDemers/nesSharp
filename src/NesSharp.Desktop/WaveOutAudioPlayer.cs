using System.ComponentModel;
using System.Runtime.InteropServices;
using NesSharp.Core.Apu;

namespace NesSharp.Desktop;

internal sealed class WaveOutAudioPlayer : IDisposable
{
    private const int BufferCount = 6;
    private const int SamplesPerBuffer = 1_024;
    private const int BytesPerSample = 2;
    private const int WaveMapper = -1;
    private const int CallbackNull = 0x00000000;
    private const int WhdrDone = 0x00000001;
    private const int WhdrPrepared = 0x00000002;

    private readonly AudioBuffer[] buffers;
    private readonly Lock sync = new();
    private IntPtr waveOutHandle;
    private int nextBuffer;
    private bool disposed;

    public WaveOutAudioPlayer()
    {
        buffers = Enumerable.Range(0, BufferCount)
            .Select(_ => new AudioBuffer(SamplesPerBuffer * BytesPerSample))
            .ToArray();
    }

    public void Start()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (waveOutHandle != IntPtr.Zero)
            {
                return;
            }

            var format = new WaveFormat
            {
                FormatTag = 1,
                Channels = 1,
                SamplesPerSec = ApuBus.OutputSampleRate,
                AvgBytesPerSec = ApuBus.OutputSampleRate * BytesPerSample,
                BlockAlign = BytesPerSample,
                BitsPerSample = 16,
                Size = 0
            };

            var result = WaveOutOpen(out var openedHandle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, CallbackNull);
            if (result != 0)
            {
                waveOutHandle = IntPtr.Zero;
                ThrowIfWaveError(result);
            }

            waveOutHandle = openedHandle;
        }
    }

    public void Stop()
    {
        lock (sync)
        {
            CloseDevice();
        }
    }

    public void Enqueue(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        lock (sync)
        {
            if (disposed || waveOutHandle == IntPtr.Zero)
            {
                return;
            }

            var offset = 0;
            while (offset < samples.Length)
            {
                var buffer = NextAvailableBuffer();
                if (buffer is null)
                {
                    return;
                }

                var sampleCount = Math.Min(samples.Length - offset, SamplesPerBuffer);
                FillBuffer(buffer, samples.Slice(offset, sampleCount));
                QueueBuffer(buffer, sampleCount * BytesPerSample);
                offset += sampleCount;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            CloseDevice();
            foreach (var buffer in buffers)
            {
                buffer.Dispose();
            }

            disposed = true;
        }
    }

    private AudioBuffer? NextAvailableBuffer()
    {
        for (var i = 0; i < buffers.Length; i++)
        {
            var index = (nextBuffer + i) % buffers.Length;
            if (buffers[index].IsAvailable)
            {
                nextBuffer = (index + 1) % buffers.Length;
                return buffers[index];
            }
        }

        return null;
    }

    private void QueueBuffer(AudioBuffer buffer, int byteCount)
    {
        if (buffer.IsPrepared)
        {
            ThrowIfWaveError(WaveOutUnprepareHeader(waveOutHandle, buffer.HeaderPointer, Marshal.SizeOf<WaveHeader>()));
            buffer.Header.Flags = 0;
        }

        buffer.Header.BufferLength = (uint)byteCount;
        buffer.WriteHeader();
        ThrowIfWaveError(WaveOutPrepareHeader(waveOutHandle, buffer.HeaderPointer, Marshal.SizeOf<WaveHeader>()));
        buffer.ReadHeader();
        ThrowIfWaveError(WaveOutWrite(waveOutHandle, buffer.HeaderPointer, Marshal.SizeOf<WaveHeader>()));
        buffer.ReadHeader();
    }

    private void CloseDevice()
    {
        if (waveOutHandle == IntPtr.Zero)
        {
            return;
        }

        WaveOutReset(waveOutHandle);
        foreach (var buffer in buffers)
        {
            buffer.ReadHeader();
            if (buffer.IsPrepared)
            {
                WaveOutUnprepareHeader(waveOutHandle, buffer.HeaderPointer, Marshal.SizeOf<WaveHeader>());
                buffer.Header.Flags = 0;
                buffer.WriteHeader();
            }
        }

        WaveOutClose(waveOutHandle);
        waveOutHandle = IntPtr.Zero;
        nextBuffer = 0;
    }

    private static void FillBuffer(AudioBuffer buffer, ReadOnlySpan<float> samples)
    {
        var pcm = MemoryMarshal.Cast<byte, short>(buffer.Bytes.AsSpan());
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = Math.Clamp(samples[i], -1.0f, 1.0f);
            pcm[i] = (short)Math.Round(sample * short.MaxValue);
        }
    }

    private static void ThrowIfWaveError(int result)
    {
        if (result != 0)
        {
            throw new Win32Exception(result, $"waveOut call failed with MMRESULT {result}.");
        }
    }

    [DllImport("winmm.dll", EntryPoint = "waveOutOpen")]
    private static extern int WaveOutOpen(
        out IntPtr waveOutHandle,
        int deviceId,
        ref WaveFormat format,
        IntPtr callback,
        IntPtr instance,
        int flags);

    [DllImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    private static extern int WaveOutPrepareHeader(IntPtr waveOutHandle, IntPtr header, int headerSize);

    [DllImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    private static extern int WaveOutUnprepareHeader(IntPtr waveOutHandle, IntPtr header, int headerSize);

    [DllImport("winmm.dll", EntryPoint = "waveOutWrite")]
    private static extern int WaveOutWrite(IntPtr waveOutHandle, IntPtr header, int headerSize);

    [DllImport("winmm.dll", EntryPoint = "waveOutReset")]
    private static extern int WaveOutReset(IntPtr waveOutHandle);

    [DllImport("winmm.dll", EntryPoint = "waveOutClose")]
    private static extern int WaveOutClose(IntPtr waveOutHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    private sealed class AudioBuffer : IDisposable
    {
        private bool disposed;

        public AudioBuffer(int byteLength)
        {
            Bytes = new byte[byteLength];
            DataHandle = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
            HeaderPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
            Header = new WaveHeader
            {
                Data = DataHandle.AddrOfPinnedObject(),
                BufferLength = (uint)Bytes.Length,
                Flags = WhdrDone
            };
            WriteHeader();
        }

        public byte[] Bytes { get; }

        public GCHandle DataHandle { get; }

        public IntPtr HeaderPointer { get; }

        public WaveHeader Header;

        public bool IsAvailable
        {
            get
            {
                ReadHeader();
                return (Header.Flags & WhdrDone) != 0 || (Header.Flags & WhdrPrepared) == 0;
            }
        }

        public bool IsPrepared => (Header.Flags & WhdrPrepared) != 0;

        public void ReadHeader()
        {
            Header = Marshal.PtrToStructure<WaveHeader>(HeaderPointer);
        }

        public void WriteHeader()
        {
            Marshal.StructureToPtr(Header, HeaderPointer, false);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Marshal.FreeHGlobal(HeaderPointer);
            DataHandle.Free();
            disposed = true;
        }
    }
}
