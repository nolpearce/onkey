using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OnkeyDesktopPet
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OnkeyForm());
        }
    }

    // Render onto premultiplied transparent pixels; never onto a colour key.
    // Arm masks separate the arms from the ears and head in the supplied sprite.
    public sealed class OnkeyRenderer : IDisposable
    {
        public const int CanvasWidth = 180;
        public const int CanvasHeight = 132;
        public const int FrameCount = 40;
        private readonly Bitmap head, leftArm, rightArm;
        private const float SourceWidth = 1774f;
        private const float SourceHeight = 887f;
        private const float PetWidth = 140f;

        public OnkeyRenderer(string spritePath)
        {
            using (Image original = Image.FromFile(spritePath))
            using (Bitmap source = new Bitmap((int)SourceWidth, (int)SourceHeight, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(source))
            using (GraphicsPath left = new GraphicsPath())
            using (GraphicsPath right = new GraphicsPath())
            {
                g.Clear(Color.Transparent);
                g.DrawImage(original, 0, 0, source.Width, source.Height);
                left.AddPolygon(new PointF[] {
                    new PointF(0, 600), new PointF(330, 600),
                    new PointF(410, 722), new PointF(565, 722),
                    new PointF(620, 752), new PointF(643, 778),
                    new PointF(643, 887), new PointF(0, 887) });
                right.AddPolygon(new PointF[] {
                    new PointF(1137, 762), new PointF(1200, 748),
                    new PointF(1340, 730), new PointF(1440, 620),
                    new PointF(1774, 620), new PointF(1774, 887),
                    new PointF(1137, 887) });
                leftArm = Cut(source, left);
                rightArm = Cut(source, right);
                head = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                using (Graphics h = Graphics.FromImage(head))
                using (Region headRegion = new Region(new Rectangle(0, 0, source.Width, source.Height)))
                {
                    h.Clear(Color.Transparent);
                    headRegion.Exclude(left);
                    headRegion.Exclude(right);
                    h.SetClip(headRegion, CombineMode.Replace);
                    h.DrawImageUnscaled(source, 0, 0);
                }
            }
        }

        private static Bitmap Cut(Bitmap source, GraphicsPath path)
        {
            Bitmap part = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(part))
            {
                g.Clear(Color.Transparent);
                g.SetClip(path);
                g.DrawImageUnscaled(source, 0, 0);
            }
            return part;
        }

        public Bitmap Render(double phase, bool walking)
        {
            Bitmap result = new Bitmap(CanvasWidth, CanvasHeight, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = PetWidth / SourceWidth;
                float bounce = walking ? (float)(2.2 * Math.Abs(Math.Sin(phase))) : 0f;
                g.TranslateTransform((CanvasWidth - PetWidth) / 2f, 26f - bounce);
                g.ScaleTransform(scale, scale);
                // Alternating planted and lifted hands propel the head forward.
                float swing = walking ? (float)(18 * Math.Sin(phase)) : 0f;
                DrawArm(g, leftArm, new PointF(633, 808), -16f + swing);
                DrawArm(g, rightArm, new PointF(1147, 808), 16f + swing);
                // Cover each rotating joint with a small patch of matching arm colour.
                using (Brush joint = new SolidBrush(Color.FromArgb(157, 87, 47)))
                {
                    g.FillEllipse(joint, 612, 782, 60, 47);
                    g.FillEllipse(joint, 1112, 782, 60, 47);
                }
                g.DrawImageUnscaled(head, 0, 0);
            }
            return result;
        }

        private static void DrawArm(Graphics g, Bitmap arm, PointF pivot, float angle)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(pivot.X, pivot.Y);
            g.RotateTransform(angle);
            g.TranslateTransform(-pivot.X, -pivot.Y);
            g.DrawImageUnscaled(arm, 0, 0);
            g.Restore(state);
        }

        public void Dispose() { head.Dispose(); leftArm.Dispose(); rightArm.Dispose(); }
    }

    public sealed class OnkeyForm : Form
    {
        private readonly Random random = new Random();
        private readonly Timer timer = new Timer();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Bitmap[] frames = new Bitmap[OnkeyRenderer.FrameCount];
        private readonly Bitmap idle;
        private readonly ToolStripMenuItem pauseItem, soundItem;
        private readonly string soundPath;
        private SoundPlayer player;
        private LayeredSurface surface;
        private Rectangle area;
        private double px, py, targetX, targetY, phase, lastTime, restUntil, nextSound;
        private bool paused, muted, disposed;

        public OnkeyForm()
        {
            string folder = Environment.GetEnvironmentVariable("ONKEY_ASSET_DIR") ?? AppDomain.CurrentDomain.BaseDirectory;
            soundPath = Path.Combine(folder, "Sounds", "oooo.wav");
            string animationFolder = Path.Combine(folder, "Frames");
            idle = LoadFrame(Path.Combine(animationFolder, "idle.png"));
            for (int i = 0; i < frames.Length; i++)
                frames[i] = LoadFrame(Path.Combine(animationFolder, "frame" + i.ToString("D2") + ".png"));
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(OnkeyRenderer.CanvasWidth, OnkeyRenderer.CanvasHeight);
            StartPosition = FormStartPosition.Manual;
            area = Screen.FromPoint(Cursor.Position).WorkingArea;
            px = Math.Max(area.Left, area.Right - Width - 60);
            py = Math.Max(area.Top, area.Bottom - Height - 40);
            Location = new Point((int)px, (int)py);
            PickTarget();
            nextSound = SoundDelay();

            ContextMenuStrip menu = new ContextMenuStrip();
            pauseItem = new ToolStripMenuItem("Pause Onkey");
            pauseItem.Click += delegate { TogglePause(); };
            soundItem = new ToolStripMenuItem(File.Exists(soundPath) ? "Mute sounds" : "Sound clip not installed");
            soundItem.Enabled = File.Exists(soundPath);
            soundItem.Click += delegate {
                muted = !muted;
                soundItem.Text = muted ? "Unmute sounds" : "Mute sounds";
                if (muted && player != null) player.Stop();
            };
            ToolStripMenuItem moveItem = new ToolStripMenuItem("Bring Onkey to this screen");
            moveItem.Click += delegate {
                area = Screen.FromPoint(Cursor.Position).WorkingArea;
                px = area.Left + Math.Max(0, (area.Width - Width) / 2);
                py = area.Top + Math.Max(0, (area.Height - Height) / 2);
                PickTarget();
                Present(idle);
            };
            ToolStripMenuItem quitItem = new ToolStripMenuItem("Exit Onkey");
            quitItem.Click += delegate { Close(); };
            menu.Items.Add(pauseItem);
            menu.Items.Add(soundItem);
            menu.Items.Add(moveItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quitItem);
            tray.Icon = SystemIcons.Information;
            tray.Text = "Onkey - right-click for controls";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { TogglePause(); };
            timer.Interval = 33;
            timer.Tick += UpdatePet;
        }

        private static Bitmap LoadFrame(string path)
        {
            using (Image source = Image.FromFile(path))
            {
                Bitmap frame = new Bitmap(OnkeyRenderer.CanvasWidth, OnkeyRenderer.CanvasHeight, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImageUnscaled(source, 0, 0);
                }
                return frame;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_LAYERED + WS_EX_TRANSPARENT + WS_EX_TOOLWINDOW + WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000;
                return cp;
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            surface = new LayeredSurface(Width, Height);
            Present(idle);
            tray.Visible = true;
            clock.Start();
            timer.Start();
        }

        private double SoundDelay() { return 90 + random.NextDouble() * 90; }
        private void TogglePause()
        {
            paused = !paused;
            pauseItem.Text = paused ? "Resume Onkey" : "Pause Onkey";
            if (paused) { clock.Stop(); if (player != null) player.Stop(); Present(idle); }
            else clock.Start();
        }
        private void PickTarget()
        {
            int maxX = Math.Max(area.Left, area.Right - Width);
            int maxY = Math.Max(area.Top, area.Bottom - Height);
            Point cursor = Cursor.Position;
            if (random.Next(5) == 0 && area.Contains(cursor))
            {
                targetX = Math.Max(area.Left, Math.Min(maxX, cursor.X - Width / 2));
                targetY = Math.Max(area.Top, Math.Min(maxY, cursor.Y - Height / 2));
            }
            else
            {
                targetX = area.Left + random.Next(maxX - area.Left + 1);
                targetY = area.Top + random.Next(maxY - area.Top + 1);
            }
        }
        private void UpdatePet(object sender, EventArgs e)
        {
            if (paused) return;
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.08, Math.Max(0, now - lastTime));
            lastTime = now;
            if (now >= nextSound) { PlaySound(); nextSound = now + SoundDelay(); }
            if (now < restUntil) { Present(idle); return; }
            double dx = targetX - px, dy = targetY - py;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 3)
            {
                restUntil = now + 2 + random.NextDouble() * 4;
                PickTarget();
                Present(idle);
                return;
            }
            double step = Math.Min(distance, 42 * dt);
            px += step * dx / distance;
            py += step * dy / distance;
            phase = (phase + dt * 2 * Math.PI * 1.15) % (2 * Math.PI);
            int index = ((int)(phase / (2 * Math.PI) * frames.Length)) % frames.Length;
            Present(frames[index]);
        }
        private void PlaySound()
        {
            if (muted || !File.Exists(soundPath)) return;
            try
            {
                if (player == null) { player = new SoundPlayer(soundPath); player.Load(); }
                player.Play();
            }
            catch { /* An invalid or unavailable clip must not interrupt the pet. */ }
        }
        private void Present(Bitmap frame)
        {
            if (surface != null)
                surface.Show(Handle, frame, (int)Math.Round(px), (int)Math.Round(py));
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!disposed)
            {
                disposed = true;
                timer.Stop(); timer.Dispose();
                tray.Visible = false;
                if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                tray.Dispose();
                if (player != null) { player.Stop(); player.Dispose(); }
                if (surface != null) surface.Dispose();
                idle.Dispose();
                foreach (Bitmap frame in frames) if (frame != null) frame.Dispose();
            }
            base.OnFormClosed(e);
        }
    }

    // One reusable 32-bit DIB. Alpha is premultiplied before Windows composites it.
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr memoryDC, bitmap, previousBitmap, pixels;
        private readonly int width, height;
        private readonly byte[] buffer;

        public LayeredSurface(int w, int h)
        {
            width = w; height = h; buffer = new byte[w * h * 4];
            IntPtr screenDC = Native.GetDC(IntPtr.Zero);
            try
            {
                memoryDC = Native.CreateCompatibleDC(screenDC);
                Native.BitmapInfo info = new Native.BitmapInfo();
                info.HeaderSize = (uint)Marshal.SizeOf(typeof(Native.BitmapInfo));
                info.Width = w; info.Height = -h;
                info.Planes = 1; info.BitCount = 32; info.SizeImage = (uint)buffer.Length;
                bitmap = Native.CreateDIBSection(screenDC, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (memoryDC == IntPtr.Zero || bitmap == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                previousBitmap = Native.SelectObject(memoryDC, bitmap);
            }
            catch { Dispose(); throw; }
            finally { if (screenDC != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screenDC); }
        }
        public void Show(IntPtr window, Bitmap frame, int x, int y)
        {
            BitmapData data = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int row = 0; row < height; row++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), buffer, row * width * 4, width * 4);
            }
            finally { frame.UnlockBits(data); }
            Marshal.Copy(buffer, 0, pixels, buffer.Length);
            Native.Point position = new Native.Point(x, y);
            Native.Point origin = new Native.Point(0, 0);
            Native.Size size = new Native.Size(width, height);
            Native.Blend blend = new Native.Blend();
            blend.Alpha = 255; blend.Format = 1;
            if (!Native.UpdateLayeredWindow(window, IntPtr.Zero, ref position, ref size, memoryDC, ref origin, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Dispose()
        {
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
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDC, ref Point position, ref Size size, IntPtr sourceDC, ref Point source, uint key, ref Blend blend, uint flags);
    }
}
