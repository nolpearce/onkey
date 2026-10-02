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
        private readonly Action<string, string> announce;
        private readonly Action exit;
        private readonly SynchronizationContext ui;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private bool firstCheck;
        private DateTime lastChecked = DateTime.MinValue;
        private string announced;

        // announce shows a note by the clock; clicking it should open the Updates tab.
        public Updater(string appFolder, Action<string, string> announce, Action exit)
        {
            this.appFolder = appFolder;
            this.announce = announce;
            this.exit = exit;
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            // .NET Framework may not offer TLS 1.2 by default, and GitHub needs it.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }
            catch (NotSupportedException ex) { Log.Error("Turning on TLS 1.2", ex); }
            timer.Tick += delegate
            {
                if (firstCheck) { firstCheck = false; timer.Interval = 6 * 60 * 60 * 1000; }
                Check();
            };
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
                catch (Exception ex) { error = ex.Message; Log.Error("Checking for updates", ex); }
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
                Log.Info("Onkey " + Available.Version + " is available");
                announce("Onkey " + Available.Version + " is out", "Click here, or click Onkey's icon and open Updates.");
            }
        }

        private void SetState(Phase state)
        {
            State = state;
            // Whatever is listening (the menu, an open panel) mustn't break the update.
            try { if (Changed != null) Changed(); }
            catch (Exception ex) { Log.Error("Showing the update state", ex); }
        }

        private void Fail(string problem)
        {
            Log.Warn("Update failed: " + problem);
            Problem = problem;
            SetState(Phase.Failed);
        }

        // Where the new files go: over this folder, or into the user's own programs folder when
        // this one can't be written (for example if he was put in Program Files).
        public string InstallFolder()
        {
            if (CanWrite(appFolder)) return appFolder.TrimEnd('\\');
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.Combine("Programs", "Onkey"));
        }

        public bool Moving { get { return !SameFolder(InstallFolder(), appFolder); } }

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
            Log.Info("Updating to " + release.Version + " from " + release.Download + " into " + target);
            Progress = 0;
            SetState(Phase.Downloading);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string unpacked = null, error = null;
                try { unpacked = DownloadAndUnpack(release, ReportProgress); }
                catch (Exception ex) { error = ex.Message; Log.Error("Downloading the update", ex); }
                ui.Post(delegate
                {
                    if (error != null) { Fail("The update didn't work: " + error); return; }
                    Log.Info("Downloaded and unpacked into " + unpacked);
                    Progress = 1;
                    SetState(Phase.Restarting);
                    // A beat to show "Restarting", then hand over.
                    System.Windows.Forms.Timer pause = new System.Windows.Forms.Timer();
                    pause.Interval = 600;
                    pause.Tick += delegate
                    {
                        pause.Dispose();
                        try { Relaunch(unpacked, target); }
                        catch (Exception ex) { Log.Error("Restarting for the update", ex); Fail("The update didn't work: " + ex.Message); }
                    };
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
                SetState(Phase.Downloading);
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
                // A dropped connection can end the stream early without an error.
                if (got == 0 || total > 0 && got != total)
                    throw new Exception("the download stopped part way (got " + got + " of " + total + " bytes). Try again.");
                Log.Info("Downloaded " + got + " bytes");
            }

            if (release.Sha256 == null) Log.Warn("The release lists no checksum for " + AssetName + ", so it isn't checked");
            else
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
            catch (IOException ex) { throw new Exception("Onkey couldn't unpack the download (" + ex.Message + ")."); }
            catch (UnauthorizedAccessException ex) { throw new Exception("Onkey couldn't unpack the download (" + ex.Message + ")."); }
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
            if (!SameFolder(target, appFolder)) MoveLoginEntry(Path.Combine(target, "Onkey.exe"));
            ProcessStartInfo start = new ProcessStartInfo(Path.Combine(files, "Onkey.exe"),
                "--install " + Process.GetCurrentProcess().Id + " " + Arg(files) + " " + Arg(target));
            start.UseShellExecute = false;
            start.WorkingDirectory = files;
            try { Process.Start(start); }
            catch (Exception ex)
            {
                Log.Error("Starting the installer", ex);
                Fail("The update didn't work: " + ex.Message);
                return;
            }
            Log.Info("Handed over to the new Onkey; exiting");
            // The new Onkey can't copy over this one while it's running, so if exiting gets
            // stuck, leave anyway.
            Thread watchdog = new Thread(delegate()
            {
                Thread.Sleep(8000);
                Log.Warn("Onkey was slow to exit for the update, so he left without tidying up");
                Environment.Exit(0);
            });
            watchdog.IsBackground = true;
            watchdog.Start();
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

        public static bool SameFolder(string a, string b)
        {
            return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
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
            catch (Exception ex) { Log.Error("Moving the startup entry", ex); /* He still runs; "Open at startup" can be turned on again. */ }
        }
    }

    // What the new Onkey.exe does when the updater starts it with --install: waits for the
    // old Onkey to exit, copies the new files over its folder, and starts Onkey again. Old
    // source files the new version no longer has are removed. The old Onkey.exe is set aside
    // first (Windows lets a running program be renamed, though not replaced), so the copy works
    // even if the old Onkey is slow to go, and if anything fails, or the new Onkey dies as
    // soon as he starts, the old one is put back and started instead. Every step is logged.
    // The download folder in %TEMP% is cleared by the next Onkey that starts, since this one
    // is running from it.
    internal static class Installer
    {
        public static void Run(string pid, string from, string to)
        {
            Log.Info("Installing from " + from + " into " + to);
            WaitForOldOnkey(pid);
            string exe = Path.Combine(to, "Onkey.exe");
            string backup = SetAside(exe);
            string error = null;
            // Antivirus scanners and Explorer can hold a file for a moment, so try a few times.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try { CopyFolder(from, to); error = null; break; }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Log.Warn("Copying the new files, try " + (attempt + 1) + ": " + ex.Message);
                    Thread.Sleep(500);
                }
            }
            if (error == null)
            {
                Log.Info("Copied the new files");
                try { RemoveStale(Path.Combine(from, "Source"), Path.Combine(to, "Source"), "*.cs"); }
                catch (Exception ex) { Log.Error("Removing old source files", ex); /* Leftovers do no harm. */ }
                if (StartedAndStayed(exe, to)) return;
                // The new Onkey has usually saved what went wrong; keep that for the report.
                const string Stopped = "The new Onkey stopped as soon as he started, so the old one was put back";
                if (Log.PendingCrash == null) Log.Crash(Stopped, null); else Log.Warn(Stopped);
            }
            else Log.Crash("The update couldn't copy the new files: " + error, null);
            if (backup != null && Restore(backup, exe)) Start(exe, to);
            else if (!File.Exists(exe) || !Start(exe, to))
                MessageBox.Show("Onkey couldn't finish updating: " + (error ?? "the new version wouldn't start")
                    + "\n\nDownload him again from github.com/nolpearce/onkey.", "Onkey");
        }

        // Waits up to 30 seconds for the old Onkey to exit, then makes him.
        private static void WaitForOldOnkey(string pid)
        {
            int id;
            if (!int.TryParse(pid, out id)) return;
            try
            {
                using (Process old = Process.GetProcessById(id))
                {
                    if (old.WaitForExit(30000)) { Log.Info("The old Onkey has exited"); return; }
                    Log.Warn("The old Onkey hasn't exited after 30 seconds; stopping him");
                    old.Kill();
                    old.WaitForExit(5000);
                }
            }
            catch (ArgumentException) { /* Already gone. */ }
            catch (Exception ex) { Log.Error("Waiting for the old Onkey to exit", ex); }
        }

        // Renames the old Onkey.exe out of the way, returning where it went (or null).
        private static string SetAside(string exe)
        {
            if (!File.Exists(exe)) return null;
            string backup = Path.Combine(Path.GetDirectoryName(exe), "Onkey.old.exe");
            try
            {
                if (File.Exists(backup)) File.Delete(backup);
            }
            catch
            {
                // An earlier backup is still in use; pick a new name.
                backup = Path.Combine(Path.GetDirectoryName(exe), "Onkey.old-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            }
            try
            {
                File.Move(exe, backup);
                Log.Info("Set the old Onkey.exe aside as " + Path.GetFileName(backup));
                return backup;
            }
            catch (Exception ex)
            {
                Log.Error("Setting the old Onkey.exe aside", ex);
                return null;
            }
        }

        private static bool Restore(string backup, string exe)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (File.Exists(exe)) File.Delete(exe);
                    File.Move(backup, exe);
                    Log.Info("Put the old Onkey.exe back");
                    return true;
                }
                catch (Exception ex)
                {
                    if (attempt == 9) Log.Error("Putting the old Onkey.exe back", ex);
                    Thread.Sleep(500);
                }
            }
            return false;
        }

        // Starts the new Onkey and watches him for a few seconds: if he quits with an error
        // in that time, he's broken. (He exits cleanly, with 0, if another Onkey is running.)
        private static bool StartedAndStayed(string exe, string folder)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(exe, "--updated");
                start.UseShellExecute = false;
                start.WorkingDirectory = folder;
                using (Process onkey = Process.Start(start))
                {
                    if (onkey.WaitForExit(10000) && onkey.ExitCode != 0)
                    {
                        Log.Warn("The new Onkey exited straight away with code " + onkey.ExitCode);
                        return false;
                    }
                }
                Log.Info("The new Onkey is running");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Starting the new Onkey", ex);
                return false;
            }
        }

        private static bool Start(string exe, string folder)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(exe, "--updated");
                start.UseShellExecute = false;
                start.WorkingDirectory = folder;
                Process.Start(start).Dispose();
                Log.Info("Started " + exe);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Starting Onkey again", ex);
                MessageBox.Show("Onkey couldn't start again: " + ex.Message, "Onkey");
                return false;
            }
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
