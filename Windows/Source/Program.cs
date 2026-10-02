// Onkey desktop pet for Windows. Matches the Mac version in ../../Mac/Sources: a
// transparent window that wanders the screen, with eyes that watch the cursor,
// blinks, a mouth that opens with his sound, and settings in the tray icon menu.
// Built into Onkey.exe by build.cmd with the C# compiler that comes with .NET Framework,
// so this must stay C# 5.
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
    public static class Program
    {
        // Compared with the latest GitHub release to find updates; keep it in step with README.txt.
        public const string Version = "4.5.3";

        // Errors on the UI thread in the last few seconds; too many at once means he's stuck.
        private static readonly Queue<DateTime> recentErrors = new Queue<DateTime>();
        private static readonly Stopwatch uptime = Stopwatch.StartNew();
        private static bool installing;

        [STAThread]
        public static void Main(string[] args)
        {
            installing = args.Length == 4 && args[0] == "--install";
            Log.Start(installing ? "install" : "app");
            // Anything that goes wrong is logged and kept for a crash report, rather than
            // showing .NET's own crash window. This has to come before any window is made.
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log.Crash(installing ? "The update crashed" : "Onkey crashed", e.ExceptionObject as Exception);
            };
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { OnError(e.Exception); };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            // The updater starts the new Onkey.exe from its download folder with these
            // arguments so it can copy itself over this install once the old one has exited.
            if (installing)
            {
                try { Installer.Run(args[1], args[2], args[3]); }
                catch (Exception ex) { Log.Crash("The update crashed", ex); }
                return;
            }
            bool created;
            using (Mutex single = new Mutex(true, "Local\\OnkeyDesktopPet", out created))
            {
                // Just after an update or a restart the old Onkey may still be on his way out.
                bool handedOver = Array.IndexOf(args, "--updated") >= 0 || Array.IndexOf(args, "--restarted") >= 0;
                if (!created && !WaitFor(single, handedOver ? 20000 : 2000))
                {
                    Log.Info("Another Onkey is already running");
                    MessageBox.Show("Onkey is already running. Click his icon beside the clock for settings.", "Onkey");
                    return;
                }
                try
                {
                    Native.SetProcessDPIAware();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new OnkeyApp());
                    Log.Info("Onkey exited");
                }
                catch (Exception ex)
                {
                    // Usually he couldn't start at all, so there's no panel to offer the report in.
                    // Just after an update, the installer is watching: quitting with an error
                    // lets it put the old Onkey back, and he offers the report instead.
                    Log.Crash("Onkey couldn't start", ex);
                    if (Array.IndexOf(args, "--updated") >= 0) Environment.ExitCode = 1;
                    else if (MessageBox.Show("Onkey couldn't start: " + ex.Message + "\n\nSend a crash report? It opens a GitHub issue "
                        + "you can check before sending.", "Onkey", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        CrashReport.Send();
                }
                try { single.ReleaseMutex(); } catch { }
            }
        }

        // Waits for the other Onkey to let go of the single-instance lock.
        private static bool WaitFor(Mutex single, int milliseconds)
        {
            try { return single.WaitOne(milliseconds); }
            catch (AbandonedMutexException) { return true; }   // He exited without letting go.
        }

        // Something went wrong on the UI thread. He carries on (the next tick usually works),
        // but if errors keep coming he's stuck, so he restarts, or quits if he only just started.
        private static void OnError(Exception ex)
        {
            Log.Error("Something went wrong", ex);
            DateTime now = DateTime.Now;
            recentErrors.Enqueue(now);
            while (recentErrors.Count > 0 && (now - recentErrors.Peek()).TotalSeconds > 10) recentErrors.Dequeue();
            if (recentErrors.Count == 1) Log.Crash("Onkey hit an error", ex);
            if (recentErrors.Count < 30) return;
            Log.Crash("Onkey kept hitting errors and " + (uptime.Elapsed.TotalSeconds > 60 ? "restarted" : "stopped"), ex);
            if (uptime.Elapsed.TotalSeconds > 60)
            {
                try
                {
                    ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, "--restarted");
                    start.UseShellExecute = false;
                    Process.Start(start);
                }
                catch (Exception again) { Log.Error("Restarting", again); }
            }
            Environment.Exit(1);
        }
    }
}
