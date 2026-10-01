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
    // One reusable 32-bit DIB that Windows composites from, premultiplied alpha. Canvas is a
    // GDI+ view of the DIB's own memory, so frames are drawn straight into what Windows
    // shows, with no copying in between.
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr memoryDC, bitmap, previousBitmap, pixels;
        private readonly int width, height;
        public Bitmap Canvas;

        public LayeredSurface(int w, int h)
        {
            width = w; height = h;
            IntPtr screenDC = Native.GetDC(IntPtr.Zero);
            try
            {
                memoryDC = Native.CreateCompatibleDC(screenDC);
                Native.BitmapInfo info = new Native.BitmapInfo();
                info.HeaderSize = (uint)Marshal.SizeOf(typeof(Native.BitmapInfo));
                info.Width = w; info.Height = -h;   // Negative: rows run top-down, as GDI+ expects.
                info.Planes = 1; info.BitCount = 32; info.SizeImage = (uint)(w * h * 4);
                bitmap = Native.CreateDIBSection(screenDC, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (memoryDC == IntPtr.Zero || bitmap == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                previousBitmap = Native.SelectObject(memoryDC, bitmap);
                Canvas = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, pixels);
            }
            catch { Dispose(); throw; }
            finally { if (screenDC != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screenDC); }
        }

        // Hands what's been drawn on Canvas to Windows, at (x, y).
        public void Show(IntPtr window, int x, int y, byte opacity)
        {
            Native.GdiFlush();
            Native.Point position = new Native.Point(x, y);
            Native.Point origin = new Native.Point(0, 0);
            Native.Size size = new Native.Size(width, height);
            Native.Blend blend = new Native.Blend();
            blend.Alpha = opacity; blend.Format = 1;
            if (!Native.UpdateLayeredWindow(window, IntPtr.Zero, ref position, ref size, memoryDC, ref origin, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            if (Canvas != null) { Canvas.Dispose(); Canvas = null; }
            if (previousBitmap != IntPtr.Zero && memoryDC != IntPtr.Zero) Native.SelectObject(memoryDC, previousBitmap);
            if (bitmap != IntPtr.Zero) { Native.DeleteObject(bitmap); bitmap = IntPtr.Zero; }
            if (memoryDC != IntPtr.Zero) { Native.DeleteDC(memoryDC); memoryDC = IntPtr.Zero; }
            previousBitmap = IntPtr.Zero;
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct Size { public int Width, Height; public Size(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { public byte Operation, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo
        {
            public uint HeaderSize; public int Width, Height; public ushort Planes, BitCount;
            public uint Compression, SizeImage; public int XPixelsPerMeter, YPixelsPerMeter; public uint ColoursUsed, ColoursImportant;
        }
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("gdi32.dll")] internal static extern bool GdiFlush();
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDC, ref Point position, ref Size size, IntPtr sourceDC, ref Point source, uint key, ref Blend blend, uint flags);
    }
}
