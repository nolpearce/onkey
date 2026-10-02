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
        public const string Version = "4.4.3";

        [STAThread]
        public static void Main(string[] args)
        {
            // The updater starts the new Onkey.exe from its download folder with these
            // arguments so it can copy itself over this install once the old one has exited.
            if (args.Length == 4 && args[0] == "--install")
            {
                Installer.Run(args[1], args[2], args[3]);
                return;
            }
            bool created;
            using (Mutex single = new Mutex(true, "Local\\OnkeyDesktopPet", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Onkey is already running. Click his icon beside the clock for settings.", "Onkey");
                    return;
                }
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new OnkeyApp());
            }
        }
    }
}
