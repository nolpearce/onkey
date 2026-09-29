// Onkey desktop pet for Windows. Matches the Mac version in Mac/Onkey.swift: a
// transparent window that wanders the screen, with eyes that watch the cursor,
// blinks, a mouth that opens with his sound, and settings in the tray icon menu.
// Compiled at launch by Windows PowerShell's Add-Type, so this must stay C# 5.
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
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool created;
            using (Mutex single = new Mutex(true, "Local\\OnkeyDesktopPet", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Onkey is already running. Right-click his icon beside the clock for settings.", "Onkey");
                    return;
                }
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new OnkeyForm());
            }
        }
    }

    // A shape lifted from the sprite: the drawn pupils (as black ink with their soft
    // edges) and the inside of each eye outline (as a mask for the eyelids).
    public sealed class Cutout
    {
        public Rectangle Rect;          // Sprite pixels.
        public PointF Center;           // Ink-weighted centre, sprite pixels.
        public byte[] Alpha;            // Rect.Width * Rect.Height.
        public Bitmap Image;            // Black ink with Alpha, for the pupils.

        public Cutout(List<int> xs, List<int> ys, List<byte> alphas)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < xs.Count; i++)
            {
                minX = Math.Min(minX, xs[i]); maxX = Math.Max(maxX, xs[i]);
                minY = Math.Min(minY, ys[i]); maxY = Math.Max(maxY, ys[i]);
            }
            Rect = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            Alpha = new byte[Rect.Width * Rect.Height];
            double sx = 0, sy = 0, weight = 0;
            for (int i = 0; i < xs.Count; i++)
            {
                Alpha[(ys[i] - minY) * Rect.Width + (xs[i] - minX)] = alphas[i];
                sx += xs[i] * (double)alphas[i]; sy += ys[i] * (double)alphas[i]; weight += alphas[i];
            }
            Center = weight > 0 ? new PointF((float)(sx / weight), (float)(sy / weight))
                                : new PointF(minX + Rect.Width / 2f, minY + Rect.Height / 2f);
            Image = new Bitmap(Rect.Width, Rect.Height, PixelFormat.Format32bppPArgb);
            byte[] pixels = new byte[Rect.Width * Rect.Height * 4];
            for (int i = 0; i < Alpha.Length; i++) pixels[i * 4 + 3] = Alpha[i];
            Pixels.Write(Image, pixels);
        }
    }

    internal static class Pixels
    {
        // 32bpp premultiplied pixels, B G R A, rows packed without padding.
        public static byte[] Read(Bitmap bitmap)
        {
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                byte[] buffer = new byte[bitmap.Width * bitmap.Height * 4];
                for (int row = 0; row < bitmap.Height; row++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), buffer, row * bitmap.Width * 4, bitmap.Width * 4);
                return buffer;
            }
            finally { bitmap.UnlockBits(data); }
        }

        public static void Write(Bitmap bitmap, byte[] buffer)
        {
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int row = 0; row < bitmap.Height; row++)
                    Marshal.Copy(buffer, row * bitmap.Width * 4, IntPtr.Add(data.Scan0, row * data.Stride), bitmap.Width * 4);
            }
            finally { bitmap.UnlockBits(data); }
        }
    }

    // Draws Onkey from Onkey.png. Arm masks separate the arms from the ears and head
    // so each arm can swing about its shoulder. Rendering from the full-size sprite
    // keeps him sharp at every size.
    public sealed class OnkeyRenderer : IDisposable
    {
        public const int CanvasWidth = 180;
        public const int CanvasHeight = 132;
        public const int FrameCount = 40;
        public const float SourceWidth = 1774f;
        public const float SourceHeight = 887f;
        public const float PetWidth = 140f;
        public const float SpriteScale = PetWidth / SourceWidth;
        // The moving pupils are the drawn pupils shrunk slightly to leave room to look around.
        public const float PupilScale = 0.86f;
        public const float PupilTravel = 13f;

        private readonly Bitmap head, blankEyedHead, leftArm, rightArm;
        private Bitmap scaledHead, scaledBlank, scaledLeft, scaledRight;
        private float preparedScale = -1, preparedK = 1;
        public readonly Cutout[] Pupils = new Cutout[2];
        public readonly Cutout[] EyeInteriors = new Cutout[2];
        public Color LidColor = Color.FromArgb(160, 84, 51);
        private static readonly Point[] EyeSeeds = { new Point(753, 525), new Point(1056, 512) };

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
            blankEyedHead = ErasePupils(head);
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

        // Flood-fills each pupil (and its grey anti-aliased rim) with white, stopping at
        // the white ring inside the eye outline, and keeps what was removed as ink so the
        // moving pupils keep their hand-drawn edges. Then finds the inside of each eye.
        private Bitmap ErasePupils(Bitmap source)
        {
            int w = source.Width, h = source.Height;
            byte[] px = Pixels.Read(source);
            for (int e = 0; e < 2; e++)
            {
                List<int> xs = new List<int>(), ys = new List<int>();
                List<byte> alphas = new List<byte>();
                Flood(w, h, EyeSeeds[e], 75, delegate(int i)
                {
                    int a = px[i + 3], m = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                    if (a <= 100 || m >= 200) return false;
                    int lightness = Math.Min(255, m * 255 / a);
                    alphas.Add((byte)((255 - lightness) * a / 255));
                    px[i] = px[i + 1] = px[i + 2] = (byte)a;   // Premultiplied white.
                    return true;
                }, xs, ys);
                if (xs.Count < 1000) throw new InvalidDataException("Could not find Onkey's pupils in Onkey.png.");
                Pupils[e] = new Cutout(xs, ys, alphas);
            }
            // With the pupils gone, each eye's inside is one light patch bounded by the dark
            // outline (and by the muzzle's outline where the muzzle overlaps the eye).
            for (int e = 0; e < 2; e++)
            {
                List<int> xs = new List<int>(), ys = new List<int>();
                List<byte> alphas = new List<byte>();
                Flood(w, h, EyeSeeds[e], 85, delegate(int i)
                {
                    int a = px[i + 3];
                    if (a <= 100) return false;
                    int max = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                    int min = Math.Min(px[i], Math.Min(px[i + 1], px[i + 2]));
                    int lightness = max * 255 / a, greyness = (max - min) * 255 / a;
                    if (lightness < 60 || greyness >= 60) return false;
                    // Fully cover everything but the darkest edge pixels, so no pale ring shows.
                    alphas.Add((byte)Math.Min(255, (lightness - 60) * 255 / 50));
                    return true;
                }, xs, ys);
                if (xs.Count < 1000) throw new InvalidDataException("Could not find Onkey's eyes in Onkey.png.");
                EyeInteriors[e] = new Cutout(xs, ys, alphas);
            }
            // Average skin colour in a patch of forehead above the left eye.
            int r = 0, gr = 0, b = 0, n = 0;
            for (int y = 405; y < 415; y++)
                for (int x = 748; x < 758; x++)
                {
                    int i = (y * w + x) * 4;
                    if (px[i + 3] <= 250) continue;
                    b += px[i]; gr += px[i + 1]; r += px[i + 2]; n++;
                }
            if (n > 0) LidColor = Color.FromArgb(r / n, gr / n, b / n);
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            Pixels.Write(result, px);
            return result;
        }

        private delegate bool PixelTest(int byteIndex);

        private static void Flood(int w, int h, Point seed, int radius, PixelTest accept, List<int> xs, List<int> ys)
        {
            bool[] visited = new bool[w * h];
            Stack<Point> stack = new Stack<Point>();
            stack.Push(seed);
            while (stack.Count > 0)
            {
                Point p = stack.Pop();
                int dx = p.X - seed.X, dy = p.Y - seed.Y;
                if (p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h || dx * dx + dy * dy >= radius * radius) continue;
                int index = p.Y * w + p.X;
                if (visited[index]) continue;
                visited[index] = true;
                if (!accept(index * 4)) continue;
                xs.Add(p.X); ys.Add(p.Y);
                stack.Push(new Point(p.X + 1, p.Y)); stack.Push(new Point(p.X - 1, p.Y));
                stack.Push(new Point(p.X, p.Y + 1)); stack.Push(new Point(p.X, p.Y - 1));
            }
        }

        public static float Bounce(double phase, bool walking) { return walking ? (float)(2.2 * Math.Abs(Math.Sin(phase))) : 0f; }

        // Shrinks the layers once per size so each frame is drawn at close to 1:1.
        private void Prepare(float s)
        {
            if (Math.Abs(s - preparedScale) < 0.0001f) return;
            DisposeScaled();
            float k = SpriteScale * s;
            scaledHead = Shrink(head, k); scaledBlank = Shrink(blankEyedHead, k);
            scaledLeft = Shrink(leftArm, k); scaledRight = Shrink(rightArm, k);
            preparedScale = s; preparedK = k;
        }

        private static Bitmap Shrink(Bitmap image, float k)
        {
            Bitmap small = new Bitmap(Math.Max(1, (int)Math.Ceiling(image.Width * k)), Math.Max(1, (int)Math.Ceiling(image.Height * k)), PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(image, new RectangleF(0, 0, image.Width * k, image.Height * k));
            }
            return small;
        }

        // Canvas (points, y-down) transform: scale s, then sprite pixels with the head's bounce.
        public static void ToSprite(Graphics g, float s, float bounce)
        {
            g.ScaleTransform(s, s);
            g.TranslateTransform((CanvasWidth - PetWidth) / 2f, 26f - bounce);
            g.ScaleTransform(SpriteScale, SpriteScale);
        }

        public static PointF CanvasPoint(PointF sprite, float bounce)
        {
            return new PointF((CanvasWidth - PetWidth) / 2f + sprite.X * SpriteScale, 26f - bounce + sprite.Y * SpriteScale);
        }

        public Bitmap Render(double phase, bool walking, float s, bool blankEyes)
        {
            Prepare(s);
            Bitmap result = new Bitmap((int)Math.Round(CanvasWidth * s), (int)Math.Round(CanvasHeight * s), PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                ToSprite(g, s, Bounce(phase, walking));
                // Alternating planted and lifted hands propel the head forward.
                float swing = walking ? (float)(18 * Math.Sin(phase)) : 0f;
                DrawArm(g, scaledLeft, new PointF(633, 808), -16f + swing, preparedK);
                DrawArm(g, scaledRight, new PointF(1147, 808), 16f + swing, preparedK);
                // Cover each rotating joint with a small patch of matching arm colour.
                using (Brush joint = new SolidBrush(Color.FromArgb(157, 87, 47)))
                {
                    g.FillEllipse(joint, 612, 782, 60, 47);
                    g.FillEllipse(joint, 1112, 782, 60, 47);
                }
                DrawLayer(g, blankEyes ? scaledBlank : scaledHead, preparedK);
            }
            return result;
        }

        // Draws a layer shrunk by k back at sprite-pixel coordinates (so at about 1:1 on screen).
        private static void DrawLayer(Graphics g, Bitmap layer, float k)
        {
            g.DrawImage(layer, new RectangleF(0, 0, layer.Width / k, layer.Height / k));
        }

        private static void DrawArm(Graphics g, Bitmap arm, PointF pivot, float angle, float k)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(pivot.X, pivot.Y);
            g.RotateTransform(angle);
            g.TranslateTransform(-pivot.X, -pivot.Y);
            DrawLayer(g, arm, k);
            g.Restore(state);
        }

        // Opaque area of a frame, in canvas points.
        public static RectangleF OpaqueBounds(Bitmap frame, float s)
        {
            byte[] px = Pixels.Read(frame);
            int w = frame.Width, h = frame.Height, minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[(y * w + x) * 4 + 3] > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        maxY = y;
                    }
            if (maxX < 0) return new RectangleF(0, 0, CanvasWidth, CanvasHeight);
            return new RectangleF(minX / s, minY / s, (maxX - minX + 1) / s, (maxY - minY + 1) / s);
        }

        private void DisposeScaled()
        {
            foreach (Bitmap b in new Bitmap[] { scaledHead, scaledBlank, scaledLeft, scaledRight }) if (b != null) b.Dispose();
            scaledHead = scaledBlank = scaledLeft = scaledRight = null;
            preparedScale = -1;
        }

        public void Dispose()
        {
            DisposeScaled();
            head.Dispose(); blankEyedHead.Dispose(); leftArm.Dispose(); rightArm.Dispose();
            foreach (Cutout c in Pupils) if (c != null) c.Image.Dispose();
            foreach (Cutout c in EyeInteriors) if (c != null) c.Image.Dispose();
        }
    }

    // Eyelids, lash lines and the mouth, drawn in sprite pixels (y-down) over each frame.
    internal static class Features
    {
        private static readonly Color Ink = Color.FromArgb(41, 20, 13);

        // Half the width of a round eye filling w x h, at height y.
        private static float Chord(float y, float w, float h)
        {
            float r = Math.Min(w, h) / 2, dy = y - h / 2;
            return Math.Abs(dy) >= r ? 0 : (float)Math.Sqrt(r * r - dy * dy);
        }

        // closure: 0 open ... 1 shut. The lid is clipped to the inside of the eye outline so
        // it never paints over the outline or the muzzle; the ink lines are not.
        public static void DrawLid(Graphics g, Cutout eye, float c, Color skin, int index)
        {
            if (c <= 0.01f) return;
            int w = eye.Rect.Width, h = eye.Rect.Height;
            float sag = h * 0.14f;
            float edge = h * c * 1.15f - sag * c;   // The lid's lower edge, sagging in the middle.
            using (Bitmap lid = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (Graphics lg = Graphics.FromImage(lid))
                using (GraphicsPath path = new GraphicsPath())
                using (Brush brush = new SolidBrush(skin))
                {
                    lg.Clear(Color.Transparent);
                    lg.SmoothingMode = SmoothingMode.AntiAlias;
                    path.AddLine(-2, -2, w + 2, -2);
                    path.AddLine(w + 2, -2, w + 2, edge);
                    AddQuad(path, new PointF(w + 2, edge), new PointF(w / 2f, edge + 2 * sag), new PointF(-2, edge));
                    path.CloseFigure();
                    lg.FillPath(brush, path);
                }
                byte[] px = Pixels.Read(lid);
                for (int i = 0; i < eye.Alpha.Length; i++)
                {
                    int m = eye.Alpha[i];
                    for (int ch = 0; ch < 4; ch++) px[i * 4 + ch] = (byte)(px[i * 4 + ch] * m / 255);
                }
                Pixels.Write(lid, px);
                g.DrawImage(lid, new RectangleF(eye.Rect.X, eye.Rect.Y, w, h));
            }
            // The lash line follows the lid down, resting a little below the middle when shut.
            // It spans the eye's width at that height, overlapping the outline a touch.
            float line = Math.Min(edge, h * 0.56f);
            float half = Chord(line, w, h) + w * 0.03f;
            float ox = eye.Rect.X, oy = eye.Rect.Y;
            using (GraphicsPath lash = InkStroke(new PointF(ox + w / 2f - half, oy + line), new PointF(ox + w / 2f + half, oy + line),
                                                 sag, 14, 1.6f, index * 3.1))
            using (Brush ink = new SolidBrush(Ink))
                g.FillPath(ink, lash);
            // A faint crease above the shut lid.
            float crease = line - h * 0.2f;
            float creaseHalf = Chord(crease, w, h) * 0.55f;
            int alpha = (int)(Math.Max(0, (c - 0.6f) / 0.4f) * 0.55f * 255);
            if (alpha > 0 && creaseHalf > 1)
                using (GraphicsPath path = InkStroke(new PointF(ox + w / 2f - creaseHalf, oy + crease), new PointF(ox + w / 2f + creaseHalf, oy + crease),
                                                     sag * 0.6f, 5.5f, 0.8f, index * 3.1 + 7))
                using (Brush faint = new SolidBrush(Color.FromArgb(alpha, Ink)))
                    g.FillPath(faint, path);
        }

        // A brush-like ink line along a sagging curve: thick in the middle, tapering to
        // fine points, with a fixed wobble so it looks hand-drawn rather than ruled.
        private static GraphicsPath InkStroke(PointF a, PointF b, float sag, float width, float wobble, double seed)
        {
            PointF control = new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2 + 2 * sag);
            const int steps = 28;
            PointF[] outline = new PointF[(steps + 1) * 2];
            for (int n = 0; n <= steps; n++)
            {
                float t = n / (float)steps, u = 1 - t;
                PointF p = new PointF(u * u * a.X + 2 * u * t * control.X + t * t * b.X, u * u * a.Y + 2 * u * t * control.Y + t * t * b.Y);
                PointF d = new PointF(2 * u * (control.X - a.X) + 2 * t * (b.X - control.X), 2 * u * (control.Y - a.Y) + 2 * t * (b.Y - control.Y));
                float len = Math.Max(0.0001f, (float)Math.Sqrt(d.X * d.X + d.Y * d.Y));
                PointF normal = new PointF(-d.Y / len, d.X / len);
                float jitter = wobble * (float)(Math.Sin(t * 11 + seed) * 0.6 + Math.Sin(t * 23 + seed * 1.7) * 0.4);
                float halfWidth = width / 2 * (float)Math.Pow(Math.Sin(t * Math.PI), 0.7) * (float)(0.85 + 0.15 * Math.Sin(t * 17 + seed));
                outline[n] = new PointF(p.X + normal.X * (jitter + halfWidth), p.Y + normal.Y * (jitter + halfWidth));
                outline[outline.Length - 1 - n] = new PointF(p.X + normal.X * (jitter - halfWidth), p.Y + normal.Y * (jitter - halfWidth));
            }
            GraphicsPath path = new GraphicsPath();
            path.AddPolygon(outline);
            return path;
        }

        // A small open mouth over the seam between his lips: flat-topped and round-bottomed
        // with a red tongue, as in the cartoon. open: 0 shut ... 1 fully open.
        public static void DrawMouth(Graphics g, float open)
        {
            if (open <= 0.02f) return;
            // Seam between the lips in Onkey.png: lowest at (900, 701), rising to the sides.
            float depth = 52 * open, width = 120 + 50 * open;
            using (GraphicsPath hole = MouthShape(900, 696, width, depth))
            using (Brush dark = new SolidBrush(Color.FromArgb(13, 8, 8)))
                g.FillPath(dark, hole);
            if (depth < 16) return;
            using (GraphicsPath tongue = MouthShape(900, 703, width - 26, depth * 0.5f))
            using (Brush red = new SolidBrush(Color.FromArgb(199, 41, 41)))
                g.FillPath(red, tongue);
        }

        private static GraphicsPath MouthShape(float cx, float top, float w, float depth)
        {
            PointF left = new PointF(cx - w / 2, top), right = new PointF(cx + w / 2, top);
            PointF bottom = new PointF(cx, top + 5 + depth);
            GraphicsPath path = new GraphicsPath();
            AddQuad(path, left, new PointF(cx, top + 10), right);
            path.AddBezier(right, new PointF(right.X, top + depth * 0.9f), new PointF(cx + w * 0.28f, bottom.Y), bottom);
            path.AddBezier(bottom, new PointF(cx - w * 0.28f, bottom.Y), new PointF(left.X, top + depth * 0.9f), left);
            path.CloseFigure();
            return path;
        }

        private static void AddQuad(GraphicsPath path, PointF p0, PointF control, PointF p2)
        {
            path.AddBezier(p0, new PointF(p0.X + 2f / 3 * (control.X - p0.X), p0.Y + 2f / 3 * (control.Y - p0.Y)),
                           new PointF(p2.X + 2f / 3 * (control.X - p2.X), p2.Y + 2f / 3 * (control.Y - p2.Y)), p2);
        }
    }

    // Settings kept in %APPDATA%\Onkey\settings.txt as key=value lines.
    internal sealed class Settings
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();
        private readonly string path;

        public Settings()
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Onkey", "settings.txt");
            string[] defaults = {
                "zone=anywhere", "chase=5", "speed=42", "soundOn=true", "soundGap=90", "volume=1",
                "size=1", "opacity=1", "layer=above", "draggable=false", "watchCursor=true", "blink=true" };
            foreach (string line in defaults) Parse(line);
            try { if (File.Exists(path)) foreach (string line in File.ReadAllLines(path)) Parse(line); }
            catch { /* Unreadable settings just mean the defaults. */ }
        }

        private void Parse(string line)
        {
            int eq = line.IndexOf('=');
            if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }

        public string Get(string key) { string v; return values.TryGetValue(key, out v) ? v : ""; }
        public bool Has(string key) { return values.ContainsKey(key); }
        public bool Bool(string key) { return Get(key) == "true"; }
        public double Number(string key)
        {
            double v;
            return double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        public void Set(string key, string value) { values[key] = value; Save(); }
        public void Set(string key, double value) { Set(key, value.ToString("R", CultureInfo.InvariantCulture)); }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> pair in values) lines.Add(pair.Key + "=" + pair.Value);
                File.WriteAllLines(path, lines.ToArray());
            }
            catch { /* Settings that can't be saved still apply until Onkey exits. */ }
        }
    }

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

    public sealed class OnkeyForm : Form
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // Delay per eye, [left, right] on screen: the left blinks first, the right close behind.
        private static readonly double[] BlinkOrder = { 0, 0.14 };

        private readonly Random random = new Random();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch wall = Stopwatch.StartNew();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Settings settings = new Settings();
        private readonly string folder;
        private readonly OnkeyRenderer renderer;
        private readonly OnkeySound sound;
        private readonly List<ToolStripMenuItem> optionItems = new List<ToolStripMenuItem>();
        private readonly List<ToolStripMenuItem> soundOptionItems = new List<ToolStripMenuItem>();
        private ToolStripMenuItem pauseItem, loginItem;
        private Bitmap[] frames = new Bitmap[0];
        private float[] frameBounce = new float[0];
        private Bitmap idle, work, current;
        private float currentBounce;
        private RectangleF petBounds = new RectangleF(0, 0, OnkeyRenderer.CanvasWidth, OnkeyRenderer.CanvasHeight);
        private LayeredSurface surface;
        private Rectangle area;
        private float dpi = 1;
        private double px, py, targetX, targetY, phase, clock, lastTick, restUntil, nextSound;
        private double blinkStart = -100, nextBlink = 3, soundStart = double.NegativeInfinity, mouthOpen;
        private readonly PointF[] gaze = new PointF[2];
        private bool paused, dragging, dragMoved, disposed;
        private Point grabMouse;
        private double grabX, grabY;

        private float PixelScale { get { return (float)settings.Number("size") * dpi; } }   // Pixels per canvas point.
        private bool Watching { get { return settings.Bool("watchCursor"); } }

        public OnkeyForm()
        {
            folder = Environment.GetEnvironmentVariable("ONKEY_ASSET_DIR") ?? AppDomain.CurrentDomain.BaseDirectory;
            renderer = new OnkeyRenderer(Path.Combine(folder, "Onkey.png"));
            string soundPath = Path.Combine(folder, Path.Combine("Sounds", "oooo.wav"));
            if (File.Exists(soundPath)) { try { sound = new OnkeySound(soundPath); } catch { sound = null; } }
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96f;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            RenderFrames();

            area = Screen.FromPoint(Cursor.Position).WorkingArea;
            px = area.Right - work.Width - 60;
            py = area.Bottom - work.Height - 40;
            if (settings.Has("savedX"))
            {
                Point saved = new Point((int)settings.Number("savedX"), (int)settings.Number("savedY"));
                foreach (Screen screen in Screen.AllScreens)
                {
                    Rectangle near = screen.WorkingArea;
                    near.Inflate(200, 200);
                    if (near.Contains(saved)) { area = screen.WorkingArea; px = saved.X; py = saved.Y; break; }
                }
            }
            ClampToArea();
            Location = new Point((int)px, (int)py);
            ClientSize = new System.Drawing.Size(work.Width, work.Height);
            PickTarget();
            nextSound = SoundDelay();
            BuildMenu();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            timer.Interval = 33;
            timer.Tick += delegate { Tick(); };
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
                if (!settings.Bool("draggable")) cp.ExStyle |= 0x00000020;
                return cp;
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyAppearance();
            current = idle;
            Present();
            tray.Visible = true;
            lastTick = wall.Elapsed.TotalSeconds;
            timer.Start();
        }

        // Frames and window

        private void RenderFrames()
        {
            float s = PixelScale;
            bool blank = Watching;
            Bitmap[] rendered = new Bitmap[OnkeyRenderer.FrameCount];
            float[] bounce = new float[OnkeyRenderer.FrameCount];
            RectangleF bounds = RectangleF.Empty;
            for (int i = 0; i < rendered.Length; i++)
            {
                double p = i * 2 * Math.PI / rendered.Length;
                rendered[i] = renderer.Render(p, true, s, blank);
                bounce[i] = OnkeyRenderer.Bounce(p, true);
                RectangleF b = OnkeyRenderer.OpaqueBounds(rendered[i], s);
                bounds = bounds.IsEmpty ? b : RectangleF.Union(bounds, b);
            }
            Bitmap still = renderer.Render(0, false, s, blank);
            bounds = RectangleF.Union(bounds, OnkeyRenderer.OpaqueBounds(still, s));
            foreach (Bitmap f in frames) f.Dispose();
            if (idle != null) idle.Dispose();
            if (work != null) work.Dispose();
            frames = rendered; frameBounce = bounce; idle = still; petBounds = bounds;
            work = new Bitmap(still.Width, still.Height, PixelFormat.Format32bppPArgb);
            current = idle; currentBounce = 0;
            if (surface != null) { surface.Dispose(); surface = null; }
            if (IsHandleCreated) surface = new LayeredSurface(work.Width, work.Height);
        }

        private Icon MakeTrayIcon()
        {
            using (Bitmap face = renderer.Render(0, false, 1, false))
            using (Bitmap icon = new Bitmap(32, 32, PixelFormat.Format32bppPArgb))
            {
                RectangleF b = OnkeyRenderer.OpaqueBounds(face, 1);
                // Crop to the head (the top of the opaque area) so he's recognisable at 16px.
                RectangleF headArea = new RectangleF(b.X + b.Width * 0.22f, b.Y, b.Width * 0.56f, b.Height * 0.72f);
                using (Graphics g = Graphics.FromImage(icon))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float scale = Math.Min(32 / headArea.Width, 32 / headArea.Height);
                    float w = headArea.Width * scale, h = headArea.Height * scale;
                    g.DrawImage(face, new RectangleF((32 - w) / 2, (32 - h) / 2, w, h), headArea, GraphicsUnit.Pixel);
                }
                return Icon.FromHandle(icon.GetHicon());
            }
        }

        // Menu

        private void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            pauseItem = Item("Pause Onkey", delegate { TogglePause(); });
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(Submenu("Where Onkey goes",
                Option("Anywhere on the screen", "zone", "anywhere"),
                Option("Along the bottom", "zone", "bottom"),
                Option("Along the top", "zone", "top"),
                Option("Up and down the left side", "zone", "left"),
                Option("Up and down the right side", "zone", "right"),
                Option("Stay in one spot", "zone", "stay"),
                new ToolStripSeparator(),
                Header("Walks toward my mouse"),
                Option("Never", "chase", "0"),
                Option("Sometimes", "chase", "5"),
                Option("Often", "chase", "2"),
                Option("Always", "chase", "1"),
                new ToolStripSeparator(),
                Header("Walking speed"),
                Option("Slow", "speed", "22"),
                Option("Normal", "speed", "42"),
                Option("Fast", "speed", "80"),
                Option("Zoomies", "speed", "160"),
                new ToolStripSeparator(),
                Item("Bring Onkey to this screen", delegate { BringHere(); })));

            ToolStripMenuItem playNow = Item("Play sound now", delegate { PlaySound(true); });
            ToolStripItem[] soundOptions = {
                Header("How often"),
                Option("Every 20-40 seconds", "soundGap", "20"),
                Option("Every 1\u00BD-3 minutes", "soundGap", "90"),
                Option("Every 5-10 minutes", "soundGap", "300"),
                new ToolStripSeparator(),
                Header("Volume"),
                Option("Quiet", "volume", "0.25"),
                Option("Medium", "volume", "0.6"),
                Option("Loud", "volume", "1") };
            foreach (ToolStripItem i in soundOptions) { ToolStripMenuItem m = i as ToolStripMenuItem; if (m != null && m.Tag != null) soundOptionItems.Add(m); }
            soundOptionItems.Add(playNow);
            List<ToolStripItem> soundMenu = new List<ToolStripItem>();
            soundMenu.Add(Option(sound == null ? "Sound clip not installed" : "Sound on", "soundOn", "true"));
            soundMenu.Add(new ToolStripSeparator());
            soundMenu.AddRange(soundOptions);
            soundMenu.Add(new ToolStripSeparator());
            soundMenu.Add(playNow);
            menu.Items.Add(Submenu("Sound", soundMenu.ToArray()));

            menu.Items.Add(Submenu("Appearance",
                Header("Size"),
                Option("Tiny", "size", "0.5"),
                Option("Small", "size", "0.75"),
                Option("Normal", "size", "1"),
                Option("Large", "size", "1.5"),
                Option("Huge", "size", "2.25"),
                new ToolStripSeparator(),
                Header("Opacity"),
                Option("Solid", "opacity", "1"),
                Option("See-through", "opacity", "0.7"),
                Option("Ghost", "opacity", "0.35"),
                new ToolStripSeparator(),
                Header("Layer"),
                Option("In front of all windows", "layer", "above"),
                Option("On the desktop, behind windows", "layer", "desktop"),
                new ToolStripSeparator(),
                Header("Eyes"),
                Option("Watch my cursor", "watchCursor", "true"),
                Option("Blink now and then", "blink", "true")));

            menu.Items.Add(Option("Let me drag Onkey around", "draggable", "true"));
            menu.Items.Add(new ToolStripSeparator());
            loginItem = Item("Open Onkey when Windows starts", delegate { ToggleLogin(); });
            menu.Items.Add(loginItem);
            menu.Items.Add(Item("Exit Onkey", delegate { Close(); }));

            tray.Icon = MakeTrayIcon();
            tray.Text = "Onkey - right-click for settings";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { TogglePause(); };
            RefreshMenu();
        }

        private static ToolStripMenuItem Item(string text, EventHandler click)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += click;
            return item;
        }

        private static ToolStripMenuItem Header(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Enabled = false;
            return item;
        }

        private static ToolStripMenuItem Submenu(string text, params ToolStripItem[] items)
        {
            ToolStripMenuItem parent = new ToolStripMenuItem(text);
            parent.DropDownItems.AddRange(items);
            // Keep the menu open while picking options, like a settings panel.
            parent.DropDown.Closing += delegate(object s, ToolStripDropDownClosingEventArgs e)
            {
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
            };
            return parent;
        }

        // A setting choice. "true" options toggle; others act as radio buttons.
        private ToolStripMenuItem Option(string text, string key, string value)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = new string[] { key, value };
            item.Click += delegate { Choose(key, value); };
            optionItems.Add(item);
            return item;
        }

        private void Choose(string key, string value)
        {
            if (value == "true") settings.Set(key, settings.Bool(key) ? "false" : "true");
            else settings.Set(key, value);
            switch (key)
            {
                case "size":
                    double cx = px + work.Width / 2.0, cy = py + work.Height / 2.0;
                    RenderFrames();
                    px = cx - work.Width / 2.0; py = cy - work.Height / 2.0;
                    ClampToArea();
                    PickTarget();
                    break;
                case "watchCursor":
                    RenderFrames();
                    break;
                case "opacity": case "layer": case "draggable":
                    ApplyAppearance();
                    break;
                case "zone": case "chase":
                    restUntil = 0;
                    PickTarget();
                    break;
                case "soundGap":
                    nextSound = clock + SoundDelay();
                    break;
                case "soundOn":
                    if (!settings.Bool("soundOn")) StopSound();
                    break;
            }
            RefreshMenu();
            Present();
        }

        private void RefreshMenu()
        {
            foreach (ToolStripMenuItem item in optionItems)
            {
                string[] tag = (string[])item.Tag;
                item.Checked = tag[1] == "true" ? settings.Bool(tag[0]) : settings.Get(tag[0]) == tag[1];
            }
            bool soundOn = sound != null && settings.Bool("soundOn");
            foreach (ToolStripMenuItem item in soundOptionItems) item.Enabled = soundOn;
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey))
                    loginItem.Checked = run != null && run.GetValue("Onkey") != null;
            }
            catch { loginItem.Enabled = false; }
        }

        private void ApplyAppearance()
        {
            TopMost = settings.Get("layer") != "desktop";
            // Switching click-through on or off needs the window style updated in place.
            if (IsHandleCreated)
            {
                int style = Native.GetWindowLong(Handle, -20);
                style = settings.Bool("draggable") ? style & ~0x20 : style | 0x20;
                Native.SetWindowLong(Handle, -20, style);
                if (surface == null) surface = new LayeredSurface(work.Width, work.Height);
            }
        }

        // Movement

        // Allowed window positions, letting the transparent canvas margin hang off-screen
        // so Onkey's hands can touch the very edge.
        private void Limits(out double minX, out double maxX, out double minY, out double maxY)
        {
            float s = PixelScale;
            minX = area.Left - petBounds.Left * s;
            maxX = Math.Max(minX, area.Right - petBounds.Right * s);
            minY = area.Top - petBounds.Top * s;
            maxY = Math.Max(minY, area.Bottom - petBounds.Bottom * s);
        }

        private void ClampToArea()
        {
            double minX, maxX, minY, maxY;
            Limits(out minX, out maxX, out minY, out maxY);
            px = Math.Min(maxX, Math.Max(minX, px));
            py = Math.Min(maxY, Math.Max(minY, py));
        }

        private void PickTarget()
        {
            double minX, maxX, minY, maxY;
            Limits(out minX, out maxX, out minY, out maxY);
            string zone = settings.Get("zone");
            if (zone == "stay") { targetX = px; targetY = py; return; }
            double x = minX + random.NextDouble() * (maxX - minX), y = minY + random.NextDouble() * (maxY - minY);
            int chase = (int)settings.Number("chase");
            Point mouse = Cursor.Position;
            if (chase > 0 && random.Next(chase) == 0 && area.Contains(mouse))
            {
                x = Math.Min(maxX, Math.Max(minX, mouse.X - work.Width / 2.0));
                y = Math.Min(maxY, Math.Max(minY, mouse.Y - work.Height / 2.0));
            }
            if (zone == "bottom") y = maxY;
            else if (zone == "top") y = minY;
            else if (zone == "left") x = minX;
            else if (zone == "right") x = maxX;
            targetX = x; targetY = y;
        }

        private void Tick()
        {
            double now = wall.Elapsed.TotalSeconds;
            double dt = Math.Min(0.08, Math.Max(0, now - lastTick));
            lastTick = now;
            if (!paused && !dragging) Walk(dt);
            UpdateFace(now, dt);
            Present();
            if (settings.Get("layer") == "desktop")
                Native.SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // HWND_BOTTOM, no size/move/activate.
        }

        private void Walk(double dt)
        {
            clock += dt;
            if (clock >= nextSound) { PlaySound(false); nextSound = clock + SoundDelay(); }
            current = idle; currentBounce = 0;
            if (clock < restUntil || settings.Get("zone") == "stay") return;
            double dx = targetX - px, dy = targetY - py;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 3)
            {
                restUntil = clock + 2 + random.NextDouble() * 4;
                PickTarget();
                return;
            }
            double speed = settings.Number("speed") * PixelScale;
            double step = Math.Min(distance, speed * dt);
            px += step * dx / distance;
            py += step * dy / distance;
            // Faster walking means faster arms, so he never looks like he's skating.
            phase = (phase + dt * 2 * Math.PI * 1.15 * speed / (42 * PixelScale)) % (2 * Math.PI);
            int index = ((int)(phase / (2 * Math.PI) * frames.Length)) % frames.Length;
            current = frames[index]; currentBounce = frameBounce[index];
        }

        // Eyes, eyelids and mouth

        private void UpdateFace(double now, double dt)
        {
            // Each pupil eases toward the cursor, so he goes a bit cross-eyed when it's close.
            if (Watching)
            {
                Point mouse = Cursor.Position;
                float s = PixelScale, ease = (float)(1 - Math.Exp(-dt * 14));
                for (int i = 0; i < 2; i++)
                {
                    PointF c = OnkeyRenderer.CanvasPoint(renderer.Pupils[i].Center, currentBounce);
                    double dx = mouse.X - (Math.Round(px) + c.X * s), dy = mouse.Y - (Math.Round(py) + c.Y * s);
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    // Full travel once the cursor is a few eye-widths away; centred when it's on the eye.
                    double reach = OnkeyRenderer.PupilTravel * Math.Min(1, distance / (25 * s));
                    PointF target = distance < 0.5 ? PointF.Empty : new PointF((float)(dx / distance * reach), (float)(dy / distance * reach));
                    gaze[i] = new PointF(gaze[i].X + (target.X - gaze[i].X) * ease, gaze[i].Y + (target.Y - gaze[i].Y) * ease);
                }
            }
            // Blinks every 6-14 seconds.
            if (settings.Bool("blink") && now >= nextBlink)
            {
                blinkStart = now;
                nextBlink = now + 6 + random.NextDouble() * 8;
            }
            // Opens his mouth "just a tad", following how loud the clip is at this moment.
            double t = now - soundStart, target2 = 0;
            if (sound != null && t >= 0 && t < sound.Envelope.Length / 60.0)
                target2 = Math.Min(1, sound.Envelope[(int)(t * 60)] * 1.25);
            mouthOpen += (target2 - mouthOpen) * (1 - Math.Exp(-dt * 25));
        }

        // Each blink is a quick close, a beat shut, and open (0.37 s in all).
        private float Closure(int eye)
        {
            double t = wall.Elapsed.TotalSeconds - blinkStart - BlinkOrder[eye];
            if (t < 0) return 0;
            if (t < 0.11) return (float)(t / 0.11);
            if (t < 0.21) return 1;
            if (t < 0.37) return (float)(1 - (t - 0.21) / 0.16);
            return 0;
        }

        private void Present()
        {
            if (surface == null || current == null) return;
            using (Graphics g = Graphics.FromImage(work))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(current, new Rectangle(0, 0, current.Width, current.Height));
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                OnkeyRenderer.ToSprite(g, PixelScale, currentBounce);
                if (Watching)
                    for (int i = 0; i < 2; i++)
                    {
                        Cutout p = renderer.Pupils[i];
                        float k = OnkeyRenderer.PupilScale;
                        g.DrawImage(p.Image, new RectangleF(p.Center.X + gaze[i].X - (p.Center.X - p.Rect.X) * k,
                                                            p.Center.Y + gaze[i].Y - (p.Center.Y - p.Rect.Y) * k,
                                                            p.Rect.Width * k, p.Rect.Height * k));
                    }
                for (int i = 0; i < 2; i++) Features.DrawLid(g, renderer.EyeInteriors[i], Closure(i), renderer.LidColor, i);
                Features.DrawMouth(g, (float)mouthOpen);
            }
            byte alpha = (byte)Math.Max(0, Math.Min(255, settings.Number("opacity") * 255));
            surface.Show(Handle, work, (int)Math.Round(px), (int)Math.Round(py), alpha);
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                area = Screen.FromPoint(new Point((int)px + work.Width / 2, (int)py + work.Height / 2)).WorkingArea;
                ClampToArea();
                PickTarget();
            });
        }

        private void SavePosition()
        {
            settings.Set("savedX", px);
            settings.Set("savedY", py);
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
            if (!dragMoved) { PlaySound(true); return; }
            dragging = false;
            area = Screen.FromPoint(Cursor.Position).WorkingArea;
            restUntil = clock + 3;
            PickTarget();
            SavePosition();
        }

        // Sound

        private double SoundDelay()
        {
            double gap = settings.Number("soundGap");
            return gap + random.NextDouble() * gap;
        }

        private void PlaySound(bool force)
        {
            if (sound == null || (!force && !settings.Bool("soundOn"))) return;
            try
            {
                sound.Play(settings.Number("volume"));
                soundStart = wall.Elapsed.TotalSeconds;
            }
            catch { /* An invalid or unavailable clip must not interrupt the pet. */ }
        }

        private void StopSound()
        {
            if (sound != null) sound.Stop();
            soundStart = double.NegativeInfinity;
        }

        // Actions

        private void TogglePause()
        {
            paused = !paused;
            pauseItem.Text = paused ? "Resume Onkey" : "Pause Onkey";
            if (paused) { StopSound(); current = idle; currentBounce = 0; }
        }

        private void BringHere()
        {
            area = Screen.FromPoint(Cursor.Position).WorkingArea;
            px = area.Left + (area.Width - work.Width) / 2.0;
            py = area.Top + (area.Height - work.Height) / 2.0;
            ClampToArea();
            restUntil = clock + 2;
            PickTarget();
            SavePosition();
        }

        private void ToggleLogin()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (run.GetValue("Onkey") != null) run.DeleteValue("Onkey");
                    else run.SetValue("Onkey", "wscript.exe \"" + Path.Combine(folder, "Start Onkey.vbs") + "\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't change the startup setting: " + ex.Message, "Onkey");
            }
            RefreshMenu();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!disposed)
            {
                disposed = true;
                SavePosition();
                SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
                timer.Stop(); timer.Dispose();
                tray.Visible = false;
                if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                if (tray.Icon != null) Native.DestroyIcon(tray.Icon.Handle);
                tray.Dispose();
                if (sound != null) sound.Dispose();
                if (surface != null) surface.Dispose();
                foreach (Bitmap f in frames) f.Dispose();
                if (idle != null) idle.Dispose();
                if (work != null) work.Dispose();
                renderer.Dispose();
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
        public void Show(IntPtr window, Bitmap frame, int x, int y, byte opacity)
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
            blend.Alpha = opacity; blend.Format = 1;
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
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDC, ref Point position, ref Size size, IntPtr sourceDC, ref Point source, uint key, ref Blend blend, uint flags);
    }
}
