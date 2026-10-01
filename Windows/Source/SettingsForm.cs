using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace OnkeyDesktopPet
{
    // The Settings window: a hand-drawn jungle page with a tab for each group of settings.
    // Everything is painted here (sketchy lines, vine sliders, leaf switches) rather than
    // built from stock controls, and every change takes effect straight away.
    internal sealed class SettingsForm : Form
    {
        public const float PageWidth = 600, PageHeight = 610;
        // The card the settings sit on, and the columns inside it.
        public const float CardLeft = 18, CardTop = 104, CardRight = PageWidth - 18, CardBottom = PageHeight - 18;
        public const float LabelLeft = 46, ControlLeft = 262, ControlRight = CardRight - 28;

        private readonly OnkeyApp app;
        private readonly Updater updater;
        private readonly float scale;
        private readonly List<Page> pages = new List<Page>();
        private int current;
        private Widget pressed, hovered;
        private Bitmap backdrop, head;
        public readonly Font TitleFont, TabFont, LabelFont, ChipFont, SmallFont;

        private sealed class Page
        {
            public string Name;
            public RectangleF Tab;
            public readonly List<Widget> Widgets = new List<Widget>();
        }

        public SettingsForm(OnkeyApp app, Updater updater, Icon icon)
        {
            this.app = app;
            this.updater = updater;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Text = "Onkey Settings";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size((int)Math.Ceiling(PageWidth * scale), (int)Math.Ceiling(PageHeight * scale));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Jungle.Night;
            KeyPreview = true;

            FontFamily hand = Jungle.HandFont();
            TitleFont = new Font(hand, 27, FontStyle.Bold, GraphicsUnit.Pixel);
            TabFont = new Font(hand, 17, FontStyle.Bold, GraphicsUnit.Pixel);
            LabelFont = new Font(hand, 17, FontStyle.Bold, GraphicsUnit.Pixel);
            ChipFont = new Font(hand, 15, FontStyle.Bold, GraphicsUnit.Pixel);
            SmallFont = new Font(hand, 14, FontStyle.Regular, GraphicsUnit.Pixel);
            head = app.RenderHead((int)Math.Round(52 * scale));

            BuildPages();
            app.SettingsChanged += Changed;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                app.SettingsChanged -= Changed;
                if (backdrop != null) backdrop.Dispose();
                if (head != null) head.Dispose();
                TitleFont.Dispose(); TabFont.Dispose(); LabelFont.Dispose(); ChipFont.Dispose(); SmallFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void Changed() { if (!IsDisposed) Invalidate(); }

        // Pages

        private void BuildPages()
        {
            Page behaviour = AddPage("Behaviour");
            Stepper count = new Stepper(1, 20, delegate { return (int)app.Settings.Number("count"); },
                delegate(int v) { app.Change("count", v); });
            Add(behaviour, "How many Onkeys", count, delegate
            {
                int n = (int)app.Settings.Number("count");
                return n <= 1 ? "just the one" : n >= 8 ? "total chaos" : "a little troop";
            });
            Add(behaviour, "Where he roams", Choices("zone",
                "Anywhere", "anywhere", "Bottom", "bottom", "Top", "top",
                "Left side", "left", "Right side", "right", "Stay put", "stay"), null);
            // "chase" is how many trips out of N head for the mouse (0 for never).
            string[] chaseValues = { "0", "5", "2", "1" }, chaseNames = { "never", "sometimes", "often", "always" };
            Slider chase = new Slider(0, 3, delegate { return Math.Max(0, Array.IndexOf(chaseValues, app.Settings.Get("chase"))); },
                delegate(double v) { app.Change("chase", chaseValues[(int)Math.Round(v)]); }, false);
            chase.Snap = 1;
            Add(behaviour, "Walks to my mouse", chase, delegate
            {
                return chaseNames[Math.Max(0, Array.IndexOf(chaseValues, app.Settings.Get("chase")))];
            });
            Add(behaviour, "Walking speed", NumberSlider("speed", 10, 200, false), delegate
            {
                double v = app.Settings.Number("speed");
                return v < 30 ? "a slow stroll" : v < 60 ? "normal" : v < 120 ? "fast" : "zoomies!";
            });
            Add(behaviour, "Dance to music", Switch("dance"), delegate { return "bops to the beat"; });
            Add(behaviour, "Let me drag him", Switch("draggable"), delegate { return "click him to hear him"; });
            Add(behaviour, null, new SketchButton("Bring Onkey to this screen", delegate { app.BringHere(); }), null);

            Page sound = AddPage("Sound");
            Func<bool> soundOn = delegate { return app.HasSound && app.Settings.Bool("soundOn"); };
            Toggle soundSwitch = Switch("soundOn");
            soundSwitch.Enabled = delegate { return app.HasSound; };
            Add(sound, "Sound", soundSwitch, delegate { return app.HasSound ? "his oooo now and then" : "sound clip not installed"; });
            Slider gap = new Slider(0, GapSteps.Length - 1, delegate { return GapIndex(app.Settings.Number("soundGap")); },
                delegate(double v) { app.Change("soundGap", GapSteps[(int)Math.Round(v)]); }, false);
            gap.Snap = 1;
            gap.Enabled = soundOn;
            Add(sound, "How often", gap, delegate { return Every(app.Settings.Number("soundGap")); });
            Slider volume = NumberSlider("volume", 0.05, 1, false);
            volume.Enabled = soundOn;
            Add(sound, "Volume", volume, delegate { return Percent(app.Settings.Number("volume")); });
            SketchButton play = new SketchButton("Say oooo now", delegate { app.PlayNow(); });
            play.Enabled = delegate { return app.HasSound; };
            Add(sound, null, play, null);

            Page look = AddPage("Look");
            // Re-rendering every frame is too slow to follow the mouse, so size waits for the drop.
            Add(look, "Size", NumberSlider("size", 0.4, 3, true), delegate { return Percent(app.Settings.Number("size")); });
            Add(look, "See-through", NumberSlider("opacity", 0.2, 1, false), delegate
            {
                double v = app.Settings.Number("opacity");
                return v > 0.95 ? "solid" : v < 0.45 ? "ghostly" : Percent(v) + " solid";
            });
            Add(look, "Where he lives", Choices("layer", "In front", "above", "On the desktop", "desktop"), null);
            Toggle fullScreen = Switch("overFullScreen");
            fullScreen.Enabled = delegate { return app.Settings.Get("layer") != "desktop"; };
            Add(look, "Over full-screen apps", fullScreen, delegate { return "videos, games, slideshows"; });
            Add(look, "Watch my cursor", Switch("watchCursor"), delegate { return "his eyes follow the mouse"; });
            Add(look, "Blink", Switch("blink"), delegate { return "now and then"; });

            Page updates = AddPage("Updates");
            Add(updates, null, new Note(delegate
            {
                Updater.Release release = updater.Available;
                if (release == null) return "You have Onkey " + Program.Version + ".";
                return updater.Downloading ? "Downloading Onkey " + release.Version + "..."
                    : "Onkey " + release.Version + " is out! You have " + Program.Version + ".";
            }), null);
            SketchButton check = new SketchButton("Check for updates", delegate { updater.MenuChosen(); });
            check.Text = delegate { return updater.Available == null ? "Check for updates" : "Update to Onkey " + updater.Available.Version; };
            check.Enabled = delegate { return !updater.Downloading; };
            Add(updates, null, check, null);
            Add(updates, "Check by himself", Switch("checkUpdates"), delegate { return "looks every few hours"; });
            Add(updates, "Open at startup", new Toggle(delegate { return app.OpensAtLogin; }, delegate(bool on) { app.ToggleLogin(); }),
                delegate { return "when Windows starts"; });
        }

        private Page AddPage(string name)
        {
            Page page = new Page();
            page.Name = name;
            float width = 112, gap = 8;
            page.Tab = new RectangleF(CardLeft + 14 + pages.Count * (width + gap), CardTop - 40, width, 44);
            pages.Add(page);
            return page;
        }

        // Rows stack down the card; a row with no label spans the whole width.
        private void Add(Page page, string label, Widget widget, Func<string> caption)
        {
            float top = CardTop + 30;
            if (page.Widgets.Count > 0) top = page.Widgets[page.Widgets.Count - 1].RowBottom + 8;
            widget.Form = this;
            widget.Label = label;
            widget.Caption = caption;
            float left = label == null ? LabelLeft : ControlLeft;
            float height = widget.Measure(ControlRight - left);
            float rowHeight = Math.Max(height, caption != null ? 46 : 30);
            widget.Bounds = new RectangleF(left, top + (rowHeight - height) / 2, ControlRight - left, height);
            widget.RowTop = top;
            widget.RowBottom = top + rowHeight;
            page.Widgets.Add(widget);
        }

        private Toggle Switch(string key)
        {
            return new Toggle(delegate { return app.Settings.Bool(key); }, delegate(bool on) { app.Change(key, on ? "true" : "false"); });
        }

        private Chips Choices(string key, params string[] namesAndValues)
        {
            return new Chips(namesAndValues, delegate { return app.Settings.Get(key); }, delegate(string v) { app.Change(key, v); });
        }

        private Slider NumberSlider(string key, double min, double max, bool onDrop)
        {
            return new Slider(min, max, delegate { return app.Settings.Number(key); }, delegate(double v) { app.Change(key, Math.Round(v, 3)); }, onDrop);
        }

        // Seconds between his sounds, at least: he waits between this and twice this.
        private static readonly double[] GapSteps = { 10, 20, 30, 45, 60, 90, 120, 180, 300, 600 };

        private static double GapIndex(double gap)
        {
            int best = 0;
            for (int i = 1; i < GapSteps.Length; i++) if (Math.Abs(GapSteps[i] - gap) < Math.Abs(GapSteps[best] - gap)) best = i;
            return best;
        }

        private static string Every(double gap)
        {
            if (gap * 2 < 120) return "every " + gap + "-" + gap * 2 + " seconds";
            return "every " + Minutes(gap) + "-" + Minutes(gap * 2) + " minutes";
        }

        private static string Minutes(double seconds)
        {
            double m = seconds / 60;
            if (Math.Abs(m - Math.Floor(m) - 0.5) < 0.01) return Math.Floor(m) > 0 ? Math.Floor(m) + "½" : "½";
            return Math.Round(m).ToString(CultureInfo.InvariantCulture);
        }

        private static string Percent(double v) { return Math.Round(v * 100) + "%"; }

        // Painting

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (backdrop == null || backdrop.Width != ClientSize.Width || backdrop.Height != ClientSize.Height)
            {
                if (backdrop != null) backdrop.Dispose();
                backdrop = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
                using (Graphics b = Graphics.FromImage(backdrop))
                {
                    Jungle.Prepare(b);
                    b.ScaleTransform(scale, scale);
                    Jungle.DrawBackdrop(b, PageWidth, PageHeight);
                }
            }
            g.DrawImageUnscaled(backdrop, 0, 0);
            g.DrawImage(head, (int)(20 * scale), (int)(12 * scale));

            Jungle.Prepare(g);
            g.ScaleTransform(scale, scale);
            Jungle.Text(g, "Onkey's settings", TitleFont, Jungle.Paper, new PointF(80, 22));

            for (int i = 0; i < pages.Count; i++) if (i != current) DrawTab(g, pages[i], false);
            Jungle.Card(g, RectangleF.FromLTRB(CardLeft, CardTop, CardRight, CardBottom), 11);
            DrawTab(g, pages[current], true);

            foreach (Widget w in pages[current].Widgets)
            {
                bool enabled = w.Enabled();
                if (w.Label != null)
                {
                    float y = w.RowTop + (w.RowBottom - w.RowTop) / 2 - (w.Caption != null ? 19 : 11);
                    Jungle.Text(g, w.Label, LabelFont, enabled ? Jungle.Ink : Jungle.Faded, new PointF(LabelLeft, y));
                    if (w.Caption != null) Jungle.Text(g, w.Caption(), SmallFont, Jungle.Faded, new PointF(LabelLeft + 1, y + 21));
                }
                w.Paint(g, enabled, w == hovered && enabled);
            }
        }

        private void DrawTab(Graphics g, Page page, bool selected)
        {
            RectangleF r = page.Tab;
            if (!selected) r = new RectangleF(r.X, r.Y + 6, r.Width, r.Height - 6);
            int seed = (int)r.X * 7 + (selected ? 1 : 0);
            using (GraphicsPath path = Sketch.Tab(r, seed))
            {
                using (Brush fill = new SolidBrush(selected ? Jungle.Paper : Jungle.Bark))
                    g.FillPath(fill, path);
                if (!selected)
                {
                    // Wood grain.
                    using (Pen grain = new Pen(Color.FromArgb(70, Jungle.Ink), 1))
                        for (int i = 1; i < 3; i++)
                            g.DrawCurve(grain, Sketch.Wiggle(new PointF(r.Left + 10, r.Top + i * r.Height / 3), new PointF(r.Right - 10, r.Top + i * r.Height / 3 + 2), seed + i, 1.2f));
                }
                Sketch.Stroke(g, path, Jungle.Ink, 2f);
            }
            if (selected)
            {
                // Hide the card's top edge under the open tab.
                using (Brush paper = new SolidBrush(Jungle.Paper)) g.FillRectangle(paper, r.Left + 3, CardTop - 2, r.Width - 6, 8);
            }
            SizeF size = g.MeasureString(page.Name, TabFont);
            Jungle.Text(g, page.Name, TabFont, selected ? Jungle.Ink : Jungle.Paper,
                new PointF(r.Left + (r.Width - size.Width) / 2, r.Top + (r.Height - size.Height) / 2 + (selected ? -1 : 0)));
        }

        // Mouse and keys

        private PointF Logical(MouseEventArgs e) { return new PointF(e.X / scale, e.Y / scale); }

        private Widget WidgetAt(PointF p)
        {
            foreach (Widget w in pages[current].Widgets)
                if (w.Interactive && w.Enabled() && w.HitArea().Contains(p)) return w;
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            PointF p = Logical(e);
            for (int i = 0; i < pages.Count; i++)
                if (pages[i].Tab.Contains(p)) { SelectPage(i); return; }
            pressed = WidgetAt(p);
            if (pressed != null) { Capture = true; pressed.Down(p); Invalidate(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            PointF p = Logical(e);
            if (pressed != null) { pressed.Drag(p); Invalidate(); return; }
            Widget over = WidgetAt(p);
            bool onTab = false;
            foreach (Page page in pages) if (page.Tab.Contains(p)) onTab = true;
            Cursor = over != null || onTab ? Cursors.Hand : Cursors.Default;
            if (over != hovered) { hovered = over; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (pressed == null) return;
            Widget w = pressed;
            pressed = null;
            Capture = false;
            w.Up(Logical(e));
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hovered != null) { hovered = null; Invalidate(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.Control && e.KeyCode == Keys.Tab) SelectPage((current + (e.Shift ? pages.Count - 1 : 1)) % pages.Count);
        }

        private void SelectPage(int index)
        {
            current = index;
            hovered = null;
            Invalidate();
        }
    }

    // Colours, the hand-drawn font, and the jungle behind the card.
    internal static class Jungle
    {
        public static readonly Color Night = Color.FromArgb(28, 52, 34);
        public static readonly Color Canopy = Color.FromArgb(40, 74, 44);
        public static readonly Color Paper = Color.FromArgb(246, 237, 211);
        public static readonly Color Ink = Color.FromArgb(58, 40, 24);
        public static readonly Color Faded = Color.FromArgb(140, 120, 94);
        public static readonly Color Bark = Color.FromArgb(146, 98, 54);
        public static readonly Color Leaf = Color.FromArgb(92, 146, 62);
        public static readonly Color LeafDark = Color.FromArgb(56, 104, 46);
        public static readonly Color Banana = Color.FromArgb(244, 200, 66);
        public static readonly Color Vine = Color.FromArgb(104, 122, 52);

        // Windows' own handwriting fonts, best first.
        public static FontFamily HandFont()
        {
            foreach (string name in new string[] { "Segoe Print", "Ink Free", "Comic Sans MS", "Comic Neue" })
            {
                try { return new FontFamily(name); }
                catch (ArgumentException) { }
            }
            return FontFamily.GenericSansSerif;
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static void Text(Graphics g, string text, Font font, Color color, PointF at)
        {
            using (Brush b = new SolidBrush(color)) g.DrawString(text, font, b, at);
        }

        public static void DrawBackdrop(Graphics g, float w, float h)
        {
            using (LinearGradientBrush sky = new LinearGradientBrush(new RectangleF(0, 0, w, h), Canopy, Night, 90f))
                g.FillRectangle(sky, 0, 0, w, h);
            Random r = new Random(7);
            // Dappled light through the canopy.
            for (int i = 0; i < 26; i++)
                using (Brush spot = new SolidBrush(Color.FromArgb(14 + r.Next(14), 200, 230, 140)))
                {
                    float s = 20 + (float)r.NextDouble() * 60;
                    g.FillEllipse(spot, (float)r.NextDouble() * w, (float)r.NextDouble() * h, s, s * 0.7f);
                }
            // Big leaves crowding in from the edges, darkest at the back.
            Color[] greens = { Color.FromArgb(34, 70, 38), LeafDark, Color.FromArgb(72, 128, 56), Leaf };
            for (int layer = 0; layer < greens.Length; layer++)
                for (int i = 0; i < 9; i++)
                {
                    int side = r.Next(4);
                    float along = (float)r.NextDouble();
                    PointF at = side == 0 ? new PointF(along * w, -6) : side == 1 ? new PointF(w + 6, along * h)
                              : side == 2 ? new PointF(along * w, h + 6) : new PointF(-6, along * h);
                    float toward = (float)(Math.Atan2(h / 2 - at.Y, w / 2 - at.X) * 180 / Math.PI) + (float)(r.NextDouble() * 70 - 35);
                    float length = 70 + (float)r.NextDouble() * 70 - layer * 8;
                    DrawLeaf(g, at, toward, length, length * 0.42f, greens[layer], r.Next());
                }
            // Vines hanging from the top.
            for (int i = 0; i < 5; i++)
            {
                float x = 40 + i * (w - 80) / 4 + (float)r.NextDouble() * 30;
                float len = 40 + (float)r.NextDouble() * 50;
                PointF end = new PointF(x + (float)r.NextDouble() * 16 - 8, len);
                using (Pen vine = new Pen(Vine, 3f)) g.DrawCurve(vine, Sketch.Wiggle(new PointF(x, -4), end, r.Next(), 3));
                DrawLeaf(g, end, 70 + (float)r.NextDouble() * 40, 22, 11, Leaf, r.Next());
                DrawLeaf(g, new PointF(x + 1, len * 0.5f), 200 + (float)r.NextDouble() * 30, 18, 9, Leaf, r.Next());
            }
        }

        public static void DrawLeaf(Graphics g, PointF at, float angle, float length, float width, Color color, int seed)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform(angle);
            using (GraphicsPath leaf = new GraphicsPath())
            {
                leaf.AddBezier(0, 0, length * 0.3f, -width, length * 0.75f, -width * 0.8f, length, 0);
                leaf.AddBezier(length, 0, length * 0.75f, width * 0.8f, length * 0.3f, width, 0, 0);
                using (Brush b = new SolidBrush(color)) g.FillPath(b, leaf);
                Color edge = Color.FromArgb(150, Ink);
                Sketch.Stroke(g, leaf, edge, 1.4f);
                using (Pen vein = new Pen(Color.FromArgb(110, Ink), 1.2f))
                {
                    g.DrawCurve(vein, Sketch.Wiggle(new PointF(0, 0), new PointF(length * 0.9f, 0), seed, 0.8f));
                    for (int i = 1; i <= 3; i++)
                    {
                        float x = length * i / 4.5f;
                        g.DrawLine(vein, x, 0, x + length * 0.12f, -width * 0.45f);
                        g.DrawLine(vein, x, 0, x + length * 0.12f, width * 0.45f);
                    }
                }
            }
            g.Restore(state);
        }

        // The paper card, with a soft shadow and a pencilled edge.
        public static void Card(Graphics g, RectangleF r, int seed)
        {
            using (GraphicsPath shadow = Sketch.Box(new RectangleF(r.X + 4, r.Y + 6, r.Width, r.Height), 14, seed, 1.5f))
            using (Brush b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillPath(b, shadow);
            using (GraphicsPath card = Sketch.Box(r, 14, seed, 1.5f))
            {
                using (Brush b = new SolidBrush(Paper)) g.FillPath(b, card);
                Sketch.Stroke(g, card, Ink, 2.2f);
            }
        }
    }

    // Wobbly, pencil-like shapes. Each is shaken by a seeded random so it keeps the same
    // wobble every time it's drawn.
    internal static class Sketch
    {
        public static GraphicsPath Box(RectangleF r, float radius, int seed, float wobble)
        {
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            List<PointF> outline = new List<PointF>();
            Corner(outline, r.Right - radius, r.Top + radius, radius, -90);
            Corner(outline, r.Right - radius, r.Bottom - radius, radius, 0);
            Corner(outline, r.Left + radius, r.Bottom - radius, radius, 90);
            Corner(outline, r.Left + radius, r.Top + radius, radius, 180);
            return Shaken(outline, seed, wobble, true);
        }

        // A tab: rounded on top, open at the bottom.
        public static GraphicsPath Tab(RectangleF r, int seed)
        {
            List<PointF> outline = new List<PointF>();
            outline.Add(new PointF(r.Left, r.Bottom));
            Corner(outline, r.Left + 12, r.Top + 12, 12, 180);
            Corner(outline, r.Right - 12, r.Top + 12, 12, 270);
            outline.Add(new PointF(r.Right, r.Bottom));
            return Shaken(outline, seed, 1.2f, false);
        }

        public static GraphicsPath Circle(PointF c, float radius, int seed, float wobble)
        {
            List<PointF> outline = new List<PointF>();
            int n = Math.Max(10, (int)(radius * 1.2f));
            for (int i = 0; i < n; i++)
            {
                double a = i * 2 * Math.PI / n;
                outline.Add(new PointF(c.X + (float)Math.Cos(a) * radius, c.Y + (float)Math.Sin(a) * radius));
            }
            return Shaken(outline, seed, wobble, true);
        }

        // Points along a hand-drawn line from a to b.
        public static PointF[] Wiggle(PointF a, PointF b, int seed, float wobble)
        {
            Random r = new Random(seed);
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            int n = Math.Max(2, (int)(length / 16));
            float nx = -dy / Math.Max(1, length), ny = dx / Math.Max(1, length);
            PointF[] points = new PointF[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                float off = i == 0 || i == n ? 0 : (float)(r.NextDouble() * 2 - 1) * wobble;
                points[i] = new PointF(a.X + dx * t + nx * off, a.Y + dy * t + ny * off);
            }
            return points;
        }

        // Two passes of the pencil, the second lighter and slightly off.
        public static void Stroke(Graphics g, GraphicsPath path, Color color, float width)
        {
            using (Pen pen = new Pen(color, width)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, path); }
            using (Matrix m = new Matrix())
            using (GraphicsPath again = (GraphicsPath)path.Clone())
            using (Pen pen = new Pen(Color.FromArgb(color.A / 3, color), width * 0.6f))
            {
                m.Translate(0.8f, -0.6f);
                again.Transform(m);
                g.DrawPath(pen, again);
            }
        }

        private static void Corner(List<PointF> outline, float cx, float cy, float radius, float startDegrees)
        {
            for (int i = 0; i <= 3; i++)
            {
                double a = (startDegrees + i * 30) * Math.PI / 180;
                outline.Add(new PointF(cx + (float)Math.Cos(a) * radius, cy + (float)Math.Sin(a) * radius));
            }
        }

        private static GraphicsPath Shaken(List<PointF> outline, int seed, float wobble, bool closed)
        {
            // Fill in long straight runs so they wobble too.
            List<PointF> points = new List<PointF>();
            int count = closed ? outline.Count : outline.Count - 1;
            for (int i = 0; i < count; i++)
            {
                PointF a = outline[i], b = outline[(i + 1) % outline.Count];
                float length = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                int steps = Math.Max(1, (int)(length / 22));
                for (int s = 0; s < steps; s++)
                    points.Add(new PointF(a.X + (b.X - a.X) * s / steps, a.Y + (b.Y - a.Y) * s / steps));
            }
            if (!closed) points.Add(outline[outline.Count - 1]);
            Random r = new Random(seed);
            for (int i = 0; i < points.Count; i++)
            {
                if (!closed && (i == 0 || i == points.Count - 1)) continue;
                points[i] = new PointF(points[i].X + (float)(r.NextDouble() * 2 - 1) * wobble,
                                       points[i].Y + (float)(r.NextDouble() * 2 - 1) * wobble);
            }
            GraphicsPath path = new GraphicsPath();
            if (closed) path.AddClosedCurve(points.ToArray(), 0.4f);
            else path.AddCurve(points.ToArray(), 0.4f);
            return path;
        }

        public static int Seed(RectangleF r) { return (int)(r.X * 31 + r.Y * 17 + r.Width * 7); }
    }

    // One control on a settings page. Bounds and mouse points are in page units (96 per inch).
    internal abstract class Widget
    {
        public SettingsForm Form;
        public string Label;
        public Func<string> Caption;
        public RectangleF Bounds;
        public float RowTop, RowBottom;
        public Func<bool> Enabled = delegate { return true; };
        public virtual bool Interactive { get { return true; } }

        // The height this widget needs at the given width.
        public abstract float Measure(float width);
        public abstract void Paint(Graphics g, bool enabled, bool hover);
        public virtual RectangleF HitArea() { return Bounds; }
        public virtual void Down(PointF p) { }
        public virtual void Drag(PointF p) { }
        public virtual void Up(PointF p) { }

        protected static Color Dim(Color c, bool enabled) { return enabled ? c : Color.FromArgb(90, c); }
    }

    // An on/off switch: a sketched pod with a leaf that slides across and turns green.
    internal sealed class Toggle : Widget
    {
        private readonly Func<bool> get;
        private readonly Action<bool> set;
        private const float W = 62, H = 30;

        public Toggle(Func<bool> get, Action<bool> set) { this.get = get; this.set = set; }

        public override float Measure(float width) { return H; }
        public override RectangleF HitArea() { return new RectangleF(Bounds.Left - 200, Bounds.Top - 6, Bounds.Width + 200, Bounds.Height + 12); }

        public override void Paint(Graphics g, bool enabled, bool hover)
        {
            bool on = get();
            RectangleF pod = new RectangleF(Bounds.Left, Bounds.Top, W, H);
            int seed = Sketch.Seed(pod);
            using (GraphicsPath path = Sketch.Box(pod, H / 2, seed, 1.1f))
            {
                using (Brush b = new SolidBrush(Dim(on ? Jungle.Leaf : Color.FromArgb(222, 208, 172), enabled))) g.FillPath(b, path);
                Sketch.Stroke(g, path, Dim(Jungle.Ink, enabled), hover ? 2.6f : 2f);
            }
            float knobX = on ? pod.Right - H / 2 - 1 : pod.Left + H / 2 + 1;
            using (GraphicsPath knob = Sketch.Circle(new PointF(knobX, pod.Top + H / 2), H / 2 - 4, seed + 3, 0.8f))
            {
                using (Brush b = new SolidBrush(Dim(on ? Jungle.Banana : Jungle.Paper, enabled))) g.FillPath(b, knob);
                Sketch.Stroke(g, knob, Dim(Jungle.Ink, enabled), 1.8f);
            }
            if (on) Jungle.DrawLeaf(g, new PointF(knobX - 5, pod.Top + H / 2 + 2), -40, 13, 6, Dim(Jungle.LeafDark, enabled), seed);
            Jungle.Text(g, on ? "on" : "off", Form.SmallFont, Dim(Jungle.Faded, enabled), new PointF(pod.Right + 8, pod.Top + 5));
        }

        public override void Up(PointF p) { if (HitArea().Contains(p)) set(!get()); }
    }

    // A slider drawn as a vine with a leaf to drag along it.
    internal sealed class Slider : Widget
    {
        private readonly double min, max;
        private readonly Func<double> get;
        private readonly Action<double> set;
        private readonly bool onDrop;
        private double? dragging;
        public double Snap;   // Rounds to multiples of this, if set.

        public Slider(double min, double max, Func<double> get, Action<double> set, bool onDrop)
        {
            this.min = min; this.max = max; this.get = get; this.set = set; this.onDrop = onDrop;
        }

        public override float Measure(float width) { return 34; }
        private float TrackLeft { get { return Bounds.Left + 12; } }
        private float TrackRight { get { return Bounds.Right - 12; } }
        private float Middle { get { return Bounds.Top + Bounds.Height / 2; } }

        public override void Paint(Graphics g, bool enabled, bool hover)
        {
            double value = dragging.HasValue ? dragging.Value : get();
            float t = (float)Math.Max(0, Math.Min(1, (value - min) / (max - min)));
            float x = TrackLeft + (TrackRight - TrackLeft) * t;
            int seed = Sketch.Seed(Bounds);
            PointF[] vine = Sketch.Wiggle(new PointF(TrackLeft, Middle), new PointF(TrackRight, Middle), seed, 2.2f);
            using (Pen dry = new Pen(Dim(Color.FromArgb(196, 176, 130), enabled), 5f)) { dry.StartCap = dry.EndCap = LineCap.Round; g.DrawCurve(dry, vine); }
            // The grown part of the vine, up to the leaf.
            GraphicsState state = g.Save();
            g.SetClip(new RectangleF(Bounds.Left - 4, Bounds.Top - 10, x - Bounds.Left + 4, Bounds.Height + 20));
            using (Pen green = new Pen(Dim(Jungle.Vine, enabled), 5f)) { green.StartCap = green.EndCap = LineCap.Round; g.DrawCurve(green, vine); }
            for (float lx = TrackLeft + 22; lx < x - 10; lx += 34)
                Jungle.DrawLeaf(g, new PointF(lx, Middle - 1), ((int)lx % 2 == 0) ? -50 : 230, 12, 5, Dim(Jungle.Leaf, enabled), (int)lx);
            g.Restore(state);
            using (Pen outline = new Pen(Dim(Color.FromArgb(150, Jungle.Ink), enabled), 1.2f)) g.DrawCurve(outline, vine);
            // The handle: a big leaf standing up on the vine.
            float grow = hover || dragging.HasValue ? 1.12f : 1f;
            Jungle.DrawLeaf(g, new PointF(x, Middle + 9 * grow), -90, 30 * grow, 13 * grow, Dim(Jungle.Leaf, enabled), seed + 1);
            using (GraphicsPath nub = Sketch.Circle(new PointF(x, Middle), 6, seed + 2, 0.6f))
            {
                using (Brush b = new SolidBrush(Dim(Jungle.Banana, enabled))) g.FillPath(b, nub);
                Sketch.Stroke(g, nub, Dim(Jungle.Ink, enabled), 1.5f);
            }
        }

        private double ValueAt(PointF p)
        {
            double t = Math.Max(0, Math.Min(1, (p.X - TrackLeft) / (TrackRight - TrackLeft)));
            double v = min + (max - min) * t;
            return Snap > 0 ? Math.Round(v / Snap) * Snap : v;
        }

        public override void Down(PointF p) { Drag(p); }

        public override void Drag(PointF p)
        {
            dragging = ValueAt(p);
            if (!onDrop) set(dragging.Value);
        }

        public override void Up(PointF p)
        {
            if (dragging.HasValue) set(dragging.Value);
            dragging = null;
        }
    }

    // A row of choices, like pebbles: the chosen one is filled in leaf green.
    internal sealed class Chips : Widget
    {
        private readonly string[] names, values;
        private readonly Func<string> get;
        private readonly Action<string> set;
        private RectangleF[] chips;
        private int pressedChip = -1;

        public Chips(string[] namesAndValues, Func<string> get, Action<string> set)
        {
            names = new string[namesAndValues.Length / 2];
            values = new string[names.Length];
            for (int i = 0; i < names.Length; i++) { names[i] = namesAndValues[i * 2]; values[i] = namesAndValues[i * 2 + 1]; }
            this.get = get; this.set = set;
        }

        public override float Measure(float width)
        {
            chips = new RectangleF[names.Length];
            float x = 0, y = 0, h = 30, gap = 6;
            using (Bitmap b = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(b))
                for (int i = 0; i < names.Length; i++)
                {
                    float w = g.MeasureString(names[i], Form.ChipFont).Width + 16;
                    if (x > 0 && x + w > width) { x = 0; y += h + gap; }
                    chips[i] = new RectangleF(x, y, w, h);
                    x += w + gap;
                }
            return y + h;
        }

        private RectangleF Chip(int i) { return new RectangleF(Bounds.Left + chips[i].X, Bounds.Top + chips[i].Y, chips[i].Width, chips[i].Height); }

        public override void Paint(Graphics g, bool enabled, bool hover)
        {
            string value = get();
            for (int i = 0; i < names.Length; i++)
            {
                RectangleF r = Chip(i);
                bool chosen = values[i] == value;
                using (GraphicsPath path = Sketch.Box(r, 12, Sketch.Seed(r), 1.3f))
                {
                    Color fill = chosen ? Jungle.Leaf : i == pressedChip ? Color.FromArgb(226, 214, 180) : Jungle.Paper;
                    using (Brush b = new SolidBrush(Dim(fill, enabled))) g.FillPath(b, path);
                    Sketch.Stroke(g, path, Dim(Jungle.Ink, enabled), chosen ? 2.4f : 1.6f);
                }
                SizeF size = g.MeasureString(names[i], Form.ChipFont);
                Jungle.Text(g, names[i], Form.ChipFont, Dim(chosen ? Jungle.Paper : Jungle.Ink, enabled),
                    new PointF(r.Left + (r.Width - size.Width) / 2, r.Top + (r.Height - size.Height) / 2));
            }
        }

        private int ChipAt(PointF p)
        {
            for (int i = 0; i < names.Length; i++) if (Chip(i).Contains(p)) return i;
            return -1;
        }

        public override void Down(PointF p) { pressedChip = ChipAt(p); }
        public override void Up(PointF p)
        {
            int i = ChipAt(p);
            if (i >= 0 && i == pressedChip) set(values[i]);
            pressedChip = -1;
        }
    }

    // A number with coconut buttons either side to take one away or add one.
    internal sealed class Stepper : Widget
    {
        private readonly int min, max;
        private readonly Func<int> get;
        private readonly Action<int> set;
        private int pressedSide;

        public Stepper(int min, int max, Func<int> get, Action<int> set) { this.min = min; this.max = max; this.get = get; this.set = set; }

        public override float Measure(float width) { return 40; }
        private PointF Minus { get { return new PointF(Bounds.Left + 20, Bounds.Top + 20); } }
        private PointF Plus { get { return new PointF(Bounds.Left + 130, Bounds.Top + 20); } }

        public override void Paint(Graphics g, bool enabled, bool hover)
        {
            int n = get();
            Coconut(g, Minus, "-", (n > min), (pressedSide < 0));
            Coconut(g, Plus, "+", (n < max), (pressedSide > 0));
            string text = n.ToString(CultureInfo.InvariantCulture);
            SizeF size = g.MeasureString(text, Form.TitleFont);
            Jungle.Text(g, text, Form.TitleFont, Jungle.Ink, new PointF(Bounds.Left + 75 - size.Width / 2, Bounds.Top + 20 - size.Height / 2));
            // Little bananas, one per Onkey (up to ten).
            for (int i = 0; i < Math.Min(n, 10); i++)
                Banana(g, new PointF(Bounds.Left + 168 + (i % 5) * 20, Bounds.Top + 10 + (i / 5) * 18), i);
        }

        private void Coconut(Graphics g, PointF c, string sign, bool enabled, bool down)
        {
            using (GraphicsPath shell = Sketch.Circle(c, down ? 15 : 17, (int)c.X, 1f))
            {
                using (Brush b = new SolidBrush(Dim(Jungle.Bark, enabled))) g.FillPath(b, shell);
                Sketch.Stroke(g, shell, Dim(Jungle.Ink, enabled), 2f);
            }
            using (Pen p = new Pen(Dim(Jungle.Paper, enabled), 3.2f))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawLine(p, c.X - 7, c.Y, c.X + 7, c.Y + 0.6f);
                if (sign == "+") g.DrawLine(p, c.X + 0.4f, c.Y - 7, c.X, c.Y + 7);
            }
        }

        private static void Banana(Graphics g, PointF at, int seed)
        {
            using (GraphicsPath b = new GraphicsPath())
            {
                b.AddBezier(at.X, at.Y, at.X + 4, at.Y + 12, at.X + 12, at.Y + 12, at.X + 16, at.Y + 4);
                b.AddBezier(at.X + 16, at.Y + 4, at.X + 11, at.Y + 8, at.X + 5, at.Y + 7, at.X, at.Y);
                using (Brush fill = new SolidBrush(Jungle.Banana)) g.FillPath(fill, b);
                using (Pen ink = new Pen(Jungle.Ink, 1.3f)) g.DrawPath(ink, b);
            }
        }

        private int SideAt(PointF p)
        {
            if (Distance(p, Minus) < 20) return -1;
            if (Distance(p, Plus) < 20) return 1;
            return 0;
        }

        private static float Distance(PointF a, PointF b) { return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }

        public override RectangleF HitArea() { return new RectangleF(Bounds.Left, Bounds.Top, 152, Bounds.Height); }
        public override void Down(PointF p) { pressedSide = SideAt(p); }
        public override void Up(PointF p)
        {
            int side = SideAt(p);
            if (side != 0 && side == pressedSide) set(Math.Max(min, Math.Min(max, get() + side)));
            pressedSide = 0;
        }
    }

    // A wooden sign to click.
    internal sealed class SketchButton : Widget
    {
        public Func<string> Text;
        private readonly Action click;
        private bool down;

        public SketchButton(string text, Action click) { Text = delegate { return text; }; this.click = click; }

        public override float Measure(float width) { return 40; }

        private RectangleF Sign(Graphics g)
        {
            float w = g.MeasureString(Text(), Form.LabelFont).Width + 36;
            return new RectangleF(Bounds.Left, Bounds.Top, w, Bounds.Height);
        }

        public override RectangleF HitArea()
        {
            using (Bitmap b = new Bitmap(1, 1)) using (Graphics g = Graphics.FromImage(b)) return Sign(g);
        }

        public override void Paint(Graphics g, bool enabled, bool hover)
        {
            RectangleF r = Sign(g);
            if (down) r.Offset(1, 2);
            using (GraphicsPath path = Sketch.Box(r, 8, Sketch.Seed(Bounds), 1.4f))
            {
                using (Brush b = new SolidBrush(Dim(hover ? Color.FromArgb(250, 212, 92) : Jungle.Banana, enabled))) g.FillPath(b, path);
                Sketch.Stroke(g, path, Dim(Jungle.Ink, enabled), 2.2f);
            }
            SizeF size = g.MeasureString(Text(), Form.LabelFont);
            Jungle.Text(g, Text(), Form.LabelFont, Dim(Jungle.Ink, enabled), new PointF(r.Left + (r.Width - size.Width) / 2, r.Top + (r.Height - size.Height) / 2));
        }

        public override void Down(PointF p) { down = true; }
        public override void Up(PointF p)
        {
            down = false;
            if (HitArea().Contains(p)) click();
        }
    }

    // A line of text that can change, like the update status.
    internal sealed class Note : Widget
    {
        private readonly Func<string> text;
        public Note(Func<string> text) { this.text = text; }
        public override bool Interactive { get { return false; } }
        public override float Measure(float width) { return 26; }
        public override void Paint(Graphics g, bool enabled, bool hover)
        {
            Jungle.Text(g, text(), Form.LabelFont, Jungle.Ink, new PointF(Bounds.Left, Bounds.Top + 1));
        }
    }
}
