using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnkeyDesktopPet
{
    // Onkey's diary, so a crash can be looked into: what he did and what went wrong, in
    // %APPDATA%\Onkey\onkey.log beside his settings. It keeps about the last half megabyte
    // (the older half moves to onkey.old.log). When something goes badly wrong, crash.txt
    // records it until a crash report is sent, so the settings panel can offer one.
    // Nothing here ever throws: a log that can't be written is just skipped.
    // Mac/Sources/Log.swift does the same for the Mac version.
    internal static class Log
    {
        private const long MaxSize = 512 * 1024;
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Onkey");
        public static readonly string FilePath = Path.Combine(Folder, "onkey.log");
        private static readonly string OldPath = Path.Combine(Folder, "onkey.old.log");
        private static readonly string CrashPath = Path.Combine(Folder, "crash.txt");
        private static readonly object gate = new object();
        // How often each error has been seen, so one that repeats every frame logs a few
        // times and then only now and then.
        private static readonly Dictionary<string, int> seen = new Dictionary<string, int>();
        // "app", or "install" while a new Onkey.exe copies itself into place.
        private static string role = "app";
        // Whether crash.txt exists, remembered so the panel can ask every frame.
        private static int hasCrash = -1;
        private static readonly int pid = Process.GetCurrentProcess().Id;

        public static void Start(string kind)
        {
            role = kind;
            Info("Onkey " + Program.Version + " starting (" + role + ") from " + AppDomain.CurrentDomain.BaseDirectory
                + " on " + WindowsVersion() + ", .NET " + Environment.Version);
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }

        public static void Error(string what, Exception ex)
        {
            string key = what + "|" + (ex == null ? "" : ex.GetType().Name + ex.Message);
            int count;
            lock (gate)
            {
                seen.TryGetValue(key, out count);
                seen[key] = ++count;
            }
            if (count <= 3) Write("ERROR", what + ": " + Describe(ex));
            else if (count % 100 == 0) Write("ERROR", what + " (seen " + count + " times): " + (ex == null ? "" : ex.Message));
        }

        // Something went badly wrong: logged, and kept in crash.txt for a crash report.
        public static void Crash(string what, Exception ex)
        {
            Write("CRASH", what + ": " + Describe(ex));
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Folder);
                    File.WriteAllText(CrashPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " Onkey " + Program.Version
                        + " (" + role + ")\r\n" + what + "\r\n" + Describe(ex) + "\r\n");
                    hasCrash = 1;
                }
            }
            catch { }
        }

        // What crash.txt says, or null when there's no crash waiting to be reported.
        public static string PendingCrash
        {
            get
            {
                try { return File.Exists(CrashPath) ? File.ReadAllText(CrashPath) : null; }
                catch { return null; }
            }
        }

        public static bool HasCrash
        {
            get
            {
                if (hasCrash < 0) { try { hasCrash = File.Exists(CrashPath) ? 1 : 0; } catch { hasCrash = 0; } }
                return hasCrash == 1;
            }
        }

        public static void ClearCrash()
        {
            try { File.Delete(CrashPath); hasCrash = 0; } catch { }
        }

        // The end of the log (the old half too, if needed), up to the given length.
        public static string Tail(int maxChars)
        {
            StringBuilder text = new StringBuilder();
            try
            {
                lock (gate)
                {
                    if (File.Exists(OldPath)) text.Append(ReadShared(OldPath));
                    if (File.Exists(FilePath)) text.Append(ReadShared(FilePath));
                }
            }
            catch (Exception ex) { text.Append("(Couldn't read the log: " + ex.Message + ")"); }
            if (text.Length <= maxChars) return text.ToString();
            string tail = text.ToString(text.Length - maxChars, maxChars);
            int line = tail.IndexOf('\n');
            return line >= 0 && line < tail.Length - 1 ? tail.Substring(line + 1) : tail;
        }

        public static string WindowsVersion()
        {
            string name = null;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (key != null)
                    {
                        name = key.GetValue("ProductName") as string;
                        object build = key.GetValue("CurrentBuild");
                        if (name != null && build != null) name += " build " + build;
                    }
            }
            catch { }
            return (name ?? Environment.OSVersion.VersionString) + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)");
        }

        private static string Describe(Exception ex)
        {
            return ex == null ? "(no details)" : ex.ToString();
        }

        private static void Write(string level, string message)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + role + " " + pid
                    + "] " + level + " " + message.Replace("\r\n", "\n").Replace("\n", "\r\n    ") + "\r\n";
                lock (gate)
                {
                    Directory.CreateDirectory(Folder);
                    FileInfo file = new FileInfo(FilePath);
                    if (file.Exists && file.Length > MaxSize)
                    {
                        File.Delete(OldPath);
                        File.Move(FilePath, OldPath);
                    }
                    // Two Onkeys can write at once (the old one exiting while the new one
                    // installs), so open it shared and try again briefly if it's busy.
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            using (FileStream stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                            using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                                writer.Write(line);
                            break;
                        }
                        catch (IOException)
                        {
                            if (attempt >= 4) throw;
                            Thread.Sleep(20);
                        }
                    }
                }
            }
            catch { /* A log that can't be written mustn't stop Onkey. */ }
        }

        private static string ReadShared(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }

    // A crash report: Onkey's version, Windows', what crashed and the end of the log, put on
    // the clipboard and into a new GitHub issue that opens in the browser for checking before
    // it's sent. Nothing is sent by Onkey itself.
    internal static class CrashReport
    {
        private const string NewIssue = "https://github.com/nolpearce/onkey/issues/new";

        public static bool Pending { get { return Log.HasCrash; } }

        public static void Send()
        {
            string crash = Log.PendingCrash;
            Log.Info("Sending a crash report");
            string details = "Onkey " + Program.Version + " on " + Log.WindowsVersion() + ", .NET " + Environment.Version;
            string full = details + "\r\n\r\n" + (crash != null ? "What went wrong:\r\n" + crash + "\r\n" : "") + "Log:\r\n" + Log.Tail(60000);
            full = Private(full);
            try { Clipboard.SetText(full); } catch (Exception ex) { Log.Error("Copying the report", ex); }

            // A link only holds so much (GitHub turns away ones much over 8,000 characters), so
            // the issue gets the crash and as much of the end of the log as fits; the whole
            // log is on the clipboard.
            string firstLine = crash == null ? null : FirstLine(crash.Substring(crash.IndexOf('\n') + 1));
            string title = crash != null ? "Crash report: " + Cut(firstLine, 80) : "Problem report";
            string url = null;
            for (int tail = 2400; url == null || url.Length > 7000 && tail > 0; tail -= 300)
            {
                string body = "**What happened?**\n(What was Onkey doing? Were you updating him?)\n\n"
                    + "**Version:** " + details + "\n"
                    + (crash != null ? "\n**What went wrong**\n```\n" + Cut(Private(crash), 1200) + "\n```\n" : "")
                    + (tail > 0 ? "\n**The end of the log**\n```\n" + Private(Log.Tail(tail)) + "\n```\n" : "")
                    + "\nThe whole log is on your clipboard: paste it below if it helps.\n";
                url = NewIssue + "?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body.Replace("\r\n", "\n"));
            }
            try { Process.Start(url); Log.ClearCrash(); }
            catch (Exception ex)
            {
                Log.Error("Opening the crash report", ex);
                // No browser: the report is on the clipboard, so show where it can go.
                try { Process.Start("explorer.exe", "\"" + Log.Folder + "\""); } catch { }
            }
        }

        // The user's own folder name appears in paths; leave it out.
        private static string Private(string text)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 3) text = text.Replace(home, "%USERPROFILE%");
            return text.Replace(Environment.UserName + "\\", "%USERNAME%\\");
        }

        private static string FirstLine(string s)
        {
            s = s.Trim();
            int n = s.IndexOfAny(new char[] { '\r', '\n' });
            return n >= 0 ? s.Substring(0, n) : s;
        }

        private static string Cut(string s, int max) { return s.Length <= max ? s : s.Substring(0, max) + "..."; }
    }
}
