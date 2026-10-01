using System;
using System.Runtime.InteropServices;

namespace OnkeyDesktopPet
{
    // Listens to what Windows is playing (a WASAPI loopback capture of the default speakers)
    // and feeds it to a BeatTracker. No permission prompt is needed. Poll() is called from the
    // UI timer, which keeps all the COM objects on one thread.
    internal sealed class MusicListener : IDisposable
    {
        private const int Shared = 0, Loopback = 0x00020000, Silent = 0x2;
        private const int Render = 0, Console = 0;
        private const long OneSecond = 10000000;   // In 100-nanosecond units.

        private IAudioClient client;
        private IAudioCaptureClient capture;
        private int channels, bitsPerSample;
        private bool isFloat;
        private double sampleRate;
        private float[] mono = new float[0], floats = new float[0];
        private short[] shorts = new short[0];
        private double lastFed, retryAt;
        public BeatTracker Tracker;

        public BeatTracker.Beat Current { get { return Tracker != null ? Tracker.Current : new BeatTracker.Beat(); } }

        public void Start(double now)
        {
            Stop();
            IMMDeviceEnumerator devices = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice speakers;
            Check(devices.GetDefaultAudioEndpoint(Render, Console, out speakers), "Finding the speakers");
            Guid iid = typeof(IAudioClient).GUID;
            object o;
            Check(speakers.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out o), "Opening the speakers");
            client = (IAudioClient)o;

            IntPtr format;
            Check(client.GetMixFormat(out format), "Reading the speakers' format");
            try
            {
                int tag = Marshal.ReadInt16(format, 0) & 0xFFFF;
                channels = Marshal.ReadInt16(format, 2);
                sampleRate = Marshal.ReadInt32(format, 4);
                bitsPerSample = Marshal.ReadInt16(format, 14);
                // WAVE_FORMAT_IEEE_FLOAT, or WAVE_FORMAT_EXTENSIBLE with a float sub-format.
                isFloat = tag == 3 || (tag == 0xFFFE && Marshal.ReadInt32(format, 24) == 3);
                if (!(isFloat && bitsPerSample == 32) && !(!isFloat && bitsPerSample == 16))
                    throw new NotSupportedException("The speakers use an audio format Onkey can't read (" + bitsPerSample + "-bit).");
                Check(client.Initialize(Shared, Loopback, OneSecond, 0, format, IntPtr.Zero), "Listening to the speakers");
            }
            finally { Marshal.FreeCoTaskMem(format); }

            Guid captureId = typeof(IAudioCaptureClient).GUID;
            object c;
            Check(client.GetService(ref captureId, out c), "Listening to the speakers");
            capture = (IAudioCaptureClient)c;
            Tracker = new BeatTracker(sampleRate);
            Check(client.Start(), "Starting to listen");
            lastFed = now;
        }

        // Reads everything captured since the last call. Windows sends nothing at all while
        // nothing is playing, so those gaps are fed in as silence (otherwise the last beat
        // would carry on forever). If the speakers change, it starts again a few seconds later.
        public void Poll(double now)
        {
            if (client == null)
            {
                if (retryAt > 0 && now >= retryAt) { retryAt = 0; try { Start(now); } catch { Stop(); retryAt = now + 3; } }
                return;
            }
            try
            {
                bool got = false;
                uint packet;
                Check(capture.GetNextPacketSize(out packet), "Reading audio");
                while (packet > 0)
                {
                    IntPtr data;
                    uint frames, flags;
                    ulong position, qpc;
                    Check(capture.GetBuffer(out data, out frames, out flags, out position, out qpc), "Reading audio");
                    Feed(data, (int)frames, (flags & Silent) != 0, now);
                    capture.ReleaseBuffer(frames);
                    got = true;
                    Check(capture.GetNextPacketSize(out packet), "Reading audio");
                }
                if (got) lastFed = now;
                else if (now - lastFed > 0.2)
                {
                    Feed(IntPtr.Zero, (int)Math.Min(sampleRate, (now - lastFed) * sampleRate), true, now);
                    lastFed = now;
                }
            }
            catch
            {
                // Usually the speakers were unplugged or switched.
                Stop();
                retryAt = now + 3;
            }
        }

        private void Feed(IntPtr data, int frames, bool silent, double now)
        {
            if (frames <= 0) return;
            if (mono.Length < frames) mono = new float[frames];
            if (silent || data == IntPtr.Zero) Array.Clear(mono, 0, frames);
            else
            {
                // Copy the packet over in one go, then mix its channels down to one.
                int samples = frames * channels;
                float scale = 1f / channels;
                if (isFloat)
                {
                    if (floats.Length < samples) floats = new float[samples];
                    Marshal.Copy(data, floats, 0, samples);
                    for (int f = 0; f < frames; f++)
                    {
                        float sum = 0;
                        for (int c = 0; c < channels; c++) sum += floats[f * channels + c];
                        mono[f] = sum * scale;
                    }
                }
                else
                {
                    if (shorts.Length < samples) shorts = new short[samples];
                    Marshal.Copy(data, shorts, 0, samples);
                    for (int f = 0; f < frames; f++)
                    {
                        float sum = 0;
                        for (int c = 0; c < channels; c++) sum += shorts[f * channels + c] / 32768f;
                        mono[f] = sum * scale;
                    }
                }
            }
            Tracker.Process(mono, frames, now);
        }

        public void Stop()
        {
            if (client != null) { try { client.Stop(); } catch { } }
            if (capture != null) Marshal.ReleaseComObject(capture);
            if (client != null) Marshal.ReleaseComObject(client);
            capture = null; client = null; Tracker = null;
        }

        public void Dispose() { retryAt = 0; Stop(); }

        private static void Check(int hr, string what)
        {
            if (hr < 0) throw new InvalidOperationException(what + " failed (error 0x" + hr.ToString("X8") + ").");
        }

        // The Core Audio COM interfaces, declared up to the last method used (vtable order matters).

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint padding);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }
    }
}
