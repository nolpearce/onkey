using System;
using System.Collections.Generic;
using System.Drawing;

namespace OnkeyDesktopPet
{
    // Onkey's arms as a rig: each one is a soft noodle from his shoulder to his wrist, with the
    // hand from the drawing on the end. Everything here is in sprite pixels (y-down), and
    // Mac/Sources/Rig.swift mirrors it line for line so both look the same.

    // Where an arm sits in Onkey.png and how thick it's drawn there, measured from the sprite.
    public sealed class ArmSpec
    {
        public double Out;                    // -1 for his left arm (reaching left), +1 for the right.
        public PointF Root;                   // Inside the body, where the arm starts.
        public double Cut;                    // Distance from the root to the body's edge.
        public PointF Wrist;                  // Where the hand from the drawing joins the arm.
        public double Bottom, Top;            // Half thickness below and above the middle of the arm.
        public double FlareSize, FlareAt, FlareFade;   // The armpit curve into the body.
        public double EndFlareSize, EndFlareFade;      // The arm widening into the hand.
        public double MarkAt, MarkOffset;              // The little hair mark.
        public Rectangle HandBox;             // The hand in Onkey.png.

        public double Length { get { return Math.Abs(Wrist.X - Root.X); } }
        public double RestAngle { get { return Out > 0 ? 0 : Math.PI; } }

        public static readonly ArmSpec Left = new ArmSpec
        {
            Out = -1, Root = new PointF(640, 804.5f), Cut = 34, Wrist = new PointF(250, 804.5f), Bottom = 31.5, Top = 31.5,
            FlareSize = 29, FlareAt = 34, FlareFade = 55, EndFlareSize = 3, EndFlareFade = 18, MarkAt = 42, MarkOffset = -4.5,
            HandBox = new Rectangle(0, 600, 262, 287)
        };
        public static readonly ArmSpec Right = new ArmSpec
        {
            Out = 1, Root = new PointF(1134, 804), Cut = 16, Wrist = new PointF(1530, 804), Bottom = 29, Top = 29,
            FlareSize = 19, FlareAt = 26, FlareFade = 45, EndFlareSize = 6, EndFlareFade = 18, MarkAt = 363, MarkOffset = -7,
            HandBox = new Rectangle(1518, 600, 256, 287)
        };
        public static readonly ArmSpec[] Both = { Left, Right };

        // What's cut out of the drawing to make room for each arm (the rest is his body).
        public static readonly PointF[] LeftCut = { new PointF(0, 600), new PointF(380, 600), new PointF(380, 703),
            new PointF(606, 703), new PointF(606, 887), new PointF(0, 887) };
        public static readonly PointF[] RightCut = { new PointF(1774, 600), new PointF(1394, 600), new PointF(1394, 713),
            new PointF(1150, 713), new PointF(1150, 887), new PointF(1774, 887) };
    }

    // Where the wrist is and which way the hand points (radians, y-down).
    public struct ArmPose
    {
        public double X, Y, Angle;

        public ArmPose(double x, double y, double angle) { X = x; Y = y; Angle = angle; }

        public static ArmPose Rest(ArmSpec spec) { return new ArmPose(spec.Wrist.X, spec.Wrist.Y, spec.RestAngle); }

        public ArmPose Mix(ArmPose other, double t)
        {
            double turn = (other.Angle - Angle) % (2 * Math.PI);
            if (turn > Math.PI) turn -= 2 * Math.PI; else if (turn < -Math.PI) turn += 2 * Math.PI;
            return new ArmPose(X + (other.X - X) * t, Y + (other.Y - Y) * t, Angle + turn * t);
        }

        public bool Same(ArmPose o) { return X == o.X && Y == o.Y && Angle == o.Angle; }
    }

    // An arm ready to draw: the brown inside, its two ink outlines, and the hair mark.
    public sealed class ArmShape
    {
        public PointF[] Fill;
        public PointF[][] Ink;
        public PointF MarkFrom, MarkTo;
    }

    public static class ArmGeometry
    {
        public const double InkWidth = 5;   // Outline width in the drawing.
        private const int Samples = 40;

        // The arch a too-long arm makes: flat where it leaves the body, rounder toward the hand.
        private static double Bump(double t)
        {
            double s = Math.Sin(Math.PI * t);
            return t < 0.5 ? s * s : Math.Pow(Math.Max(0, s), 1.4);
        }

