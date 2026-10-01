using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnkeyDesktopPet
{
    // His sound, pre-scaled for volume (SoundPlayer has no volume control), and its
    // loudness 60 times a second to move his mouth.
    internal sealed class OnkeySound : IDisposable
    {
        private readonly byte[] original;
        private readonly int dataStart = -1, dataLength, channels = 1, sampleRate = 44100, bits;
        private SoundPlayer player;
        private double volume = -1;
        public readonly double[] Envelope = new double[0];

        public OnkeySound(string path)
        {
            original = File.ReadAllBytes(path);
            for (int i = 12; i + 8 <= original.Length; )
            {
                string id = System.Text.Encoding.ASCII.GetString(original, i, 4);
                int size = BitConverter.ToInt32(original, i + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(original, i + 10);
                    sampleRate = BitConverter.ToInt32(original, i + 12);
                    bits = BitConverter.ToInt16(original, i + 22);
                }
                else if (id == "data") { dataStart = i + 8; dataLength = Math.Min(size, original.Length - dataStart); break; }
                i += 8 + size + (size & 1);
            }
            if (dataStart < 0 || bits != 16) return;
            int frames = dataLength / (2 * channels), window = Math.Max(1, sampleRate / 60);
            List<double> levels = new List<double>();
            for (int start = 0; start < frames; start += window)
            {
                int end = Math.Min(frames, start + window);
                double sum = 0;
                for (int f = start; f < end; f++)
                {
                    double v = BitConverter.ToInt16(original, dataStart + f * 2 * channels) / 32768.0;
                    sum += v * v;
                }
                levels.Add(Math.Sqrt(sum / (end - start)));
            }
            double loudest = 0;
            foreach (double l in levels) loudest = Math.Max(loudest, l);
            if (loudest > 0) { Envelope = new double[levels.Count]; for (int i = 0; i < levels.Count; i++) Envelope[i] = levels[i] / loudest; }
        }

        public void Play(double newVolume)
        {
            if (player == null || Math.Abs(newVolume - volume) > 0.001)
            {
                if (player != null) { player.Stop(); player.Dispose(); }
                byte[] bytes = (byte[])original.Clone();
                if (dataStart >= 0 && bits == 16)
                    for (int i = dataStart; i + 1 < dataStart + dataLength; i += 2)
                    {
                        int v = (int)(BitConverter.ToInt16(bytes, i) * newVolume);
                        bytes[i] = (byte)(v & 0xFF); bytes[i + 1] = (byte)((v >> 8) & 0xFF);
                    }
                player = new SoundPlayer(new MemoryStream(bytes));
                player.Load();
                volume = newVolume;
            }
            player.Stop();
            player.Play();
        }

        public void Stop() { if (player != null) player.Stop(); }
        public void Dispose() { if (player != null) { player.Stop(); player.Dispose(); } }
    }
}
