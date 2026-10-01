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
        private ToolStripMenuItem pauseItem, updateItem;
        private SettingsPanel panel;
        private DateTime panelClosed = DateTime.MinValue;
        // Fired whenever a setting or the update state changes, so an open Settings window can follow.
        public event Action SettingsChanged;
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
            using (Bitmap icon = RenderHead(32)) return Icon.FromHandle(icon.GetHicon());
        }

        // Onkey's head, cropped from the top of his opaque area, on a square of the given size.
        public Bitmap RenderHead(int size)
        {
            using (Bitmap face = Renderer.Render(0, false, Math.Max(1, size / 32f), false))
            {
                Bitmap icon = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                RectangleF b = OnkeyRenderer.OpaqueBounds(face, 1);   // In pixels.
                // Crop to the head so he's recognisable at 16px.
                RectangleF headArea = new RectangleF(b.X + b.Width * 0.22f, b.Y, b.Width * 0.56f, b.Height * 0.72f);
                using (Graphics g = Graphics.FromImage(icon))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float scale = Math.Min(size / headArea.Width, size / headArea.Height);
                    float w = headArea.Width * scale, h = headArea.Height * scale;
                    g.DrawImage(face, new RectangleF((size - w) / 2, (size - h) / 2, w, h), headArea, GraphicsUnit.Pixel);
                }
                return icon;
            }
        }

        // The tray menu keeps only the quick actions; everything else is in the Settings window.
        private void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            pauseItem = Item("Pause Onkey", delegate { TogglePause(); });
            menu.Items.Add(pauseItem);
            menu.Items.Add(Option("Dance to music", "dance"));
            menu.Items.Add(Option("Let me drag Onkey around", "draggable"));
            menu.Items.Add(new ToolStripSeparator());
            // Only shown while there's an update to install.
            updateItem = Item("Update Onkey...", delegate { updater.MenuChosen(); });
            menu.Items.Add(updateItem);
            ToolStripMenuItem settingsItem = Item("Settings...", delegate { ShowSettings(Cursor.Position); });
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
            menu.Items.Add(settingsItem);
            menu.Items.Add(Item("Exit Onkey", delegate { Exit(); }));

            tray.Icon = MakeTrayIcon();
            tray.Text = "Onkey - click for settings";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                // Clicking the icon while the panel is open closes it (it closes itself as the
                // click lands), so don't open it straight back up.
                if (e.Button == MouseButtons.Left && (DateTime.Now - panelClosed).TotalMilliseconds > 300) ShowSettings(Cursor.Position);
            };
            RefreshMenu();
        }

        private static ToolStripMenuItem Item(string text, EventHandler click)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += click;
            return item;
        }

        // An on/off setting.
        private ToolStripMenuItem Option(string text, string key)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = key;
            item.Click += delegate { Change(key, Settings.Bool(key) ? "false" : "true"); };
            optionItems.Add(item);
            return item;
        }

        // Pops the settings panel up beside the tray icon.
        public void ShowSettings(Point near)
        {
            if (panel != null) { panel.Activate(); return; }
            panel = new SettingsPanel(this, updater);
            panel.FormClosed += delegate { panel = null; panelClosed = DateTime.Now; };
            panel.ShowNear(near);
        }

        // Stores a setting and puts it into effect straight away.
        public void Change(string key, string value)
        {
            if (Settings.Get(key) == value) return;
            int oldWidth = CanvasPixelsWide, oldHeight = CanvasPixelsHigh;
            Settings.Set(key, value);
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
                case "opacity": case "layer": case "draggable": case "overFullScreen":
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

        public void Change(string key, double value) { Change(key, value.ToString("R", CultureInfo.InvariantCulture)); }

        private void RefreshMenu()
        {
            foreach (ToolStripMenuItem item in optionItems) item.Checked = Settings.Bool((string)item.Tag);
            if (updater != null)
            {
                Updater.Release release = updater.Available;
                updateItem.Visible = release != null;
                if (release != null) updateItem.Text = updater.Downloading ? "Downloading Onkey " + release.Version + "..." : "Update to Onkey " + release.Version + "...";
                updateItem.Enabled = !updater.Downloading;
                tray.Text = release == null ? "Onkey - click for settings" : "Onkey - version " + release.Version + " is available";
            }
            else updateItem.Visible = false;
            if (SettingsChanged != null) SettingsChanged();
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
            bool restack = ++tickCount % 15 == 0 && !tray.ContextMenuStrip.Visible && panel == null;
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

        public void TogglePause()
        {
            Paused = !Paused;
            pauseItem.Text = Paused ? "Resume Onkey" : "Pause Onkey";
            if (Paused) { StopSound(); foreach (PetForm pet in pets) pet.ShowIdle(); }
            if (SettingsChanged != null) SettingsChanged();
        }

        public bool HasSound { get { return Sound != null; } }

        public void PlayNow()
        {
            if (pets.Count > 0) Speak(pets[Random.Next(pets.Count)], true);
        }

        // Gathers every Onkey onto the screen under the mouse, loosely around the middle.
        public void BringHere()
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

        public bool OpensAtLogin
        {
            get
            {
                try
                {
                    using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey))
                        return run != null && run.GetValue("Onkey") != null;
                }
                catch { return false; }
            }
        }

        public void ToggleLogin()
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

        public void Exit()
        {
            if (exiting) return;
            exiting = true;
            SavePosition();
            if (panel != null) panel.Close();
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
