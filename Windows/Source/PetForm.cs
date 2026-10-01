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
    // One Onkey on screen: his window, where he's walking, and his eyes, blinks and mouth.
    internal sealed class PetForm : Form
    {
        // Delay per eye, [left, right] on screen: the left blinks first, the right close behind.
        private static readonly double[] BlinkOrder = { 0, 0.14 };

        private readonly OnkeyApp app;
        public Rectangle Area;
        private double px, py, targetX, targetY, phase, restUntil, nextSound;
        private double blinkStart = -100, nextBlink, mouthOpen;
        public double SoundStart = double.NegativeInfinity;
        private readonly PointF[] gaze = new PointF[2];
        private Bitmap current;
        private float currentBounce;
        private int canvasWidth, canvasHeight;   // Window size in pixels.
        // What's on screen now, so unchanged frames are skipped (resting Onkeys cost nothing).
        private Bitmap shownFrame;
        private int shownX = int.MinValue, shownY, shownGaze0X, shownGaze0Y, shownGaze1X, shownGaze1Y, shownLid0, shownLid1, shownMouth;
        private byte shownAlpha;
        private bool redraw = true;
        private int shownSquash;
        // How squashed he is for the beat right now (0 normal, 0.15 is 15% shorter); set by the app.
        public float Squash;
        private LayeredSurface surface;
        private bool dragging, dragMoved, closed;
        private Point grabMouse;
        private double grabX, grabY;

        public double X { get { return px; } }
        public double Y { get { return py; } }
        public bool Dragging { get { return dragging; } }
        private float PixelScale { get { return app.PixelScale; } }

        public PetForm(OnkeyApp app, Point origin, Rectangle area)
        {
            this.app = app;
            Area = area;
            px = origin.X; py = origin.Y;
            nextBlink = app.Wall.Elapsed.TotalSeconds + 2 + app.Random.NextDouble() * 6;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            FramesChanged();
            ClampToArea();
            Location = new Point((int)px, (int)py);
            PickTarget();
            RescheduleSound();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_LAYERED + WS_EX_TOOLWINDOW + WS_EX_NOACTIVATE, plus WS_EX_TRANSPARENT
                // (clicks pass through) unless dragging is enabled.
                cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000;
                // The base Form constructor reads CreateParams before ours has set app.
                if (app == null || !app.Settings.Bool("draggable")) cp.ExStyle |= 0x00000020;
                return cp;
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyAppearance();
            Present();
        }

        // New frames (a new size, or eyes switched): a canvas and surface to match.
        public void FramesChanged()
        {
            canvasWidth = app.Idle.Width; canvasHeight = app.Idle.Height;
            current = app.Idle; currentBounce = 0;
            ClientSize = new System.Drawing.Size(canvasWidth, canvasHeight);
            if (surface != null) { surface.Dispose(); surface = null; }
            if (IsHandleCreated) surface = new LayeredSurface(canvasWidth, canvasHeight);
            redraw = true;
        }

        public void ApplyAppearance()
        {
            TopMost = app.Settings.Get("layer") != "desktop";
            // Switching click-through on or off needs the window style updated in place.
            if (IsHandleCreated)
            {
                int style = Native.GetWindowLong(Handle, -20);
                style = app.Settings.Bool("draggable") ? style & ~0x20 : style | 0x20;
                Native.SetWindowLong(Handle, -20, style);
                if (surface == null) surface = new LayeredSurface(canvasWidth, canvasHeight);
            }
            redraw = true;
        }

        public void SendToBottom()
        {
            if (IsHandleCreated) Native.SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // HWND_BOTTOM, no size/move/activate.
        }

        // After a size change: keep him centred where he was, at the new size.
        public void Resized(int oldWidth, int oldHeight)
        {
            px += (oldWidth - canvasWidth) / 2.0;
            py += (oldHeight - canvasHeight) / 2.0;
            ClampToArea();
            PickTarget();
        }

        public void ShowIdle() { current = app.Idle; currentBounce = 0; }
        public void RestFor(double seconds) { restUntil = app.Clock + seconds; }
        public void RescheduleSound() { nextSound = app.Clock + app.SoundDelay(); }

        public void MoveTo(Rectangle area, double x, double y)
        {
            Area = area; px = x; py = y;
            ClampToArea();
            RestFor(2);
            PickTarget();
            ShowIdle();
        }

        public void ScreensChanged()
        {
            Area = Screen.FromPoint(new Point((int)px + canvasWidth / 2, (int)py + canvasHeight / 2)).WorkingArea;
            ClampToArea();
            PickTarget();
        }

        // Movement

        // Allowed window positions, letting the transparent canvas margin hang off-screen
        // so Onkey's hands can touch the very edge.
        private void Limits(out double minX, out double maxX, out double minY, out double maxY)
        {
            float s = PixelScale;
            RectangleF b = app.PetBounds;
            minX = Area.Left - b.Left * s;
            maxX = Math.Max(minX, Area.Right - b.Right * s);
            minY = Area.Top - b.Top * s;
            maxY = Math.Max(minY, Area.Bottom - b.Bottom * s);
        }

        private void ClampToArea()
        {
            double minX, maxX, minY, maxY;
            Limits(out minX, out maxX, out minY, out maxY);
            px = Math.Min(maxX, Math.Max(minX, px));
            py = Math.Min(maxY, Math.Max(minY, py));
        }

        public void PickTarget()
        {
            double minX, maxX, minY, maxY;
            Limits(out minX, out maxX, out minY, out maxY);
            string zone = app.Settings.Get("zone");
            if (zone == "stay") { targetX = px; targetY = py; return; }
            double x = minX + app.Random.NextDouble() * (maxX - minX), y = minY + app.Random.NextDouble() * (maxY - minY);
            int chase = (int)app.Settings.Number("chase");
            Point mouse = Cursor.Position;
            if (chase > 0 && app.Random.Next(chase) == 0 && Area.Contains(mouse))
            {
                x = Math.Min(maxX, Math.Max(minX, mouse.X - canvasWidth / 2.0));
                y = Math.Min(maxY, Math.Max(minY, mouse.Y - canvasHeight / 2.0));
            }
            if (zone == "bottom") y = maxY;
            else if (zone == "top") y = minY;
            else if (zone == "left") x = minX;
            else if (zone == "right") x = maxX;
            targetX = x; targetY = y;
        }

        public void Walk(double dt)
        {
            double clock = app.Clock;
            if (clock >= nextSound) { app.Speak(this, false); nextSound = clock + app.SoundDelay(); }
            ShowIdle();
            if (clock < restUntil || app.Settings.Get("zone") == "stay") return;
            double dx = targetX - px, dy = targetY - py;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 3)
            {
                restUntil = clock + 2 + app.Random.NextDouble() * 4;
                PickTarget();
                return;
            }
            double speed = app.Settings.Number("speed") * PixelScale;
            double step = Math.Min(distance, speed * dt);
            px += step * dx / distance;
            py += step * dy / distance;
            // Faster walking means faster arms, so he never looks like he's skating.
            phase = (phase + dt * 2 * Math.PI * 1.15 * speed / (42 * PixelScale)) % (2 * Math.PI);
            Bitmap[] frames = app.Frames;
            int index = ((int)(phase / (2 * Math.PI) * frames.Length)) % frames.Length;
            current = frames[index]; currentBounce = app.FrameBounce[index];
        }

        // Eyes, eyelids and mouth

        public void UpdateFace(double now, double dt)
        {
            // Each pupil eases toward the cursor, so he goes a bit cross-eyed when it's close.
            if (app.Watching)
            {
                Point mouse = Cursor.Position;
                float s = PixelScale, ease = (float)(1 - Math.Exp(-dt * 14));
                for (int i = 0; i < 2; i++)
                {
                    PointF c = OnkeyRenderer.CanvasPoint(app.Renderer.Pupils[i].Center, currentBounce);
                    double dx = mouse.X - (Math.Round(px) + c.X * s), dy = mouse.Y - (Math.Round(py) + c.Y * s);
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    // Full travel once the cursor is a few eye-widths away; centred when it's on the eye.
                    double reach = OnkeyRenderer.PupilTravel * Math.Min(1, distance / (25 * s));
                    PointF target = distance < 0.5 ? PointF.Empty : new PointF((float)(dx / distance * reach), (float)(dy / distance * reach));
                    gaze[i] = new PointF(gaze[i].X + (target.X - gaze[i].X) * ease, gaze[i].Y + (target.Y - gaze[i].Y) * ease);
                }
            }
            // Blinks every 6-14 seconds.
            if (app.Settings.Bool("blink") && now >= nextBlink)
            {
                blinkStart = now;
                nextBlink = now + 6 + app.Random.NextDouble() * 8;
            }
            // Opens his mouth "just a tad", following how loud the clip is at this moment.
            double t = now - SoundStart, open = 0;
            if (app.Sound != null && t >= 0 && t < app.Sound.Envelope.Length / 60.0)
                open = Math.Min(1, app.Sound.Envelope[(int)(t * 60)] * 1.25);
            mouthOpen += (open - mouthOpen) * (1 - Math.Exp(-dt * 25));
        }

        // Each blink is a quick close, a beat shut, and open (0.37 s in all).
        private float Closure(int eye)
        {
            double t = app.Wall.Elapsed.TotalSeconds - blinkStart - BlinkOrder[eye];
            if (t < 0) return 0;
            if (t < 0.11) return (float)(t / 0.11);
            if (t < 0.21) return 1;
            if (t < 0.37) return (float)(1 - (t - 0.21) / 0.16);
            return 0;
        }

        public void Present()
        {
            if (closed || surface == null || current == null) return;
            int x = (int)Math.Round(px), y = (int)Math.Round(py);
            byte alpha = (byte)Math.Max(0, Math.Min(255, app.Settings.Number("opacity") * 255));
            // Everything that affects the picture, rounded to what could visibly change it.
            bool watching = app.Watching;
            int g0x = watching ? (int)Math.Round(gaze[0].X * 4) : 0, g0y = watching ? (int)Math.Round(gaze[0].Y * 4) : 0;
            int g1x = watching ? (int)Math.Round(gaze[1].X * 4) : 0, g1y = watching ? (int)Math.Round(gaze[1].Y * 4) : 0;
            int lid0 = (int)Math.Round(Closure(0) * 100), lid1 = (int)Math.Round(Closure(1) * 100);
            int mouth = mouthOpen <= 0.02 ? 0 : (int)Math.Round(mouthOpen * 100);
            int squash = (int)Math.Round(Squash * 1000);
            bool same = !redraw && current == shownFrame && alpha == shownAlpha && squash == shownSquash
                && g0x == shownGaze0X && g0y == shownGaze0Y && g1x == shownGaze1X && g1y == shownGaze1Y
                && lid0 == shownLid0 && lid1 == shownLid1 && mouth == shownMouth;
            if (same)
            {
                // Same picture: at most slide the window, which needs no redraw.
                if (x != shownX || y != shownY)
                    Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);   // No size, z-order or activation change.
                shownX = x; shownY = y;
                return;
            }
            using (Graphics g = Graphics.FromImage(surface.Canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (squash == 0)
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(current, new Rectangle(0, 0, current.Width, current.Height));
                }
                else
                {
                    // Bopping: squash everything toward the bottom of his hands, which stay put.
                    g.Clear(Color.Transparent);
                    float baseY = app.PetBounds.Bottom * PixelScale;
                    g.TranslateTransform(0, baseY);
                    g.ScaleTransform(1, 1 - Squash);
                    g.TranslateTransform(0, -baseY);
                    g.DrawImage(current, new Rectangle(0, 0, current.Width, current.Height));
                }
                g.CompositingMode = CompositingMode.SourceOver;
                OnkeyRenderer.ToSprite(g, PixelScale, currentBounce);
                if (app.Watching)
                    for (int i = 0; i < 2; i++)
                    {
                        Cutout p = app.Renderer.Pupils[i];
                        float k = OnkeyRenderer.PupilScale;
                        g.DrawImage(p.Image, new RectangleF(p.Center.X + gaze[i].X - (p.Center.X - p.Rect.X) * k,
                                                            p.Center.Y + gaze[i].Y - (p.Center.Y - p.Rect.Y) * k,
                                                            p.Rect.Width * k, p.Rect.Height * k));
                    }
                for (int i = 0; i < 2; i++) Features.DrawLid(g, app.Renderer.EyeInteriors[i], Closure(i), app.Renderer.LidColor, i);
                Features.DrawMouth(g, (float)mouthOpen);
            }
            surface.Show(Handle, x, y, alpha);
            shownFrame = current; shownAlpha = alpha; shownX = x; shownY = y;
            shownGaze0X = g0x; shownGaze0Y = g0y; shownGaze1X = g1x; shownGaze1Y = g1y;
            shownLid0 = lid0; shownLid1 = lid1; shownMouth = mouth; shownSquash = squash;
            redraw = false;
        }

        // Dragging (when "Let me drag Onkey around" is ticked)

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            grabMouse = Cursor.Position; grabX = px; grabY = py; dragMoved = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button != MouseButtons.Left) return;
            Point now = Cursor.Position;
            int dx = now.X - grabMouse.X, dy = now.Y - grabMouse.Y;
            if (!dragMoved && Math.Abs(dx) + Math.Abs(dy) < 3) return;
            dragMoved = true; dragging = true;
            px = grabX + dx; py = grabY + dy;
            Present();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (!dragMoved) { app.Speak(this, true); return; }
            dragging = false;
            Area = Screen.FromPoint(Cursor.Position).WorkingArea;
            RestFor(3);
            PickTarget();
            if (app.IsLead(this)) app.SavePosition();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!closed)
            {
                closed = true;
                if (surface != null) surface.Dispose();
            }
            base.OnFormClosed(e);
        }
    }
}