        private static double Length(double[] xs, double[] ys)
        {
            double total = 0;
            for (int i = 1; i < xs.Length; i++) total += Hypot(xs[i] - xs[i - 1], ys[i] - ys[i - 1]);
            return total;
        }

        private static double Hypot(double x, double y) { return Math.Sqrt(x * x + y * y); }

        // The middle of the arm from root to wrist. It leaves the body level and arrives along the
        // hand. Short of room, it arches like an elbow; with too much, it stretches thinner.
        public static void Centerline(ArmSpec spec, double rx, double ry, ArmPose pose, out double[] xs, out double[] ys, out double thin)
        {
            double wx = pose.X, wy = pose.Y;
            double hx = Math.Cos(pose.Angle), hy = Math.Sin(pose.Angle);
            double chord = Math.Max(1e-6, Hypot(wx - rx, wy - ry));
            double p1x = rx + spec.Out * chord * 0.4, p1y = ry;
            double p2x = wx - hx * chord * 0.3, p2y = wy - hy * chord * 0.3;
            double[] bx = new double[Samples + 1], by = new double[Samples + 1];
            for (int i = 0; i <= Samples; i++)
            {
                double t = (double)i / Samples, u = 1 - t;
                double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                bx[i] = a * rx + b * p1x + c * p2x + d * wx;
                by[i] = a * ry + b * p1y + c * p2y + d * wy;
            }
            double along = Length(bx, by);
            // His top side: up when he's lying flat, outward when he dangles.
            double cx = (wx - rx) / chord, cy = (wy - ry) / chord;
            double nx = spec.Out < 0 ? -cy : cy, ny = spec.Out < 0 ? cx : -cx;
            if (along >= spec.Length)
            {
                xs = bx; ys = by;
                thin = Math.Max(0.72, Math.Sqrt(spec.Length / along));
                return;
            }
            double lo = 0, hi = spec.Length;
            xs = new double[Samples + 1]; ys = new double[Samples + 1];
            for (int n = 0; n < 16; n++)
            {
                double mid = (lo + hi) / 2;
                Arch(bx, by, nx, ny, mid, xs, ys);
                if (Length(xs, ys) < spec.Length) lo = mid; else hi = mid;
            }
            Arch(bx, by, nx, ny, lo, xs, ys);
            thin = 1;
        }

        private static void Arch(double[] bx, double[] by, double nx, double ny, double amount, double[] xs, double[] ys)
        {
            for (int i = 0; i < bx.Length; i++)
            {
                double k = amount * Bump((double)i / Samples);
                xs[i] = bx[i] + nx * k; ys[i] = by[i] + ny * k;
            }
        }

