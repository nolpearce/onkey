using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Media;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnkeyDesktopPet
{
    // Checks GitHub Releases for a newer Onkey and installs it in place: downloads
    // Onkey-Windows.zip, checks it against the SHA-256 GitHub lists for it, unzips it, then
    // exits and lets the new Onkey.exe (run with --install, see Installer below) copy itself
    // over this folder and start again. It never pops up a window: the Updates tab of the
    // settings panel shows what it's doing (looking, what's new, download progress, or what
    // went wrong), so the whole update happens there. Mac/Sources/Updater.swift does the same
    // for the Mac version.
    internal sealed class Updater
    {
        private const string LatestUrl = "https://api.github.com/repos/nolpearce/onkey/releases/latest";
        private const string AssetName = "Onkey-Windows.zip";

        public sealed class Release
        {
            public string Version, Download, Sha256, Notes;
        }

        public enum Phase { Idle, Checking, UpToDate, Available, Downloading, Restarting, Failed }

        // A newer release, once a check has found one.
        public Release Available;
        public Phase State = Phase.Idle;
        public double Progress;       // 0 to 1 while downloading.
        public string Problem = "";   // What went wrong, when State is Failed.
        public bool Downloading { get { return State == Phase.Downloading || State == Phase.Restarting; } }
        // Called on the UI thread whenever any of the above changes.
        public event Action Changed;

        private readonly string appFolder;
        private readonly NotifyIcon tray;
        private readonly Action exit;
        private readonly SynchronizationContext ui;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private bool firstCheck;
        private DateTime lastChecked = DateTime.MinValue;
        private string announced;

        public Updater(string appFolder, NotifyIcon tray, Action exit, Action showUpdates)
        {
            this.appFolder = appFolder;
            this.tray = tray;
            this.exit = exit;
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            // .NET Framework may not offer TLS 1.2 by default, and GitHub needs it.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            timer.Tick += delegate
            {
                if (firstCheck) { firstCheck = false; timer.Interval = 6 * 60 * 60 * 1000; }
                Check();
            };
            tray.BalloonTipClicked += delegate { if (Available != null) showUpdates(); };
        }

        // Checks shortly after launch and then every six hours, quietly.
        public void StartAutomaticChecks()
        {
            if (timer.Enabled) return;
            firstCheck = true;
            timer.Interval = 15000;
            timer.Start();
        }

        public void StopAutomaticChecks() { timer.Stop(); }

        // Looks again when the Updates tab opens, unless it looked in the last minute.
        public void CheckIfStale()
        {
            if (State == Phase.Idle || (State != Phase.Checking && !Downloading && (DateTime.Now - lastChecked).TotalSeconds > 60)) Check();
        }

        // Asks GitHub for the latest release. The answer shows in the Updates tab; the search
        // shows for at least a moment, so the loading animation never just flickers. The first
        // time a quiet check finds a new version, a note by the clock says so.
        public void Check()
        {
            if (State == Phase.Checking || Downloading) return;
            SetState(Phase.Checking);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Stopwatch took = Stopwatch.StartNew();
                Release release = null;
                string error = null;
                try
                {
                    using (WebClient web = NewClient())
                    {
                        web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                        release = Parse(web.DownloadString(LatestUrl));
                    }
                    if (release == null) error = "GitHub answered with something Onkey didn't understand.";
                }
                catch (Exception ex) { error = ex.Message; }
                int wait = 900 - (int)took.ElapsedMilliseconds;
                if (wait > 0) Thread.Sleep(wait);
                ui.Post(delegate { Checked(release, error); }, null);
            });
        }

        private void Checked(Release release, string error)
        {
            lastChecked = DateTime.Now;
            if (error != null)
            {
                // Keep showing a release already found; otherwise say what went wrong.
                if (Available != null) SetState(Phase.Available);
                else Fail("Couldn't reach GitHub. " + error);
                return;
            }
            Available = IsNewer(release.Version, Program.Version) ? release : null;
            SetState(Available != null ? Phase.Available : Phase.UpToDate);
            if (Available != null && announced != Available.Version)
            {
                announced = Available.Version;
                tray.ShowBalloonTip(8000, "Onkey " + Available.Version + " is out",
                    "Click here, or click Onkey's icon and open Updates.", ToolTipIcon.Info);
            }
        }

        private void SetState(Phase state)
        {
            State = state;
            if (Changed != null) Changed();
        }

        private void Fail(string problem)
        {
            Problem = problem;
            SetState(Phase.Failed);
        }

        // Where the new files go: over this folder, or into the user's own programs folder when
        // this one can't be written (for example if he was put in Program Files).
        public string InstallFolder()
        {
            if (CanWrite(appFolder)) return appFolder;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.Combine("Programs", "Onkey"));
        }

        public bool Moving { get { return InstallFolder() != appFolder; } }

        // Installing

        // Downloads and installs the release the last check found, then restarts.
        public void Install()
        {
            Release release = Available;
            if (release == null || Downloading) return;
            // In a copy of the repo the assets live in ..\Assets rather than beside
            // Onkey.exe; that copy is updated with git, not with release downloads.
            if (!OnkeyApp.IsReleaseFolder(appFolder))
            {
                Fail("This Onkey runs from a copy of the code, so update him with \"git pull\" instead.");
                return;
            }
            string target = InstallFolder();
            try { Directory.CreateDirectory(target); } catch { }
            if (!CanWrite(target))
            {
                Fail("Onkey can't write to " + target + ", so he can't update himself.");
                return;
            }
            Progress = 0;
            SetState(Phase.Downloading);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string unpacked = null, error = null;
                try { unpacked = DownloadAndUnpack(release, ReportProgress); }
                catch (Exception ex) { error = ex.Message; }
                ui.Post(delegate
                {
                    if (error != null) { Fail("The update didn't work: " + error); return; }
                    Progress = 1;
                    SetState(Phase.Restarting);
                    // A beat to show "Restarting", then hand over.
                    System.Windows.Forms.Timer pause = new System.Windows.Forms.Timer();
                    pause.Interval = 600;
                    pause.Tick += delegate { pause.Dispose(); Relaunch(unpacked, target); };
                    pause.Start();
                }, null);
            });
        }

        // Called from the download thread; passes the progress to the UI a step at a time.
        private void ReportProgress(double done)
        {
            ui.Post(delegate
            {
                if (State != Phase.Downloading || done - Progress < 0.01 && done < 1) return;
                Progress = done;
                if (Changed != null) Changed();
            }, null);
        }

        // Downloads, checks and unzips the release into a scratch folder in %TEMP%, returning
        // the folder holding the new files. Runs off the UI thread.
        private static string DownloadAndUnpack(Release release, Action<double> progress)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "Onkey-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            string zip = Path.Combine(scratch, AssetName);
            using (WebClient web = NewClient())
            using (Stream from = web.OpenRead(release.Download))
            using (FileStream to = File.Create(zip))
            {
                long total;
                if (!long.TryParse(web.ResponseHeaders[HttpResponseHeader.ContentLength], out total)) total = 0;
                byte[] buffer = new byte[64 * 1024];
                long got = 0;
                int n;
                while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
                {
                    to.Write(buffer, 0, n);
                    got += n;
                    if (total > 0) progress(Math.Min(1, (double)got / total));
                }
            }

            if (release.Sha256 != null)
            {
                string digest;
                using (SHA256 sha = SHA256.Create())
                using (FileStream file = File.OpenRead(zip))
                    digest = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                if (digest != release.Sha256.ToLowerInvariant())
                    throw new Exception("the download was damaged (its checksum didn't match). Try again later.");
            }

            string expanded = Path.Combine(scratch, "files");
            try { ZipFile.ExtractToDirectory(zip, expanded); }
            catch (InvalidDataException) { throw new Exception("the download wasn't a zip Onkey could open. Try again later."); }
            string files = Path.Combine(expanded, "Onkey-Windows");
            if (!File.Exists(Path.Combine(files, "Onkey.exe")) || !OnkeyApp.IsReleaseFolder(files))
                throw new Exception("the download didn't contain Onkey's files.");
            return files;
        }

        // Starts the new Onkey.exe in install mode and exits; it waits for this Onkey to
        // close, copies the new files into the target folder and starts Onkey again. When he
        // moves, "Open at startup" follows him.
        private void Relaunch(string files, string target)
        {
            if (target != appFolder) MoveLoginEntry(Path.Combine(target, "Onkey.exe"));
            ProcessStartInfo start = new ProcessStartInfo(Path.Combine(files, "Onkey.exe"),
                "--install " + Process.GetCurrentProcess().Id + " " + Arg(files) + " " + Arg(target));
            start.UseShellExecute = false;
            start.WorkingDirectory = files;
            try { Process.Start(start); }
            catch (Exception ex)
            {
                Fail("The update didn't work: " + ex.Message);
                return;
            }
            exit();
        }

        // Helpers

        private static WebClient NewClient()
        {
            WebClient web = new WebClient();
            web.Headers[HttpRequestHeader.UserAgent] = "Onkey/" + Program.Version;
            return web;
        }

        // Reads the few fields Onkey needs from GitHub's release JSON. Text inside strings has
        // its quotes escaped, so these patterns only match real keys.
        private static Release Parse(string json)
        {
            Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            Match download = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*/" + Regex.Escape(AssetName) + ")\"");
            if (!tag.Success || !download.Success) return null;
            Release release = new Release();
            release.Version = tag.Groups[1].Value.TrimStart('v', 'V');
            release.Download = download.Groups[1].Value;
            Match body = Regex.Match(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            release.Notes = "";
            try { if (body.Success) release.Notes = PlainNotes(Regex.Unescape(body.Groups[1].Value)); } catch (ArgumentException) { }
            // Each asset lists its name before its digest, so a digest belongs to the
            // nearest name before it.
            string lastName = null;
            foreach (Match m in Regex.Matches(json, "\"name\"\\s*:\\s*\"([^\"]*)\"|\"digest\"\\s*:\\s*\"sha256:([0-9a-fA-F]{64})\""))
            {
                if (m.Groups[1].Success) lastName = m.Groups[1].Value;
                else if (lastName == AssetName) { release.Sha256 = m.Groups[2].Value; break; }
            }
            return release;
        }

        // Compares dotted version numbers, so 4.10 is newer than 4.9 and 4.3 equals 4.3.0.
        public static bool IsNewer(string a, string b)
        {
            string[] x = a.Split('.'), y = b.Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? LeadingNumber(x[i]) : 0, q = i < y.Length ? LeadingNumber(y[i]) : 0;
                if (p != q) return p > q;
            }
            return false;
        }

        private static int LeadingNumber(string s)
        {
            int n = 0, v;
            while (n < s.Length && char.IsDigit(s[n])) n++;
            return int.TryParse(s.Substring(0, n), out v) ? v : 0;
        }

        private static bool CanWrite(string folder)
        {
            try
            {
                string probe = Path.Combine(folder, ".onkey-update-check");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // A command-line argument in double quotes. Folder paths can't contain quotes; a
        // trailing backslash would escape the closing quote, so it's dropped.
        private static string Arg(string s) { return "\"" + s.TrimEnd('\\') + "\""; }

        // The release notes without their Markdown, for the Updates tab.
        private static string PlainNotes(string markdown)
        {
            List<string> lines = new List<string>();
            foreach (string raw in markdown.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim().Replace("**", "").TrimStart('#').Trim();
                if (line.StartsWith("- ") || line.StartsWith("* ")) line = "\u2022 " + line.Substring(2);
                if (line.Length == 0 && (lines.Count == 0 || lines[lines.Count - 1].Length == 0)) continue;
                lines.Add(line);
            }
            string text = string.Join("\n", lines.ToArray()).Trim();
            if (text.Length > 4000) text = text.Substring(0, 4000).Trim() + "\u2026";
            return text;
        }

        private void MoveLoginEntry(string exe)
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    string value = run == null ? null : run.GetValue("Onkey") as string;
                    if (value != null && value.IndexOf(appFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) >= 0)
                        run.SetValue("Onkey", "\"" + exe + "\"");
                }
            }
            catch { /* He still runs; "Open at startup" can be turned on again. */ }
        }
    }

    // What the new Onkey.exe does when the updater starts it with --install: waits for the
    // old Onkey to exit, copies the new files over its folder, and starts Onkey again. Old
    // source files the new version no longer has are removed. If copying fails, whatever
    // Onkey is in the folder starts again and says so. The download folder in %TEMP% is
    // cleared by the next Onkey that starts, since this one is running from it.
    internal static class Installer
    {
        public static void Run(string pid, string from, string to)
        {
            int id;
            if (int.TryParse(pid, out id))
            {
                try { using (Process old = Process.GetProcessById(id)) old.WaitForExit(30000); }
                catch (ArgumentException) { /* Already gone. */ }
            }
            string error = null;
            // Antivirus scanners and Explorer can hold a file for a moment, so try a few times.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try { CopyFolder(from, to); error = null; break; }
                catch (Exception ex) { error = ex.Message; Thread.Sleep(500); }
            }
            if (error == null)
            {
                try { RemoveStale(Path.Combine(from, "Source"), Path.Combine(to, "Source"), "*.cs"); }
                catch { /* Leftover source files do no harm. */ }
            }
            else MessageBox.Show("Onkey couldn't finish updating: " + error, "Onkey");
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(Path.Combine(to, "Onkey.exe"));
                start.UseShellExecute = false;
                start.WorkingDirectory = to;
                Process.Start(start);
            }
            catch (Exception ex) { MessageBox.Show("Onkey couldn't start again: " + ex.Message, "Onkey"); }
        }

        private static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (string folder in Directory.GetDirectories(from))
                CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
        }

        private static void RemoveStale(string from, string to, string pattern)
        {
            if (!Directory.Exists(to)) return;
            foreach (string file in Directory.GetFiles(to, pattern))
                if (!File.Exists(Path.Combine(from, Path.GetFileName(file)))) File.Delete(file);
        }
    }
}
