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
    // Settings kept in %APPDATA%\Onkey\settings.txt as key=value lines.
    internal sealed class Settings
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();
        // Parsed once when set, since the Onkeys read these every tick.
        private readonly Dictionary<string, double> numbers = new Dictionary<string, double>();
        private readonly string path;

        public Settings()
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Onkey", "settings.txt");
            string[] defaults = {
                "zone=anywhere", "chase=5", "speed=42", "soundOn=true", "soundGap=90", "volume=1",
                "size=1", "opacity=1", "layer=above", "overFullScreen=false", "draggable=false", "watchCursor=true", "blink=true", "count=1", "dance=false", "checkUpdates=true" };
            foreach (string line in defaults) Parse(line);
            try { if (File.Exists(path)) foreach (string line in File.ReadAllLines(path)) Parse(line); }
            catch { /* Unreadable settings just mean the defaults. */ }
        }

        private void Parse(string line)
        {
            int eq = line.IndexOf('=');
            if (eq > 0) Store(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
        }

        private void Store(string key, string value)
        {
            values[key] = value;
            double v;
            numbers[key] = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        public string Get(string key) { string v; return values.TryGetValue(key, out v) ? v : ""; }
        public bool Has(string key) { return values.ContainsKey(key); }
        public bool Bool(string key) { return Get(key) == "true"; }
        public double Number(string key) { double v; return numbers.TryGetValue(key, out v) ? v : 0; }

        public void Set(string key, string value) { Store(key, value); Save(); }
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
}
