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
}
