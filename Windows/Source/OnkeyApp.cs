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
    // Owns everything the Onkeys share: the tray menu, settings, drawing, sound and clock.
    internal sealed class OnkeyApp : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public readonly Random Random = new Random();
        public readonly Stopwatch Wall = Stopwatch.StartNew();
        public readonly Settings Settings = new Settings();
        public readonly OnkeyRenderer Renderer;
        public readonly OnkeySound Sound;
        public Bitmap[] Frames = new Bitmap[0];
        public float[] FrameBounce = new float[0];
        // The lowest opaque row of each frame (points, unscaled): where his hands end.
        public float[] FrameBottom = new float[0];
        public float IdleBottom = OnkeyRenderer.CanvasHeight;
        public Bitmap Idle;
        public RectangleF PetBounds = new RectangleF(0, 0, OnkeyRenderer.CanvasWidth, OnkeyRenderer.CanvasHeight);
        // Seconds Onkey has been awake; stops while paused.
        public double Clock;
        public bool Paused;

        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly string assetFolder, appFolder;
        private readonly List<PetForm> pets = new List<PetForm>();
        private readonly List<ToolStripMenuItem> optionItems = new List<ToolStripMenuItem>();
        private readonly List<ToolStripMenuItem> soundOptionItems = new List<ToolStripMenuItem>();
        private ToolStripMenuItem pauseItem, loginItem, updateItem;
        // Finds and installs new releases from GitHub.
        private readonly Updater updater;
        private float dpi = 1;
        private double lastTick;
        // Hears music and finds its beat while "Dance to music" is on.
        private readonly MusicListener listener = new MusicListener();
        private double danceLevel;   // Eases between 0 (still) and 1 (bopping) as music starts and stops.
        private int tickCount;
        private bool exiting;

        public float PixelScale { get { return (float)Settings.Number("size") * dpi; } }   // Pixels per canvas point.
        public bool Watching { get { return Settings.Bool("watchCursor"); } }
        public int CanvasPixelsWide { get { return (int)Math.Round(OnkeyRenderer.CanvasWidth * PixelScale); } }
        public int CanvasPixelsHigh { get { return (int)Math.Round(OnkeyRenderer.CanvasHeight * PixelScale); } }

        public OnkeyApp()
        {
            // Set by Start Onkey.ps1: where the launcher lives, and where Onkey.png and Sounds are.
            appFolder = Environment.GetEnvironmentVariable("ONKEY_APP_DIR") ?? AppDomain.CurrentDomain.BaseDirectory;
            assetFolder = Environment.GetEnvironmentVariable("ONKEY_ASSET_DIR") ?? appFolder;
            Renderer = new OnkeyRenderer(Path.Combine(assetFolder, "Onkey.png"));
            string soundPath = Path.Combine(assetFolder, Path.Combine("Sounds", "oooo.wav"));
            if (File.Exists(soundPath)) { try { Sound = new OnkeySound(soundPath); } catch { Sound = null; } }
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96f;
            RenderFrames();

            // The first Onkey comes back where he was last time.
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Point origin = new Point(area.Right - CanvasPixelsWide - 60, area.Bottom - CanvasPixelsHigh - 40);
            if (Settings.Has("savedX"))
            {
                Point saved = new Point((int)Settings.Number("savedX"), (int)Settings.Number("savedY"));
                foreach (Screen screen in Screen.AllScreens)
                {
                    Rectangle near = screen.WorkingArea;
                    near.Inflate(200, 200);
                    if (near.Contains(saved)) { area = screen.WorkingArea; origin = saved; break; }
                }
            }
            AddPet(origin, area);
            MatchPetCount();
            BuildMenu();
            tray.Visible = true;
            if (Settings.Bool("dance")) StartListening();
            updater = new Updater(appFolder, tray, Exit);
            updater.Changed += RefreshMenu;
            if (Settings.Bool("checkUpdates")) updater.StartAutomaticChecks();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            lastTick = Wall.Elapsed.TotalSeconds;
            timer.Interval = 33;
            timer.Tick += delegate { Tick(); };
            timer.Start();
        }

        // Onkeys

        private void AddPet(Point origin, Rectangle area)
        {
            PetForm pet = new PetForm(this, origin, area);
            pets.Add(pet);
            pet.Show();
        }

        // Adds or removes Onkeys to match the "How many Onkeys" setting. New ones turn up
        // somewhere random on the first Onkey's screen.
        private void MatchPetCount()
        {
            int wanted = Math.Max(1, (int)Settings.Number("count"));
            while (pets.Count > wanted)
            {
                PetForm pet = pets[pets.Count - 1];
                pets.RemoveAt(pets.Count - 1);
                pet.Close();
            }
            while (pets.Count < wanted)
            {
                Rectangle area = pets.Count > 0 ? pets[0].Area : Screen.FromPoint(Cursor.Position).WorkingArea;
                Point origin = new Point(area.Left + Random.Next(Math.Max(1, area.Width - CanvasPixelsWide)),
                                         area.Top + Random.Next(Math.Max(1, area.Height - CanvasPixelsHigh)));
                AddPet(origin, area);
            }
        }

        public void RenderFrames()
        {
            float s = PixelScale;
            bool blank = Watching;
            Bitmap[] rendered = new Bitmap[OnkeyRenderer.FrameCount];
            float[] bounce = new float[OnkeyRenderer.FrameCount];
            float[] bottom = new float[OnkeyRenderer.FrameCount];
            RectangleF bounds = RectangleF.Empty;
            for (int i = 0; i < rendered.Length; i++)
            {
                double p = i * 2 * Math.PI / rendered.Length;
                rendered[i] = Renderer.Render(p, true, s, blank);
                bounce[i] = OnkeyRenderer.Bounce(p, true);
                RectangleF b = OnkeyRenderer.OpaqueBounds(rendered[i], s);
                bottom[i] = b.Bottom;
                bounds = bounds.IsEmpty ? b : RectangleF.Union(bounds, b);
            }
            Bitmap still = Renderer.Render(0, false, s, blank);
            RectangleF stillBounds = OnkeyRenderer.OpaqueBounds(still, s);
            bounds = RectangleF.Union(bounds, stillBounds);
            Bitmap[] oldFrames = Frames;
            Bitmap oldIdle = Idle;
            Frames = rendered; FrameBounce = bounce; FrameBottom = bottom; Idle = still; IdleBottom = stillBounds.Bottom;
            PetBounds = bounds;
            foreach (PetForm pet in pets) pet.FramesChanged();
            foreach (Bitmap f in oldFrames) f.Dispose();
            if (oldIdle != null) oldIdle.Dispose();
        }

        private Icon MakeTrayIcon()
        {
            using (Bitmap face = Renderer.Render(0, false, 1, false))
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
            menu.Items.Add(Option("Dance to music", "dance", "true"));
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(Submenu("How many Onkeys",
                Option("One", "count", "1"),
                Option("Two", "count", "2"),
                Option("Three", "count", "3"),
                Option("Five", "count", "5"),
                Option("Ten (chaos)", "count", "10")));

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

            ToolStripMenuItem playNow = Item("Play sound now", delegate { PlayNow(); });
            ToolStripItem[] soundOptions = {
                Header("How often"),
                Option("Every 20-40 seconds", "soundGap", "20"),
                Option("Every 1½-3 minutes", "soundGap", "90"),
                Option("Every 5-10 minutes", "soundGap", "300"),
                new ToolStripSeparator(),
                Header("Volume"),
                Option("Quiet", "volume", "0.25"),
                Option("Medium", "volume", "0.6"),
                Option("Loud", "volume", "1") };
            foreach (ToolStripItem i in soundOptions) { ToolStripMenuItem m = i as ToolStripMenuItem; if (m != null && m.Tag != null) soundOptionItems.Add(m); }
            soundOptionItems.Add(playNow);
            List<ToolStripItem> soundMenu = new List<ToolStripItem>();
            soundMenu.Add(Option(Sound == null ? "Sound clip not installed" : "Sound on", "soundOn", "true"));
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
                Option("Stay in front of full-screen apps", "overFullScreen", "true"),
                new ToolStripSeparator(),
                Header("Eyes"),
                Option("Watch my cursor", "watchCursor", "true"),
                Option("Blink now and then", "blink", "true")));

            menu.Items.Add(Option("Let me drag Onkey around", "draggable", "true"));
            menu.Items.Add(new ToolStripSeparator());
            updateItem = Item("Check for updates...", delegate { updater.MenuChosen(); });
            menu.Items.Add(updateItem);
            menu.Items.Add(Option("Check for updates automatically", "checkUpdates", "true"));
            loginItem = Item("Open Onkey when Windows starts", delegate { ToggleLogin(); });
            menu.Items.Add(loginItem);
            menu.Items.Add(Item("Exit Onkey", delegate { Exit(); }));

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
            int oldWidth = CanvasPixelsWide, oldHeight = CanvasPixelsHigh;
            if (value == "true") Settings.Set(key, Settings.Bool(key) ? "false" : "true");
            else Settings.Set(key, value);
            switch (key)
            {
                case "count":
                    MatchPetCount();
                    break;
                case "dance":
                    if (Settings.Bool("dance")) StartListening(); else listener.Dispose();
                    break;
                case "size":
                    RenderFrames();
                    foreach (PetForm pet in pets) pet.Resized(oldWidth, oldHeight);
                    break;
                case "watchCursor":
                    RenderFrames();
                    break;
                case "opacity": case "layer": case "draggable":
                    foreach (PetForm pet in pets) pet.ApplyAppearance();
                    break;
                case "zone": case "chase":
                    foreach (PetForm pet in pets) { pet.RestFor(0); pet.PickTarget(); }
                    break;
                case "soundGap":
                    foreach (PetForm pet in pets) pet.RescheduleSound();
                    break;
                case "soundOn":
                    if (!Settings.Bool("soundOn")) StopSound();
                    break;
                case "checkUpdates":
                    if (Settings.Bool("checkUpdates")) updater.StartAutomaticChecks(); else updater.StopAutomaticChecks();
                    break;
            }
            RefreshMenu();
            foreach (PetForm pet in pets) pet.Present();
        }

        private void RefreshMenu()
        {
            foreach (ToolStripMenuItem item in optionItems)
            {
                string[] tag = (string[])item.Tag;
                item.Checked = tag[1] == "true" ? Settings.Bool(tag[0]) : Settings.Get(tag[0]) == tag[1];
            }
            bool soundOn = Sound != null && Settings.Bool("soundOn");
            foreach (ToolStripMenuItem item in soundOptionItems) item.Enabled = soundOn;
            if (updater != null)
            {
                Updater.Release release = updater.Available;
                updateItem.Text = release == null ? "Check for updates..."
                    : updater.Downloading ? "Downloading Onkey " + release.Version + "..."
                    : "Update to Onkey " + release.Version + "...";
                updateItem.Enabled = !updater.Downloading;
                tray.Text = release == null ? "Onkey - right-click for settings" : "Onkey - version " + release.Version + " is available";
            }
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey))
                    loginItem.Checked = run != null && run.GetValue("Onkey") != null;
            }
            catch { loginItem.Enabled = false; }
        }

        // Ticking

        private void Tick()
        {
            double now = Wall.Elapsed.TotalSeconds;
            double dt = Math.Min(0.08, Math.Max(0, now - lastTick));
            lastTick = now;
            if (!Paused) Clock += dt;
            float squash = BeatSquash(now, dt);
            // Windows doesn't keep him where he was put: a newly opened window (a browser's new
            // tab or window, say) can land above him even while he's topmost. Putting him back
            // in his layer now and then is enough to undo that (but not over the open tray menu).
            bool restack = ++tickCount % 15 == 0 && !tray.ContextMenuStrip.Visible;
            bool desktop = Settings.Get("layer") == "desktop";
            foreach (PetForm pet in pets.ToArray())
            {
                if (!Paused && !pet.Dragging) pet.Walk(dt);
                pet.UpdateFace(now, dt);
                pet.Squash = squash;
                pet.Present();
                if (restack) { if (desktop) pet.SendToBottom(); else pet.BringToTop(); }
            }
        }

        // How squashed every Onkey is right now. He squashes down just before each beat so the
        // deepest point (15%) lands exactly on it, then springs back more slowly; faded in and
        // out as music starts and stops.
        private float BeatSquash(double now, double dt)
        {
            if (Settings.Bool("dance")) listener.Poll(now);
            BeatTracker.Beat beat = listener.Current;
            double target = Settings.Bool("dance") && beat.Active && !Paused ? 1 : 0;
            danceLevel += (target - danceLevel) * Math.Min(1, dt * 3);
            if (danceLevel <= 0.001 || beat.Period <= 0) return 0;
            double phase = ((now - beat.LastBeat) / beat.Period) % 1;
            if (phase < 0) phase += 1;
            return (float)(0.15 * danceLevel * BopShape(phase));
        }

        // 0...1 over one beat, peaking at 1 on the beat (phase 0): eases down over the last 20%
        // of the beat before, and back up over the first 45% after.
        private static double BopShape(double phase)
        {
            double fromBeat = phase < 0.5 ? phase : phase - 1;   // Negative before the beat.
            double width = fromBeat < 0 ? 0.2 : 0.45;
            if (Math.Abs(fromBeat) >= width) return 0;
            return 0.5 + 0.5 * Math.Cos(Math.PI * fromBeat / width);
        }

        private void StartListening()
        {
            try { listener.Start(Wall.Elapsed.TotalSeconds); }
            catch (Exception ex)
            {
                listener.Dispose();
                Settings.Set("dance", "false");
                RefreshMenu();
                MessageBox.Show("Onkey can't hear your music: " + ex.Message, "Onkey");
            }
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            if (pets.Count == 0 || !pets[0].IsHandleCreated) return;
            pets[0].BeginInvoke((MethodInvoker)delegate { foreach (PetForm pet in pets) pet.ScreensChanged(); });
        }

        public void SavePosition()
        {
            if (pets.Count == 0) return;
            Settings.Set("savedX", pets[0].X);
            Settings.Set("savedY", pets[0].Y);
        }

        public bool IsLead(PetForm pet) { return pets.Count > 0 && pets[0] == pet; }

        // Sound. Windows plays one sound at a time here, so a new "oooo" cuts off the last
        // one, and only the Onkey speaking now moves his mouth.

        public double SoundDelay()
        {
            double gap = Settings.Number("soundGap");
            return gap + Random.NextDouble() * gap;
        }

        public void Speak(PetForm speaker, bool force)
        {
            if (Sound == null || (!force && !Settings.Bool("soundOn"))) return;
            try
            {
                Sound.Play(Settings.Number("volume"));
                double now = Wall.Elapsed.TotalSeconds;
                foreach (PetForm pet in pets) pet.SoundStart = pet == speaker ? now : double.NegativeInfinity;
            }
            catch { /* An invalid or unavailable clip must not interrupt the pet. */ }
        }

        private void StopSound()
        {
            if (Sound != null) Sound.Stop();
            foreach (PetForm pet in pets) pet.SoundStart = double.NegativeInfinity;
        }

        // Actions

        private void TogglePause()
        {
            Paused = !Paused;
            pauseItem.Text = Paused ? "Resume Onkey" : "Pause Onkey";
            if (Paused) { StopSound(); foreach (PetForm pet in pets) pet.ShowIdle(); }
        }

        private void PlayNow()
        {
            if (pets.Count > 0) Speak(pets[Random.Next(pets.Count)], true);
        }

        // Gathers every Onkey onto the screen under the mouse, loosely around the middle.
        private void BringHere()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            for (int i = 0; i < pets.Count; i++)
            {
                double spread = i == 0 ? 0 : Math.Min(area.Width, area.Height) * 0.25;
                pets[i].MoveTo(area,
                    area.Left + (area.Width - CanvasPixelsWide) / 2.0 + (Random.NextDouble() * 2 - 1) * spread,
                    area.Top + (area.Height - CanvasPixelsHigh) / 2.0 + (Random.NextDouble() * 2 - 1) * spread);
            }
            SavePosition();
        }

        private void ToggleLogin()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (run.GetValue("Onkey") != null) run.DeleteValue("Onkey");
                    else run.SetValue("Onkey", "wscript.exe \"" + Path.Combine(appFolder, "Start Onkey.vbs") + "\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't change the startup setting: " + ex.Message, "Onkey");
            }
            RefreshMenu();
        }

        private void Exit()
        {
            if (exiting) return;
            exiting = true;
            SavePosition();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            timer.Stop(); timer.Dispose();
            foreach (PetForm pet in pets.ToArray()) pet.Close();
            pets.Clear();
            tray.Visible = false;
            if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
            if (tray.Icon != null) Native.DestroyIcon(tray.Icon.Handle);
            tray.Dispose();
            if (Sound != null) Sound.Dispose();
            listener.Dispose();
            foreach (Bitmap f in Frames) f.Dispose();
            if (Idle != null) Idle.Dispose();
            Renderer.Dispose();
            ExitThread();
        }
    }
}
