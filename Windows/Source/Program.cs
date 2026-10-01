// Onkey desktop pet for Windows. Matches the Mac version in ../../Mac/Sources: a
// transparent window that wanders the screen, with eyes that watch the cursor,
// blinks, a mouth that opens with his sound, and settings in the tray icon menu.
// Compiled at launch by Windows PowerShell's Add-Type, so this must stay C# 5.
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
        public const string Version = "4.4.2";

        [STAThread]
        public static void Main()
        {
            bool created;
            using (Mutex single = new Mutex(true, "Local\\OnkeyDesktopPet", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Onkey is already running. Right-click his icon beside the clock for settings.", "Onkey");
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