        // Outlines the arm. boil (sprite pixels) makes the lines wander a little, differently for
        // each seed, like a drawing redrawn every few frames.
        public static ArmShape Shape(ArmSpec spec, double rx, double ry, ArmPose pose, double seed, double boil)
        {
            double[] px, py; double thin;
            Centerline(spec, rx, ry, pose, out px, out py, out thin);
            int n = px.Length;
            double[] s = new double[n];
            for (int i = 1; i < n; i++) s[i] = s[i - 1] + Hypot(px[i] - px[i - 1], py[i] - py[i - 1]);
            double total = s[n - 1];
            PointF[] tops = new PointF[n + 1], bottoms = new PointF[n + 1];
            PointF[] topOuter = new PointF[n + 1], topInner = new PointF[n + 1], bottomOuter = new PointF[n + 1], bottomInner = new PointF[n + 1];
            double[] tx = new double[n], ty = new double[n], nxs = new double[n], nys = new double[n];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                double dx = px[b] - px[a], dy = py[b] - py[a];
                double l = Math.Max(1e-6, Hypot(dx, dy)); dx /= l; dy /= l;
                double nx = spec.Out < 0 ? -dy : dy, ny = spec.Out < 0 ? dx : -dx;
                tx[i] = dx; ty[i] = dy; nxs[i] = nx; nys[i] = ny;
                double t = (double)i / (n - 1);
                double squeeze = 1 - (1 - thin) * Math.Pow(Math.Sin(Math.PI * t), 2);
                double top = spec.Top * squeeze + spec.FlareSize * Math.Exp(-(s[i] - spec.FlareAt) / spec.FlareFade)
                    + spec.EndFlareSize * Math.Exp(-(total - s[i]) / spec.EndFlareFade);
                double bottom = spec.Bottom * squeeze;
                // The lines hold still where they meet the body and the hand.
                double fade = Math.Min(1, Math.Min(Math.Max(0, (s[i] - spec.Cut) / 40), Math.Max(0, (total - s[i]) / 40)));
                double j1 = boil * fade * (0.6 * Math.Sin(s[i] / 23 + seed) + 0.4 * Math.Sin(s[i] / 9.7 + seed * 1.7));
                double j2 = boil * fade * (0.6 * Math.Sin(s[i] / 19 + seed * 2.3) + 0.4 * Math.Sin(s[i] / 11.3 + seed * 0.7));
                double wobble = boil > 0 ? 0.12 * fade : 0;
                double wt = InkWidth * (1 + wobble * Math.Sin(s[i] / 14 + seed * 3.1)), wb = InkWidth * (1 + wobble * Math.Sin(s[i] / 13 + seed * 1.3));
                double ct = top - InkWidth / 2 + j1, cb = bottom - InkWidth / 2 + j2;
                tops[i] = At(px[i], py[i], nx, ny, ct); bottoms[i] = At(px[i], py[i], nx, ny, -cb);
                topOuter[i] = At(px[i], py[i], nx, ny, ct + wt / 2); topInner[i] = At(px[i], py[i], nx, ny, ct - wt / 2);
                bottomOuter[i] = At(px[i], py[i], nx, ny, -cb - wb / 2); bottomInner[i] = At(px[i], py[i], nx, ny, -cb + wb / 2);
            }
            // Each edge runs on a little under the hand so no gap opens at the wrist.
            float ex = (float)(tx[n - 1] * 12), ey = (float)(ty[n - 1] * 12);
            foreach (PointF[] edge in new PointF[][] { tops, bottoms, topOuter, topInner, bottomOuter, bottomInner })
                edge[n] = new PointF(edge[n - 1].X + ex, edge[n - 1].Y + ey);
            ArmShape shape = new ArmShape();
            shape.Fill = Join(tops, bottoms);
            shape.Ink = new PointF[][] { Join(topOuter, topInner), Join(bottomOuter, bottomInner) };
            // The hair mark: a short slanted dash.
            int m = n - 2;
            for (int i = 0; i < n; i++) if (s[i] >= spec.MarkAt) { m = Math.Min(n - 2, i); break; }
            double mcx = px[m] - nxs[m] * spec.MarkOffset, mcy = py[m] - nys[m] * spec.MarkOffset;
            double mx = (tx[m] * 0.5 + nxs[m] * 0.85) * 4, my = (ty[m] * 0.5 + nys[m] * 0.85) * 4;
            shape.MarkFrom = new PointF((float)(mcx - mx), (float)(mcy - my));
            shape.MarkTo = new PointF((float)(mcx + mx), (float)(mcy + my));
            return shape;
        }

        private static PointF At(double x, double y, double nx, double ny, double k) { return new PointF((float)(x + nx * k), (float)(y + ny * k)); }

