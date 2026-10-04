using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace OnkeyDesktopPet
{
    // The settings drop-down: a hand-drawn jungle panel that pops up from the tray icon, with
    // quick switches along the top and a tab for each group of settings. Everything is painted
    // here (sketchy lines, vine sliders, leaf switches) rather than built from stock controls,
    // and every change takes effect straight away. It closes when you click anywhere else.
    internal sealed class SettingsPanel : Form
    {
        public const float PageWidth = 380, PageHeight = 680;
        // The card the settings sit on, and the columns inside it.
        public const float CardLeft = 12, CardTop = 124, CardRight = PageWidth - 12, CardBottom = PageHeight - 12;
        public const float LabelLeft = 30, ControlRight = CardRight - 18;
        private const float FooterTop = CardBottom - 50;

        private readonly OnkeyApp app;
        private readonly Updater updater;
        private readonly float scale;
        private readonly List<Page> pages = new List<Page>();
        private readonly List<Widget> always = new List<Widget>();   // The quick switches and footer.
        private int current;
        private Widget pressed, hovered;
        private Bitmap backdrop, head;
        private string headSkin;   // The skin his head at the top was drawn in.
        private readonly Timer animator = new Timer();
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private double fade, fadeTarget = 1;   // 0 hidden ... 1 fully shown.
        private Point home;
        private Size slideFrom;
        public readonly Font TitleFont, TabFont, LabelFont, ChipFont, SmallFont;

        private sealed class Page
        {
            public string Name;
            public RectangleF Tab;
            public readonly List<Widget> Widgets = new List<Widget>();
        }

        public SettingsPanel(OnkeyApp app, Updater updater)
        {
            this.app = app;
            this.updater = updater;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Text = "Onkey";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size((int)Math.Ceiling(PageWidth * scale), (int)Math.Ceiling(PageHeight * scale));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Jungle.Night;
            KeyPreview = true;
            using (GraphicsPath corners = Rounded(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 14 * scale))
                Region = new Region(corners);

            FontFamily hand = Jungle.HandFont();
            TitleFont = new Font(hand, 22, FontStyle.Bold, GraphicsUnit.Pixel);
            TabFont = new Font(hand, 14, FontStyle.Bold, GraphicsUnit.Pixel);
            LabelFont = new Font(hand, 15, FontStyle.Bold, GraphicsUnit.Pixel);
            ChipFont = new Font(hand, 13, FontStyle.Bold, GraphicsUnit.Pixel);
            SmallFont = new Font(hand, 12.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            head = app.RenderHead((int)Math.Round(40 * scale));
            headSkin = app.Skin.Id;

            BuildPages();
            app.SettingsChanged += Changed;
            animator.Interval = 15;
            animator.Tick += delegate { Animate(); };
        }

        // Shows the panel beside the tray, on whichever side of the screen the taskbar is.
        public void ShowNear(Point cursor)
        {
            Rectangle work = Screen.FromPoint(cursor).WorkingArea;
            int w = Width, h = Height, gap = (int)(8 * scale);
            int x = Math.Max(work.Left + gap, Math.Min(cursor.X - w / 2, work.Right - w - gap));
            int y = Math.Max(work.Top + gap, Math.Min(cursor.Y - h / 2, work.Bottom - h - gap));
            if (cursor.Y >= work.Bottom) y = work.Bottom - h - gap;
            else if (cursor.Y < work.Top) y = work.Top + gap;
            else if (cursor.X >= work.Right) x = work.Right - w - gap;
            else if (cursor.X < work.Left) x = work.Left + gap;
            home = new Point(x, y);
            // It glides in from the taskbar's side (from below, for a taskbar along the bottom).
            int lift = (int)(12 * scale);
            slideFrom = cursor.Y >= work.Bottom ? new Size(0, lift) : cursor.Y < work.Top ? new Size(0, -lift)
                      : cursor.X >= work.Right ? new Size(lift, 0) : cursor.X < work.Left ? new Size(-lift, 0) : new Size(0, lift);
            Opacity = 0;
            Location = home + slideFrom;
            Show();
            Activate();
            fade = 0;
            fadeTarget = 1;
            clock.Restart();
            animator.Start();
        }

        // Clicking anywhere else closes it, like a menu.
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Wait until the new window is active: if it's one of Onkey's own (a pet changing in
            // response to a switch here), take focus back rather than closing.
            BeginInvoke((MethodInvoker)delegate
            {
                if (IsDisposed || FadingOut) return;
                Form now = Form.ActiveForm;
                if (now == this) return;
                if (now != null || pressed != null) Activate();
                else FadeOut();
            });
        }

        // True while it's fading away; it's as good as closed.
        public bool FadingOut { get { return fadeTarget == 0; } }

        // Fades and slides back toward the taskbar, then closes.
        public void FadeOut()
        {
            if (FadingOut || IsDisposed) return;
            fadeTarget = 0;
            animator.Start();
        }

        // Runs the fade and the switches' slides, about 60 times a second while the panel is open.
        private void Animate()
        {
            double dt = Math.Min(0.05, clock.Elapsed.TotalSeconds);
            clock.Restart();
            if (fade != fadeTarget)
            {
                // About 0.18 s in and 0.13 s out, easing at the end.
                fade = fadeTarget > fade ? Math.Min(1, fade + dt / 0.18) : Math.Max(0, fade - dt / 0.13);
                double eased = 1 - Math.Pow(1 - fade, 3);
                Opacity = eased;
                Location = new Point(home.X + (int)Math.Round(slideFrom.Width * (1 - eased)),
                                     home.Y + (int)Math.Round(slideFrom.Height * (1 - eased)));
                if (fade == 0) { animator.Stop(); Close(); return; }
            }
            bool moving = false;
            foreach (Widget w in always) moving |= w.Animate(dt);
            foreach (Widget w in pages[current].Widgets) moving |= w.Animate(dt);
            if (moving) Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ClassStyle |= 0x20000;   // CS_DROPSHADOW
                return p;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                app.SettingsChanged -= Changed;
                animator.Dispose();
                if (backdrop != null) backdrop.Dispose();
                if (head != null) head.Dispose();
                TitleFont.Dispose(); TabFont.Dispose(); LabelFont.Dispose(); ChipFont.Dispose(); SmallFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void Changed()
        {
            if (IsDisposed) return;
            if (headSkin != app.Skin.Id)
            {
                // Redrawn in place, since the update card shows the same picture.
                using (Bitmap fresh = app.RenderHead(head.Width))
                using (Graphics g = Graphics.FromImage(head))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(fresh, 0, 0);
                }
                headSkin = app.Skin.Id;
            }
            Invalidate();
        }

        // Pages

        private void BuildPages()
        {
            // Quick switches along the top, under his name.
            float qx = 18;
            foreach (QuickToggle q in new QuickToggle[] {
                new QuickToggle("Pause", delegate { return app.Paused; }, delegate { app.TogglePause(); }),
                new QuickToggle("Dance", delegate { return app.Settings.Bool("dance"); }, delegate { Flip("dance"); }),
                new QuickToggle("Drag him", delegate { return app.Settings.Bool("draggable"); }, delegate { Flip("draggable"); }) })
            {
                q.Form = this;
                float w = q.Width();
                q.Bounds = new RectangleF(qx, 56, w, 28);
                qx += w + 8;
                always.Add(q);
            }
            SketchButton quit = new SketchButton("Exit Onkey", delegate { animator.Stop(); Close(); app.Exit(); });
            quit.Form = this;
            quit.Small = true;
            quit.Bounds = new RectangleF(LabelLeft, FooterTop + 12, 0, 30);
            always.Add(quit);
            // Puts a crash report (or any problem) into a GitHub issue to check and send. After
            // a crash it says so, with a berry on it.
            SketchButton report = new SketchButton("", delegate { CrashReport.Send(); });
            report.Text = delegate { return CrashReport.Pending ? "Send crash report" : "Report a problem"; };
            report.Flag = delegate { return CrashReport.Pending; };
            report.Form = this;
            report.Small = true;
            report.Bounds = new RectangleF(quit.HitArea().Right + 8, FooterTop + 12, 0, 30);
            always.Add(report);

            Page moves = AddPage("Moves");
            Stepper count = new Stepper(1, 20, delegate { return (int)app.Settings.Number("count"); },
                delegate(int v) { app.Change("count", v); });
            Inline(moves, "How many Onkeys", count, delegate
            {
                int n = (int)app.Settings.Number("count");
                return n <= 1 ? "just the one" : n >= 8 ? "total chaos" : "a little troop";
            });
            Stacked(moves, "Where he roams", Choices("zone",
                "Anywhere", "anywhere", "Bottom", "bottom", "Top", "top",
                "Left side", "left", "Right side", "right", "Stay put", "stay"), null);
            // "chase" is how many trips out of N head for the mouse (0 for never).
            string[] chaseValues = { "0", "5", "2", "1" }, chaseNames = { "never", "sometimes", "often", "always" };
            Slider chase = new Slider(0, 3, delegate { return Math.Max(0, Array.IndexOf(chaseValues, app.Settings.Get("chase"))); },
                delegate(double v) { app.Change("chase", chaseValues[(int)Math.Round(v)]); }, false);
            chase.Snap = 1;
            Stacked(moves, "Walks to my mouse", chase, delegate
            {
                return chaseNames[Math.Max(0, Array.IndexOf(chaseValues, app.Settings.Get("chase")))];
            });
            Stacked(moves, "Walking speed", NumberSlider("speed", 10, 200, false), delegate
            {
                double v = app.Settings.Number("speed");
                return v < 30 ? "a slow stroll" : v < 60 ? "normal" : v < 120 ? "fast" : "zoomies!";
            });
            Inline(moves, "Floppy arms", Switch("floppyArms"), delegate { return "they dangle when you carry him"; });
            Full(moves, new SketchButton("Bring Onkey to this screen", delegate { app.BringHere(); }));

            Page sound = AddPage("Sound");
            Func<bool> soundOn = delegate { return app.HasSound && app.Settings.Bool("soundOn"); };
            Toggle soundSwitch = Switch("soundOn");
            soundSwitch.Enabled = delegate { return app.HasSound; };
            Inline(sound, "Sound", soundSwitch, delegate { return app.HasSound ? "his oooo now and then" : "sound clip not installed"; });
            Slider gap = new Slider(0, GapSteps.Length - 1, delegate { return GapIndex(app.Settings.Number("soundGap")); },
                delegate(double v) { app.Change("soundGap", GapSteps[(int)Math.Round(v)]); }, false);
            gap.Snap = 1;
            gap.Enabled = soundOn;
            Stacked(sound, "How often", gap, delegate { return Every(app.Settings.Number("soundGap")); });
            Slider volume = NumberSlider("volume", 0.05, 1, false);
            volume.Enabled = soundOn;
            Stacked(sound, "Volume", volume, delegate { return Percent(app.Settings.Number("volume")); });
            SketchButton play = new SketchButton("Say oooo now", delegate { app.PlayNow(); });
            play.Enabled = delegate { return app.HasSound; };
            Full(sound, play);

            Page look = AddPage("Look");
            string[] skins = new string[Skin.All.Length * 2];
            for (int i = 0; i < Skin.All.Length; i++) { skins[i * 2] = Skin.All[i].Name; skins[i * 2 + 1] = Skin.All[i].Id; }
            Stacked(look, "Skin", Choices("skin", skins), delegate { return app.Skin.Caption; });
            // Re-rendering every frame is too slow to follow the mouse, so size waits for the drop.
            Stacked(look, "Size", NumberSlider("size", 0.4, 3, true), delegate { return Percent(app.Settings.Number("size")); });
            Stacked(look, "See-through", NumberSlider("opacity", 0.2, 1, false), delegate
            {
                double v = app.Settings.Number("opacity");
                return v > 0.95 ? "solid" : v < 0.45 ? "ghostly" : Percent(v) + " solid";
            });
            Stacked(look, "Where he lives", Choices("layer", "In front", "above", "On the desktop", "desktop"), null);
            Toggle fullScreen = Switch("overFullScreen");
            fullScreen.Enabled = delegate { return app.Settings.Get("layer") != "desktop"; };
            Inline(look, "Over full-screen apps", fullScreen, delegate { return "videos, games, slideshows"; });
            Inline(look, "Watch my cursor", Switch("watchCursor"), delegate { return "his eyes follow the mouse"; });
            Inline(look, "Blink", Switch("blink"), delegate { return "now and then"; });
            Inline(look, "Sketchy arms", Switch("sketchy"), delegate { return "lines wiggle like a drawing"; });

            Page updates = AddPage("Updates");
            Full(updates, new UpdateCard(updater, head));
            Inline(updates, "Check by himself", Switch("checkUpdates"), delegate { return "looks every few hours"; });
            Inline(updates, "Open at startup", new Toggle(delegate { return app.OpensAtLogin; }, delegate(bool on) { app.ToggleLogin(); }),
                delegate { return "when Windows starts"; });
        }

        private void Flip(string key) { app.Change(key, app.Settings.Bool(key) ? "false" : "true"); }

        private Page AddPage(string name)
        {
            Page page = new Page();
            page.Name = name;
            float width = 78, gap = 6;
            page.Tab = new RectangleF(CardLeft + 10 + pages.Count * (width + gap), CardTop - 32, width, 36);
            pages.Add(page);
            return page;
        }

        private float NextTop(Page page)
        {
            return page.Widgets.Count == 0 ? CardTop + 20 : page.Widgets[page.Widgets.Count - 1].RowBottom + 10;
        }

        // Label (and note) on the left, a small control on the right.
        private void Inline(Page page, string label, Widget widget, Func<string> caption)
        {
            float top = NextTop(page);
            Place(page, widget, label, caption, top, top + 40, 0);
        }

        // Label on one line with its note at the right, and the control across the card below it.
        private void Stacked(Page page, string label, Widget widget, Func<string> caption)
        {
            float top = NextTop(page);
            widget.Stacked = true;
            Place(page, widget, label, caption, top, top + 24, 24);
        }

        private void Full(Page page, Widget widget)
        {
            float top = NextTop(page);
            Place(page, widget, null, null, top, top, 0);
        }

        private void Place(Page page, Widget widget, string label, Func<string> caption, float top, float controlTop, float labelHeight)
        {
            widget.Form = this;
            widget.Label = label;
            widget.Caption = caption;
            widget.RowTop = top;
            float width = ControlRight - LabelLeft;
            float height = widget.Measure(width);
            float natural = widget.NaturalWidth;
            if (label != null && labelHeight == 0)
            {
                // Inline: centre the control against the label and its note.
                widget.Bounds = new RectangleF(ControlRight - natural, top + (40 - height) / 2, natural, height);
                widget.RowBottom = top + Math.Max(40, height);
            }
            else
            {
                widget.Bounds = new RectangleF(LabelLeft, controlTop, natural > 0 ? natural : width, height);
                widget.RowBottom = controlTop + height;
            }
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
            if (gap * 2 < 120) return "every " + gap + "-" + gap * 2 + " sec";
            return "every " + Minutes(gap) + "-" + Minutes(gap * 2) + " min";
        }

        private static string Minutes(double seconds)
        {
            double m = seconds / 60;
            if (Math.Abs(m - Math.Floor(m) - 0.5) < 0.01) return Math.Floor(m) > 0 ? Math.Floor(m) + "½" : "½";
            return Math.Round(m).ToString(CultureInfo.InvariantCulture);
        }

        private static string Percent(double v) { return Math.Round(v * 100) + "%"; }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

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
            g.DrawImage(head, (int)(14 * scale), (int)(9 * scale));

            Jungle.Prepare(g);
            g.ScaleTransform(scale, scale);
            Jungle.Text(g, "Onkey", TitleFont, Jungle.Paper, new PointF(60, 14));
            using (GraphicsPath edge = Rounded(new RectangleF(0.5f, 0.5f, PageWidth - 1, PageHeight - 1), 14))
            using (Pen ink = new Pen(Jungle.Ink, 2)) g.DrawPath(ink, edge);

            for (int i = 0; i < pages.Count; i++) if (i != current) DrawTab(g, pages[i], false);
            Jungle.Card(g, RectangleF.FromLTRB(CardLeft, CardTop, CardRight, CardBottom), 11);
            DrawTab(g, pages[current], true);
            using (Pen rule = new Pen(Color.FromArgb(90, Jungle.Ink), 1.2f))
                g.DrawCurve(rule, Sketch.Wiggle(new PointF(LabelLeft, FooterTop), new PointF(ControlRight, FooterTop), 4, 1.2f));
            Jungle.Text(g, "v" + Program.Version, SmallFont, Jungle.Faded, new PointF(ControlRight - 40, FooterTop + 18));

            foreach (Widget w in always) w.Paint(g, w.Enabled(), w == hovered);
            foreach (Widget w in pages[current].Widgets)
            {
                bool enabled = w.Enabled();
                if (w.Label != null)
                {
                    float y = w.Stacked ? w.RowTop : w.RowTop + 20 - (w.Caption != null ? 18 : 10);
                    Jungle.Text(g, w.Label, LabelFont, enabled ? Jungle.Ink : Jungle.Faded, new PointF(LabelLeft, y));
                    if (w.Caption != null)
                    {
                        string caption = w.Caption();
                        if (w.Stacked)
                        {
                            SizeF size = g.MeasureString(caption, SmallFont);
                            Jungle.Text(g, caption, SmallFont, Jungle.Faded, new PointF(ControlRight - size.Width, y + 3));
                        }
                        else Jungle.Text(g, caption, SmallFont, Jungle.Faded, new PointF(LabelLeft + 1, y + 19));
                    }
                }
                w.Paint(g, enabled, w == hovered && enabled);
            }
        }

        private void DrawTab(Graphics g, Page page, bool selected)
        {
            RectangleF r = page.Tab;
            if (!selected) r = new RectangleF(r.X, r.Y + 5, r.Width, r.Height - 5);
            int seed = (int)r.X * 7 + (selected ? 1 : 0);
            using (GraphicsPath path = Sketch.Tab(r, seed))
            {
                using (Brush fill = new SolidBrush(selected ? Jungle.Paper : Jungle.Bark)) g.FillPath(fill, path);
                if (!selected)
                {
                    // Wood grain.
                    using (Pen grain = new Pen(Color.FromArgb(70, Jungle.Ink), 1))
                        for (int i = 1; i < 3; i++)
                            g.DrawCurve(grain, Sketch.Wiggle(new PointF(r.Left + 8, r.Top + i * r.Height / 3), new PointF(r.Right - 8, r.Top + i * r.Height / 3 + 2), seed + i, 1.2f));
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
            if (pages.IndexOf(page) == UpdatesPage && updater.Available != null)
            {
                // A banana on the Updates tab while there's a new Onkey to get.
                using (GraphicsPath dot = Sketch.Circle(new PointF(r.Right - 5, r.Top + 3), 5f, 5, 0.5f))
                {
                    using (Brush b = new SolidBrush(Jungle.Banana)) g.FillPath(b, dot);
                    using (Pen ink = new Pen(Jungle.Ink, 1.2f)) g.DrawPath(ink, dot);
                }
            }
        }

        // Mouse and keys

        private PointF Logical(MouseEventArgs e) { return new PointF(e.X / scale, e.Y / scale); }

        private Widget WidgetAt(PointF p)
        {
            foreach (Widget w in always)
                if (w.Interactive && w.Enabled() && w.HitArea().Contains(p)) return w;
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
            if (!IsDisposed) Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hovered != null) { hovered = null; Invalidate(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) FadeOut();
            else if (e.Control && e.KeyCode == Keys.Tab) SelectPage((current + (e.Shift ? pages.Count - 1 : 1)) % pages.Count);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            PointF p = Logical(e);
            foreach (Widget w in pages[current].Widgets)
                if (w.Wheel(p, e.Delta)) { Invalidate(); return; }
        }

        private void SelectPage(int index)
        {
            current = index;
            hovered = null;
            if (index == UpdatesPage) updater.CheckIfStale();
            Invalidate();
        }

        private const int UpdatesPage = 3;

        // Opens the Updates tab, where the whole update happens.
        public void ShowUpdates() { SelectPage(UpdatesPage); }
    }

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
        public SettingsPanel Form;
        public string Label;
        public bool Stacked;   // Label above the control, rather than beside it.
        public Func<string> Caption;
        public RectangleF Bounds;
        public float RowTop, RowBottom;
        public Func<bool> Enabled = delegate { return true; };
        public virtual bool Interactive { get { return true; } }
        // How wide the control is, or -1 to fill the row.
        public virtual float NaturalWidth { get { return -1; } }

        // The height this widget needs at the given width.
        public abstract float Measure(float width);
        public abstract void Paint(Graphics g, bool enabled, bool hover);
        public virtual RectangleF HitArea() { return Bounds; }
        public virtual void Down(PointF p) { }
        public virtual void Drag(PointF p) { }
        public virtual void Up(PointF p) { }
        // Moves any animation on by dt seconds; true while it still needs redrawing.
        public virtual bool Animate(double dt) { return false; }
        // The mouse wheel turned over the page; true if this widget used it.
        public virtual bool Wheel(PointF p, int delta) { return false; }

        // Eases `shown` toward 0 or 1 (about 0.15 s for the whole way); true while it's moving.
        protected static bool Ease(ref float shown, bool on, double dt)
        {
            float target = on ? 1 : 0;
            if (shown < 0 || Math.Abs(shown - target) < 0.004f) { shown = target; return false; }
            shown += (target - shown) * (float)Math.Min(1, dt * 16);
            return true;
        }

        protected static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        protected static Color Dim(Color c, bool enabled) { return enabled ? c : Color.FromArgb(90, c); }
    }

    // An on/off switch: a sketched pod with a leaf that slides across and turns green.
    internal sealed class Toggle : Widget
    {
        private readonly Func<bool> get;
        private readonly Action<bool> set;
        private const float W = 62, H = 30;
        private float shown = -1;   // Where the knob is drawn: 0 off ... 1 on.

        public Toggle(Func<bool> get, Action<bool> set) { this.get = get; this.set = set; }

        public override float Measure(float width) { return H; }
        public override float NaturalWidth { get { return W; } }
        // The whole row, label included.
        public override RectangleF HitArea() { return RectangleF.FromLTRB(SettingsPanel.LabelLeft, RowTop, Bounds.Right, RowBottom); }

        public override bool Animate(double dt) { return Ease(ref shown, get(), dt); }

        public override void Paint(Graphics g, bool enabled, bool hover)
        {
            float t = shown < 0 ? (get() ? 1 : 0) : shown;
            RectangleF pod = new RectangleF(Bounds.Left, Bounds.Top, W, H);
            int seed = Sketch.Seed(pod);
            using (GraphicsPath path = Sketch.Box(pod, H / 2, seed, 1.1f))
            {
                using (Brush b = new SolidBrush(Dim(Mix(Color.FromArgb(222, 208, 172), Jungle.Leaf, t), enabled))) g.FillPath(b, path);
                Sketch.Stroke(g, path, Dim(Jungle.Ink, enabled), hover ? 2.6f : 2f);
            }
            float left = pod.Left + H / 2 + 1, right = pod.Right - H / 2 - 1;
            float knobX = left + (right - left) * t;
            // The knob keeps its wobble as it slides, rather than re-shaking every frame.
            using (GraphicsPath knob = Sketch.Circle(new PointF(left, pod.Top + H / 2), H / 2 - 4, seed + 3, 0.8f))
            using (Matrix move = new Matrix())
            {
                move.Translate(knobX - left, 0);
                knob.Transform(move);
                using (Brush b = new SolidBrush(Dim(Mix(Jungle.Paper, Jungle.Banana, t), enabled))) g.FillPath(b, knob);
                Sketch.Stroke(g, knob, Dim(Jungle.Ink, enabled), 1.8f);
            }
            if (t > 0.05f)
                Jungle.DrawLeaf(g, new PointF(knobX - 5, pod.Top + H / 2 + 2), -40, 13 * t, 6 * t, Dim(Color.FromArgb((int)(255 * t), Jungle.LeafDark), enabled), seed);
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

        public override float Measure(float width) { return 36; }
        public override float NaturalWidth { get { return 118; } }
        private PointF Minus { get { return new PointF(Bounds.Left + 17, Bounds.Top + 18); } }
        private PointF Plus { get { return new PointF(Bounds.Right - 17, Bounds.Top + 18); } }

        public override void Paint(Graphics g, bool enabled, bool hover)
        {
            int n = get();
            Coconut(g, Minus, "-", (n > min), (pressedSide < 0));
            Coconut(g, Plus, "+", (n < max), (pressedSide > 0));
            string text = n.ToString(CultureInfo.InvariantCulture);
            SizeF size = g.MeasureString(text, Form.TitleFont);
            Jungle.Text(g, text, Form.TitleFont, Jungle.Ink, new PointF(Bounds.Left + Bounds.Width / 2 - size.Width / 2, Bounds.Top + 18 - size.Height / 2));
        }

        private void Coconut(Graphics g, PointF c, string sign, bool enabled, bool down)
        {
            using (GraphicsPath shell = Sketch.Circle(c, down ? 13 : 15, (int)c.X, 1f))
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

        private int SideAt(PointF p)
        {
            if (Distance(p, Minus) < 20) return -1;
            if (Distance(p, Plus) < 20) return 1;
            return 0;
        }

        private static float Distance(PointF a, PointF b) { return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }

        public override RectangleF HitArea() { return Bounds; }
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

        public bool Small;
        // When true, a berry sits on its corner to catch the eye.
        public Func<bool> Flag;

        public SketchButton(string text, Action click) { Text = delegate { return text; }; this.click = click; }

        public override float Measure(float width) { return Small ? 30 : 38; }
        private Font Font { get { return Small ? Form.ChipFont : Form.LabelFont; } }

        private RectangleF Sign(Graphics g)
        {
            float w = g.MeasureString(Text(), Font).Width + (Small ? 22 : 36);
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
            SizeF size = g.MeasureString(Text(), Font);
            Jungle.Text(g, Text(), Font, Dim(Jungle.Ink, enabled), new PointF(r.Left + (r.Width - size.Width) / 2, r.Top + (r.Height - size.Height) / 2));
            if (Flag != null && Flag())
                using (GraphicsPath dot = Sketch.Circle(new PointF(r.Right - 3, r.Top + 2), 5f, 7, 0.5f))
                {
                    using (Brush b = new SolidBrush(Color.FromArgb(214, 84, 52))) g.FillPath(b, dot);
                    using (Pen ink = new Pen(Jungle.Ink, 1.2f)) g.DrawPath(ink, dot);
                }
        }

        public override void Down(PointF p) { down = true; }
        public override void Up(PointF p)
        {
            down = false;
            if (HitArea().Contains(p)) click();
        }
    }

    // The whole update, in one sketched box: looking for a new Onkey, what's new in it, the
    // download growing along a vine, and restarting. Each step fades in over the last.
    internal sealed class UpdateCard : Widget
    {
        private static readonly Color Fill = Color.FromArgb(234, 222, 191);
        private readonly Updater updater;
        private readonly Bitmap head;
        private readonly SketchButton checkAgain, update, tryAgain;
        private SketchButton button, pressedButton;   // The button showing now, and the one held down.
        private Updater.Phase shownPhase = (Updater.Phase)(-1);
        private float appear = 1, scroll, notesHeight, progress;
        private double time;
        private RectangleF notesArea;
        private bool hovering;

        public UpdateCard(Updater updater, Bitmap head)
        {
            this.updater = updater;
            this.head = head;
            checkAgain = new SketchButton("Check again", delegate { updater.Check(); });
            checkAgain.Small = true;
            tryAgain = new SketchButton("Try again", delegate { updater.Check(); });
            tryAgain.Small = true;
            update = new SketchButton("Update and restart", delegate { updater.Install(); });
        }

        public override float Measure(float width) { return 236; }

        public override bool Animate(double dt)
        {
            time += dt;
            bool moving = false;
            if (updater.State != shownPhase) { shownPhase = updater.State; appear = 0; scroll = 0; progress = 0; }
            if (appear < 1) { appear = Math.Min(1, appear + (float)(dt / 0.25)); moving = true; }
            float target = (float)updater.Progress;
            if (Math.Abs(progress - target) > 0.002f) { progress += (target - progress) * (float)Math.Min(1, dt * 10); moving = true; }
            Updater.Phase p = updater.State;
            return moving || p == Updater.Phase.Idle || p == Updater.Phase.Checking || p == Updater.Phase.Restarting;
        }

        public override RectangleF HitArea() { return button == null ? RectangleF.Empty : button.HitArea(); }
        public override void Down(PointF p) { pressedButton = button; if (button != null) button.Down(p); }
        public override void Up(PointF p) { if (pressedButton != null) pressedButton.Up(p); pressedButton = null; }

        public override bool Wheel(PointF p, int delta)
        {
            if (updater.State != Updater.Phase.Available || !notesArea.Contains(p)) return false;
            scroll = Math.Max(0, Math.Min(Math.Max(0, notesHeight - notesArea.Height), scroll - delta / 120f * 30));
            return true;
        }

        public override void Paint(Graphics g, bool enabled, bool hover)
        {
            RectangleF r = Bounds;
            using (GraphicsPath box = Sketch.Box(r, 12, 21, 1.2f))
            {
                using (Brush b = new SolidBrush(Fill)) g.FillPath(b, box);
                Sketch.Stroke(g, box, Jungle.Ink, 1.6f);
            }
            hovering = hover;
            button = null;
            string version = updater.Available != null ? updater.Available.Version : "";
            float mid = r.Top + r.Height / 2;
            switch (updater.State)
            {
                case Updater.Phase.Idle:
                case Updater.Phase.Checking:
                    Coconuts(g, new PointF(r.Left + r.Width / 2, mid - 22));
                    Centered(g, "Looking for a new Onkey...", Form.LabelFont, Jungle.Ink, mid + 18);
                    Centered(g, "You have Onkey " + Program.Version + ".", Form.SmallFont, Jungle.Faded, mid + 44);
                    break;
                case Updater.Phase.UpToDate:
                    g.DrawImage(head, new RectangleF(r.Left + r.Width / 2 - 26, mid - 84, 52, 52));
                    Centered(g, "You're up to date!", Form.LabelFont, Jungle.Ink, mid - 22);
                    Centered(g, "Onkey " + Program.Version + " is the newest one.", Form.SmallFont, Jungle.Faded, mid + 4);
                    Place(g, checkAgain, mid + 32);
                    break;
                case Updater.Phase.Available:
                    Jungle.Text(g, "Onkey " + version + " is out!", Form.LabelFont, Jungle.Ink, new PointF(r.Left + 14, r.Top + 12));
                    string have = "you have " + Program.Version;
                    SizeF haveSize = g.MeasureString(have, Form.SmallFont);
                    Jungle.Text(g, have, Form.SmallFont, Jungle.Faded, new PointF(r.Right - 14 - haveSize.Width, r.Top + 16));
                    Notes(g, RectangleF.FromLTRB(r.Left + 14, r.Top + 42, r.Right - 14, r.Bottom - 58));
                    Place(g, update, r.Bottom - 50);
                    break;
                case Updater.Phase.Downloading:
                    Centered(g, "Downloading Onkey " + version + "...", Form.LabelFont, Jungle.Ink, mid - 50);
                    Vine(g, RectangleF.FromLTRB(r.Left + 20, mid - 17, r.Right - 20, mid + 17), progress);
                    Centered(g, Math.Round(progress * 100) + "%", Form.SmallFont, Jungle.Faded, mid + 24);
                    break;
                case Updater.Phase.Restarting:
                    Coconuts(g, new PointF(r.Left + r.Width / 2, mid - 22));
                    Centered(g, "Restarting with Onkey " + version + "...", Form.LabelFont, Jungle.Ink, mid + 18);
                    Centered(g, "Your settings come with him.", Form.SmallFont, Jungle.Faded, mid + 44);
                    break;
                case Updater.Phase.Failed:
                    Centered(g, "Hmm, that didn't work", Form.LabelFont, Jungle.Ink, r.Top + 40);
                    using (StringFormat centre = new StringFormat())
                    using (Brush faded = new SolidBrush(Jungle.Faded))
                    {
                        centre.Alignment = StringAlignment.Center;
                        g.DrawString(updater.Problem, Form.SmallFont, faded, RectangleF.FromLTRB(r.Left + 18, r.Top + 72, r.Right - 18, r.Top + 126), centre);
                    }
                    Place(g, tryAgain, r.Top + 132);
                    break;
            }
            if (appear < 1)
            {
                // Fade the new step in by laying the card's colour over it and lifting it away.
                using (Brush veil = new SolidBrush(Color.FromArgb((int)((1 - appear) * 255), Fill)))
                    g.FillRectangle(veil, RectangleF.Inflate(r, -4, -4));
            }
        }

        private void Centered(Graphics g, string text, Font font, Color color, float top)
        {
            SizeF size = g.MeasureString(text, font);
            Jungle.Text(g, text, font, color, new PointF(Bounds.Left + (Bounds.Width - size.Width) / 2, top));
        }

        // Shows a button centred across the card.
        private void Place(Graphics g, SketchButton b, float top)
        {
            b.Form = Form;
            b.Bounds = new RectangleF(Bounds.Left, top, 0, b.Measure(0));
            float w = b.HitArea().Width;
            b.Bounds = new RectangleF(Bounds.Left + (Bounds.Width - w) / 2, top, 0, b.Measure(0));
            b.Paint(g, true, hovering);
            button = b;
        }

        // What's new, scrolled with the mouse wheel when it's long.
        private void Notes(Graphics g, RectangleF area)
        {
            notesArea = area;
            Updater.Release release = updater.Available;
            string text = release == null || release.Notes.Length == 0 ? "A new version of Onkey." : release.Notes;
            if (updater.Moving) text = "He'll move into " + updater.InstallFolder() + ".\n\n" + text;
            notesHeight = g.MeasureString(text, Form.SmallFont, (int)area.Width - 8).Height;
            scroll = Math.Max(0, Math.Min(Math.Max(0, notesHeight - area.Height), scroll));
            GraphicsState state = g.Save();
            g.SetClip(area);
            using (Brush ink = new SolidBrush(Jungle.Ink))
                g.DrawString(text, Form.SmallFont, ink, new RectangleF(area.Left, area.Top - scroll, area.Width - 8, notesHeight + 4));
            g.Restore(state);
            if (notesHeight > area.Height)
            {
                // A thin vine down the side shows where you are in the notes.
                float thumb = Math.Max(20, area.Height * area.Height / notesHeight);
                float at = area.Top + (area.Height - thumb) * scroll / (notesHeight - area.Height);
                using (Pen track = new Pen(Color.FromArgb(70, Jungle.Ink), 2)) g.DrawLine(track, area.Right - 2, area.Top, area.Right - 2, area.Bottom);
                using (Pen bar = new Pen(Jungle.Vine, 4)) { bar.StartCap = bar.EndCap = LineCap.Round; g.DrawLine(bar, area.Right - 2, at, area.Right - 2, at + thumb); }
            }
        }

        // Three coconuts bouncing one after another: the loading animation.
        private void Coconuts(Graphics g, PointF centre)
        {
            float ground = centre.Y + 20;
            using (Pen vine = new Pen(Jungle.Vine, 3)) { vine.StartCap = vine.EndCap = LineCap.Round; g.DrawCurve(vine, Sketch.Wiggle(new PointF(centre.X - 54, ground + 4), new PointF(centre.X + 54, ground + 4), 9, 1.5f)); }
            for (int i = 0; i < 3; i++)
            {
                double phase = time * 2.2 - i * 0.22;
                phase -= Math.Floor(phase);
                float hop = (float)Math.Sin(phase * Math.PI) * 26;
                float squash = phase < 0.08 || phase > 0.92 ? 0.85f : 1f;
                float x = centre.X + (i - 1) * 34, radius = 10;
                float shadow = radius * (1 - hop / 60);
                using (Brush s = new SolidBrush(Color.FromArgb(46, Jungle.Ink))) g.FillEllipse(s, x - shadow, ground, shadow * 2, 4);
                GraphicsState state = g.Save();
                g.TranslateTransform(x, ground - radius * squash - hop);
                g.ScaleTransform(1 / squash, squash);
                using (GraphicsPath shell = Sketch.Circle(PointF.Empty, radius, 30 + i, 0.7f))
                {
                    using (Brush b = new SolidBrush(Jungle.Bark)) g.FillPath(b, shell);
                    Sketch.Stroke(g, shell, Jungle.Ink, 1.5f);
                }
                using (Brush eyes = new SolidBrush(Color.FromArgb(180, Jungle.Ink)))
                    for (int e = 0; e < 3; e++)
                    {
                        double a = e * 2.1 - 1.6;
                        g.FillEllipse(eyes, (float)Math.Cos(a) * 4 - 1.3f, (float)Math.Sin(a) * 4 - 1.3f, 2.6f, 2.6f);
                    }
                g.Restore(state);
            }
        }

        // A vine that grows leaf by leaf as the download comes in.
        private static void Vine(Graphics g, RectangleF r, float t)
        {
            float left = r.Left + 12, right = r.Right - 12, middle = r.Top + r.Height / 2;
            float x = left + (right - left) * Math.Max(0, Math.Min(1, t));
            PointF[] vine = Sketch.Wiggle(new PointF(left, middle), new PointF(right, middle), 77, 2.2f);
            using (Pen dry = new Pen(Color.FromArgb(196, 176, 130), 6f)) { dry.StartCap = dry.EndCap = LineCap.Round; g.DrawCurve(dry, vine); }
            GraphicsState state = g.Save();
            g.SetClip(new RectangleF(r.Left - 4, r.Top - 10, x - r.Left + 4, r.Height + 20));
            using (Pen green = new Pen(Jungle.Vine, 6f)) { green.StartCap = green.EndCap = LineCap.Round; g.DrawCurve(green, vine); }
            for (float lx = left + 18; lx < x - 6; lx += 26)
                Jungle.DrawLeaf(g, new PointF(lx, middle - 1), ((int)lx % 2 == 0) ? -50 : 230, 13, 6, Jungle.Leaf, (int)lx);
            g.Restore(state);
            using (Pen outline = new Pen(Color.FromArgb(150, Jungle.Ink), 1.2f)) g.DrawCurve(outline, vine);
            using (GraphicsPath tip = Sketch.Circle(new PointF(x, middle), 7, 3, 0.6f))
            {
                using (Brush b = new SolidBrush(Jungle.Banana)) g.FillPath(b, tip);
                Sketch.Stroke(g, tip, Jungle.Ink, 1.5f);
            }
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

    // A quick on/off switch along the top of the panel, like a pebble that turns green.
    internal sealed class QuickToggle : Widget
    {
        private readonly string text;
        private readonly Func<bool> get;
        private readonly Action flip;
        private bool down;
        private float shown = -1;

        public QuickToggle(string text, Func<bool> get, Action flip) { this.text = text; this.get = get; this.flip = flip; }

        public override float Measure(float width) { return 28; }
        public override bool Animate(double dt) { return Ease(ref shown, get(), dt); }

        public float Width()
        {
            using (Bitmap b = new Bitmap(1, 1)) using (Graphics g = Graphics.FromImage(b))
                return g.MeasureString(text, Form.ChipFont).Width + 30;
        }

        public override void Paint(Graphics g, bool enabled, bool hover)
        {
            float t = shown < 0 ? (get() ? 1 : 0) : shown;
            RectangleF r = Bounds;
            if (down) r.Offset(0.5f, 1.5f);
            using (GraphicsPath path = Sketch.Box(r, 13, Sketch.Seed(Bounds), 1.2f))
            {
                Color fill = Mix(hover ? Color.FromArgb(255, 246, 222) : Jungle.Paper, Jungle.Leaf, t);
                using (Brush b = new SolidBrush(fill)) g.FillPath(b, path);
                Sketch.Stroke(g, path, Jungle.Ink, 1.6f + 0.6f * t);
            }
            // A little dot: a banana when on, an empty seed when off. It swells as it turns on.
            using (GraphicsPath dot = Sketch.Circle(new PointF(r.Left + 12, r.Top + r.Height / 2), 4.5f + (float)Math.Sin(t * Math.PI) * 1.5f, (int)r.X, 0.5f))
            {
                using (Brush b = new SolidBrush(Mix(Color.FromArgb(222, 208, 172), Jungle.Banana, t))) g.FillPath(b, dot);
                using (Pen ink = new Pen(Jungle.Ink, 1.2f)) g.DrawPath(ink, dot);
            }
            SizeF size = g.MeasureString(text, Form.ChipFont);
            Jungle.Text(g, text, Form.ChipFont, Mix(Jungle.Ink, Jungle.Paper, t), new PointF(r.Left + 21, r.Top + (r.Height - size.Height) / 2));
        }

        public override void Down(PointF p) { down = true; }
        public override void Up(PointF p)
        {
            down = false;
            if (Bounds.Contains(p)) flip();
        }
    }
}
