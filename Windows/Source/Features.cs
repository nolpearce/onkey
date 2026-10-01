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
}