        // One edge there, the other back.
        private static PointF[] Join(PointF[] a, PointF[] b)
        {
            PointF[] all = new PointF[a.Length + b.Length];
            a.CopyTo(all, 0);
            for (int i = 0; i < b.Length; i++) all[a.Length + i] = b[b.Length - 1 - i];
            return all;
        }
    }

    // An arm hanging loose while he's carried: the wrist and the tip of the hand are two
    // weights on a stretchy arm, swinging as the window moves. Positions are in "world" sprite
    // pixels, so the window moving under them is what sets them swinging.
    internal sealed class Dangle
    {
        private const double Gravity = 15000;   // Sprite pixels per second², for a swing of about a second.
        private const double Hand = 200;        // Wrist to fingertips.
        // The lowest the fingertips can go, below the canvas's top (sprite pixels).
        private const double Bottom = (OnkeyRenderer.CanvasHeight - 26) / OnkeyRenderer.SpriteScale - 90;
        private double wx, wy, wbx, wby, tx, ty, tbx, tby;

        public Dangle(ArmSpec spec, double ox, double oy, ArmPose pose)
        {
            wx = wbx = ox + pose.X; wy = wby = oy + pose.Y;
            tx = tbx = wx + Math.Cos(pose.Angle) * Hand; ty = tby = wy + Math.Sin(pose.Angle) * Hand;
        }

        private static void Fall(ref double x, ref double y, ref double bx, ref double by, double dt)
        {
            double vx = (x - bx) * 0.985, vy = (y - by) * 0.985;
            double v = Math.Sqrt(vx * vx + vy * vy);
            if (v > 700) { vx *= 700 / v; vy *= 700 / v; }   // A hard fling mustn't send them flying apart.
            bx = x; by = y;
            x += vx; y += vy + Gravity * dt * dt;
        }

        public void Step(ArmSpec spec, double ox, double oy, double dt)
        {
            double rx = ox + spec.Root.X, ry = oy + spec.Root.Y;
            Fall(ref wx, ref wy, ref wbx, ref wby, dt);
            Fall(ref tx, ref ty, ref tbx, ref tby, dt);
            for (int n = 0; n < 4; n++)
            {
                // The arm stretches a little but never folds right up, and his hands don't cross.
                double limit = ox + 887 + spec.Out * 110;
                if (spec.Out < 0 ? wx > limit : wx < limit) wx = limit;
                // Nor do they go up behind him, where they'd stick: out to his side they go.
                Clear(spec, ox, oy, ref wx, wy);
                Clear(spec, ox, oy, ref tx, ty);
                // And they stay inside his window.
                wy = Math.Min(wy, oy + Bottom - Hand); ty = Math.Min(ty, oy + Bottom);
                double dx = wx - rx, dy = wy - ry;
                double d = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
                double reach = Math.Min(Math.Max(d, 0.5 * spec.Length), 1.08 * spec.Length);
                wx = rx + dx / d * reach; wy = ry + dy / d * reach;
                // The hand stays the same size, and his wrist gently lines it up with the arm.
                double wantX = wx + dx / d * Hand, wantY = wy + dy / d * Hand;
                tx += (wantX - tx) * 0.08; ty += (wantY - ty) * 0.08;
                double hx = tx - wx, hy = ty - wy;
                double h = Math.Max(1e-6, Math.Sqrt(hx * hx + hy * hy));
                tx = wx + hx / h * Hand; ty = wy + hy / h * Hand;
            }
        }

        // His body, which hands can't hide behind (sprite pixels, from the canvas's top-left).
        private const double BodyLeft = 580, BodyRight = 1176, BodyBottom = 870;

        private static void Clear(ArmSpec spec, double ox, double oy, ref double x, double y)
        {
            if (y - oy >= BodyBottom || x - ox <= BodyLeft || x - ox >= BodyRight) return;
            x = ox + (spec.Out < 0 ? BodyLeft : BodyRight);
        }

        public ArmPose Pose(double ox, double oy) { return new ArmPose(wx - ox, wy - oy, Math.Atan2(ty - wy, tx - wx)); }
    }

    // One Onkey's arms: walking hand over hand, dangling when picked up, and easing between.
    public sealed class ArmRig
    {
        // Walking: each hand is planted for this much of a stride, then lifts and reaches forward.
        private const double Stance = 0.55, Travel = 150, Lift = 55;
        // How far his head bobs at most (canvas points), twice a stride.
        private const double Bob = 2.2;

        private double phase, direction = -1, across = 1;
        private double walkMix;
        private bool walkingNow;
        private Dangle[] dangles;
        private double dangleMix;
        private bool dangling;
        private double boilClock;
        public double Seed;
        public readonly ArmPose[] Poses = { ArmPose.Rest(ArmSpec.Left), ArmPose.Rest(ArmSpec.Right) };
        public double Bounce;   // Canvas points his head is lifted by.
        public bool Moving;     // Whether the arms are animating at all.

        // He walked this far this tick (sprite pixels; dx across the screen, + is right).
        public void Walked(double dx, double distance)
        {
            if (distance <= 0) return;
            walkingNow = true;
            if (Math.Abs(dx) > distance * 0.2) direction = dx > 0 ? 1 : -1;
            // A planted hand slides back exactly as far as he moves, so it never skates.
            phase = (phase + distance * Stance / Travel) % 1;
            across = Math.Min(1, Math.Abs(dx) / distance);
        }

        // ox, oy: where the canvas's top-left is, in world sprite pixels (y-down).
        // Returns whether anything about the arms changed.
        public bool Update(double dt, double ox, double oy, bool carried, bool floppy, bool sketchy)
        {
            ArmPose before0 = Poses[0], before1 = Poses[1];
            double beforeBounce = Bounce, beforeSeed = Seed;
            walkMix += ((walkingNow ? 1 : 0) - walkMix) * Math.Min(1, dt * 8);
            if (!walkingNow && walkMix < 0.002) walkMix = 0;
            walkingNow = false;

            ArmPose[] target = new ArmPose[2];
            for (int i = 0; i < 2; i++)
            {
                ArmSpec spec = ArmSpec.Both[i];
                target[i] = ArmPose.Rest(spec);
                if (walkMix > 0) target[i] = target[i].Mix(Gait(spec), walkMix);
            }
            Bounce = Bob * Math.Abs(Math.Sin(2 * Math.PI * phase)) * walkMix;

            bool hang = carried && floppy;
            if (hang && !dangling)
            {
                dangles = new Dangle[] { new Dangle(ArmSpec.Left, ox, oy, Poses[0]), new Dangle(ArmSpec.Right, ox, oy, Poses[1]) };
                dangling = true;
            }
            else if (!hang && dangling) dangling = false;
            if (dangling) dangleMix = 1; else if (dangleMix > 0) dangleMix = Math.Max(0, dangleMix - dt / 0.45);
            if (dangleMix == 0) dangles = null;
            if (dangles != null)
            {
                // Settling back down overshoots a touch, so his hands slap onto the ground.
                double t = 1 - dangleMix, k = 1.7;
                double eased = dangling ? 0 : 1 + (k + 1) * Math.Pow(t - 1, 3) + k * Math.Pow(t - 1, 2);
                for (int i = 0; i < 2; i++)
                {
                    dangles[i].Step(ArmSpec.Both[i], ox, oy, dt);
                    target[i] = dangles[i].Pose(ox, oy).Mix(target[i], eased);
                }
            }
            Poses[0] = target[0]; Poses[1] = target[1];
            Moving = walkMix > 0 || dangleMix > 0;
            if (Moving && sketchy)
            {
                // Redrawn eight times a second, like animation on paper.
                boilClock += dt;
                if (boilClock >= 0.125) { boilClock = 0; Seed = (Seed + 2.39) % 100; }
            }
            return !before0.Same(Poses[0]) || !before1.Same(Poses[1]) || beforeBounce != Bounce || beforeSeed != Seed;
        }

        public PointF Shoulder(ArmSpec spec)
        {
            return new PointF(spec.Root.X, (float)(spec.Root.Y - Bounce / OnkeyRenderer.SpriteScale));
        }

        public ArmShape[] Shapes(bool sketchy)
        {
            ArmShape[] shapes = new ArmShape[2];
            for (int i = 0; i < 2; i++)
            {
                PointF r = Shoulder(ArmSpec.Both[i]);
                shapes[i] = ArmGeometry.Shape(ArmSpec.Both[i], r.X, r.Y, Poses[i], Seed, sketchy && Moving ? 1.2 : 0);
            }
            return shapes;
        }

        // Where a hand is in its stride, walking in `direction`.
        private ArmPose Gait(ArmSpec spec)
        {
            double p = (phase + (spec.Out < 0 ? 0 : 0.5)) % 1;
            double travel = Travel * across;
            // The hand on the side he's heading reaches out further; the other one tucks in.
            double front = travel * (spec.Out == direction ? 2.0 / 3 : 1.0 / 3);
            double fx = spec.Wrist.X + direction * front, bx = spec.Wrist.X - direction * (travel - front);
            if (p < Stance) return new ArmPose(fx + (bx - fx) * p / Stance, spec.Wrist.Y, spec.RestAngle);
            double u = (p - Stance) / (1 - Stance);
            double ease = u * u * (3 - 2 * u);
            // The hand peels up off the ground and reaches forward, fingers first.
            double tilt = 0.56 * Math.Pow(Math.Sin(Math.PI * u), 0.8);
            return new ArmPose(bx + (fx - bx) * ease, spec.Wrist.Y - Lift * Math.Sin(Math.PI * u),
                               spec.RestAngle + (spec.Out < 0 ? tilt : -tilt));
        }
    }
}
