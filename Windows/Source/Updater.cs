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
    // over this folder and start again. Mac/Sources/Updater.swift does the same for the Mac
    // version.
    internal sealed class Updater
    {
        private const string LatestUrl = "https://api.github.com/repos/nolpearce/onkey/releases/latest";
        private const string AssetName = "Onkey-Windows.zip";

        public sealed class Release
        {
            public string Version, Download, Sha256, Page;
        }

        // A newer release, once a check has found one.
        public Release Available;
        public bool Downloading;
        // Called on the UI thread whenever Available or Downloading changes.
        public event Action Changed;

        private readonly string appFolder;
        private readonly NotifyIcon tray;
        private readonly Action exit;
        private readonly SynchronizationContext ui;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private bool checking, firstCheck;
        private string announced;

        public Updater(string appFolder, NotifyIcon tray, Action exit)
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
                Check(false);
            };
            tray.BalloonTipClicked += delegate { if (Available != null) OfferInstall(); };
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

        // The menu item: installs a release already found, or looks for one.
        public void MenuChosen()
        {
            if (Available != null) OfferInstall(); else Check(true);
        }

        // Asks GitHub for the latest release. Quiet checks only update the menu (and show a
        // balloon once per new version); when the user asked, they also hear "you're up to
        // date" or what went wrong.
        public void Check(bool userAsked)
        {
            if (checking || Downloading) return;
            checking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
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
                ui.Post(delegate { Checked(release, error, userAsked); }, null);
            });
        }

        private void Checked(Release release, string error, bool userAsked)
        {
            checking = false;
            if (error != null)
            {
                if (userAsked) MessageBox.Show("Couldn't check for updates: " + error, "Onkey");
                return;
            }
            Available = IsNewer(release.Version, Program.Version) ? release : null;
            if (Changed != null) Changed();
            if (userAsked)
            {
                if (Available != null) OfferInstall();
                else MessageBox.Show("You have the latest Onkey (version " + Program.Version + ").", "Onkey");
            }
            else if (Available != null && announced != Available.Version)
            {
                announced = Available.Version;
                tray.ShowBalloonTip(8000, "Onkey " + Available.Version + " is out",
                    "Click here, or right-click Onkey's icon and choose \"Update to Onkey " + Available.Version + "\".", ToolTipIcon.Info);
            }
        }

        private void OfferInstall()
        {
            Release release = Available;
            if (release == null || Downloading) return;
            DialogResult answer = MessageBox.Show(
                "Onkey " + release.Version + " is out (you have " + Program.Version + ").\n\n" +
                "Update now? Onkey will download it, restart, and keep all your settings.\n\n" +
                "Choose No to see what's new first.",
                "Update Onkey", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) Install(release);
            else if (answer == DialogResult.No) Open(release.Page);
        }

        // Installing

        private void Install(Release release)
        {
            // In a copy of the repo the assets live in ..\Assets rather than beside
            // Onkey.exe; that copy is updated with git, not with release downloads.
            if (!OnkeyApp.IsReleaseFolder(appFolder))
            {
                MessageBox.Show("This Onkey runs from a copy of the code, so update it with \"git pull\" instead.", "Onkey");
                return;
            }
            if (!CanWrite(appFolder))
            {
                if (MessageBox.Show("Onkey can't write to " + appFolder + ", so he can't update himself there.\n\nOpen the download page instead?",
                                    "Onkey", MessageBoxButtons.YesNo) == DialogResult.Yes) Open(release.Page);
                return;
            }
            Downloading = true;
            if (Changed != null) Changed();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string unpacked = null, error = null;
                try { unpacked = DownloadAndUnpack(release); }
                catch (Exception ex) { error = ex.Message; }
                ui.Post(delegate
                {
                    Downloading = false;
                    if (Changed != null) Changed();
                    if (error != null) MessageBox.Show("Onkey couldn't update: " + error, "Onkey");
                    else Relaunch(unpacked);
                }, null);
            });
        }

        // Downloads, checks and unzips the release into a scratch folder in %TEMP%, returning
        // the folder holding the new files. Runs off the UI thread.
        private static string DownloadAndUnpack(Release release)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "Onkey-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            string zip = Path.Combine(scratch, AssetName);
            using (WebClient web = NewClient()) web.DownloadFile(release.Download, zip);

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
        // close, copies the new files over this folder and starts Onkey again.
        private void Relaunch(string files)
        {
            ProcessStartInfo start = new ProcessStartInfo(Path.Combine(files, "Onkey.exe"),
                "--install " + Process.GetCurrentProcess().Id + " " + Arg(files) + " " + Arg(appFolder));
            start.UseShellExecute = false;
            start.WorkingDirectory = files;
            try { Process.Start(start); }
            catch (Exception ex)
            {
                MessageBox.Show("Onkey couldn't update: " + ex.Message, "Onkey");
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
            release.Page = "https://github.com/nolpearce/onkey/releases/tag/" + Uri.EscapeDataString(tag.Groups[1].Value);
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

        private static void Open(string url)
        {
            try { Process.Start(url); } catch { /* No browser; nothing more to do. */ }
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
