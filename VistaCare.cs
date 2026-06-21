using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// =====================================================================
//  VistaCare -- a tiny tray screen dimmer for Windows
//  ---------------------------------------------------------------------
//  Dims ALL monitors by scaling the GPU gamma ramp. The gamma table is
//  applied at the final scan-out stage of the display pipeline -- AFTER
//  the cursor, taskbar and Start menu are composited -- so every pixel
//  dims uniformly. There is no overlay window, and none of the "bright
//  cursor / bright taskbar" bugs that overlay-based dimmers suffer from.
//
//  Limits (inherent to gamma dimming, not bugs):
//    * HDR monitors ignore gamma changes -- turn HDR off to dim them.
//    * Windows clamps how dark gamma can go (anti-lockout safeguard).
//    * Shares the single gamma table with Night Light / f.lux, so don't
//      run those at the same time or they will fight over the colors.
// =====================================================================

[assembly: AssemblyTitle("VistaCare")]
[assembly: AssemblyProduct("VistaCare")]
[assembly: AssemblyDescription("Dims all monitors below their hardware minimum via the GPU gamma ramp.")]
[assembly: AssemblyCompany("VistaCare")]
[assembly: AssemblyCopyright("Copyright (C) 2026")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace VistaCare
{
    static class Program
    {
        static Mutex _mutex; // held for the whole process lifetime to enforce single-instance

        // Fallback auto-start time until the user picks one from the tray menu.
        public const int DefaultHour = 19; // 7pm

        [STAThread]
        static void Main(string[] args)
        {
            // The scheduled task launches us with "/auto". Before the chosen start hour we
            // bow out silently, so the screen is never auto-dimmed earlier in the day.
            if (IsAutoLaunch(args) && DateTime.Now.Hour < Config.LoadStartHour(DefaultHour))
                return;

            bool createdNew;
            _mutex = new Mutex(true, "VistaCare.SingleInstance.6B1E0F2A", out createdNew);
            if (!createdNew) return; // another copy is already running

            // Whatever happens, never leave the screen stuck dim.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate { SafeRestore(); Application.Exit(); };
            AppDomain.CurrentDomain.UnhandledException += delegate { SafeRestore(); };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (TrayApp app = new TrayApp())
                Application.Run(app);

            GC.KeepAlive(_mutex);
        }

        static void SafeRestore() { try { Gamma.Apply(1.0); } catch { } }

        static bool IsAutoLaunch(string[] args)
        {
            if (args != null)
                foreach (string a in args)
                    if (string.Equals(a, "/auto", StringComparison.OrdinalIgnoreCase))
                        return true;
            return false;
        }

        // 19 -> "7pm", 13 -> "1pm", 0 -> "12am".
        public static string HourLabel(int hour24)
        {
            int h = hour24 % 12; if (h == 0) h = 12;
            return h + (hour24 % 24 < 12 ? "am" : "pm");
        }
    }

    // ----------------------------------------------------------------
    //  Persisted settings, stored under %APPDATA%\VistaCare
    // ----------------------------------------------------------------
    static class Config
    {
        static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VistaCare");

        public static double LoadLevel(double fallback) { double v; return TryRead("level.txt", out v) ? v : fallback; }
        public static void SaveLevel(double level) { Write("level.txt", level.ToString("0.###", CultureInfo.InvariantCulture)); }

        public static int LoadStartHour(int fallback)
        {
            double v;
            if (TryRead("starthour.txt", out v) && v >= 0 && v <= 23) return (int)v;
            return fallback;
        }
        public static void SaveStartHour(int hour) { Write("starthour.txt", hour.ToString(CultureInfo.InvariantCulture)); }

        static bool TryRead(string name, out double value)
        {
            value = 0;
            try
            {
                string p = Path.Combine(Folder, name);
                if (File.Exists(p))
                    return double.TryParse(File.ReadAllText(p).Trim(),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            }
            catch { }
            return false;
        }

        static void Write(string name, string text)
        {
            try { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, name), text); }
            catch { }
        }
    }

    // ----------------------------------------------------------------
    //  Native gamma control
    // ----------------------------------------------------------------
    static class Gamma
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Ramp
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DisplayDevice
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr hdc, ref Ramp ramp);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initData);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);

        const int AttachedToDesktop = 0x1;
        const int MirroringDriver = 0x8;

        // brightness: 1.0 = normal, lower = dimmer. Applied to every real monitor.
        public static void Apply(double brightness)
        {
            if (brightness < 0.05) brightness = 0.05;
            if (brightness > 1.00) brightness = 1.00;
            Ramp ramp = Build(brightness);

            bool any = false;
            DisplayDevice dd = new DisplayDevice();
            dd.cb = Marshal.SizeOf(typeof(DisplayDevice));

            // Walk every display adapter and set the ramp on each real monitor.
            for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
            {
                bool real = (dd.StateFlags & AttachedToDesktop) != 0
                         && (dd.StateFlags & MirroringDriver) == 0;
                if (real)
                {
                    IntPtr hdc = CreateDC(dd.DeviceName, null, null, IntPtr.Zero);
                    if (hdc != IntPtr.Zero)
                    {
                        if (SetDeviceGammaRamp(hdc, ref ramp)) any = true;
                        DeleteDC(hdc);
                    }
                }
                dd.cb = Marshal.SizeOf(typeof(DisplayDevice));
            }

            // Fallback for setups where no per-adapter device context was available.
            if (!any)
            {
                IntPtr hdc = GetDC(IntPtr.Zero);
                if (hdc != IntPtr.Zero)
                {
                    SetDeviceGammaRamp(hdc, ref ramp);
                    ReleaseDC(IntPtr.Zero, hdc);
                }
            }
        }

        static Ramp Build(double b)
        {
            Ramp r = new Ramp();
            r.Red = new ushort[256]; r.Green = new ushort[256]; r.Blue = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                int v = (int)(i * 257.0 * b + 0.5); // 257 == 65535 / 255 (full 16-bit range)
                if (v < 0) v = 0; else if (v > 65535) v = 65535;
                r.Red[i] = r.Green[i] = r.Blue[i] = (ushort)v;
            }
            return r;
        }
    }

    // ----------------------------------------------------------------
    //  Tray application
    // ----------------------------------------------------------------
    class TrayApp : ApplicationContext
    {
        static readonly int[] Presets = { 100, 90, 80, 70, 60, 50, 40, 30, 20 };
        const int FirstHour = 12, LastHour = 23; // auto-start times offered in the menu (noon .. 11pm)

        readonly SliderForm slider;
        readonly Icon trayIcon;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        ToolStripMenuItem pauseItem;
        ToolStripMenuItem startupItem;

        double brightness;  // last chosen dim level (kept while paused / across restarts)
        int startHour;      // hour (0-23) the auto-start task fires; also the /auto open gate
        bool paused;        // dimming temporarily suspended (screen at full brightness)
        bool startupOn;     // cached startup-task state, so the menu needn't query schtasks

        public double Brightness { get { return brightness; } }

        public TrayApp()
        {
            brightness = Clamp(Config.LoadLevel(1.0));
            startHour = Config.LoadStartHour(Program.DefaultHour);

            slider = new SliderForm(this);
            slider.ForceHandle(); // create the hidden window now so its global hotkeys go live

            trayIcon = MakeIcon();
            menu = BuildMenu();
            tray = new NotifyIcon();
            tray.Icon = trayIcon;
            tray.ContextMenuStrip = menu;
            tray.MouseClick += OnTrayClick;
            UpdateTrayText();   // sets the tooltip from the current level
            tray.Visible = true;

            // Windows wipes the gamma table on these events; re-assert our dim afterwards.
            SystemEvents.DisplaySettingsChanged += OnReassert;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            ApplyCurrent();                  // dim right away
            startupOn = Startup.IsEnabled();
            if (startupOn) Startup.Enable(startHour); // refresh the task (exe path + chosen hour)
        }

        // ---- brightness / pause ----
        // Everything that touches the gamma goes through here: the dim level, or full
        // brightness while paused.
        void ApplyCurrent() { Gamma.Apply(paused ? 1.0 : brightness); }

        public void SetBrightness(double value)
        {
            brightness = Clamp(value);
            paused = false; // an explicit level change is a clear "dim me now"
            ApplyCurrent();
            slider.SyncValue();
            UpdateTrayText();
            Config.SaveLevel(brightness);
        }

        public void Step(double delta) { SetBrightness(brightness + delta); }

        void TogglePause()
        {
            paused = !paused;
            ApplyCurrent();
            UpdateTrayText();
        }

        static double Clamp(double b) { return b < 0.10 ? 0.10 : (b > 1.0 ? 1.0 : b); }

        // ---- tray icon + menu ----
        void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) slider.ToggleNearTray();
        }

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            foreach (int p in Presets)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(p + "%");
                item.Tag = p;
                item.Click += delegate(object s, EventArgs e) {
                    SetBrightness((int)((ToolStripMenuItem)s).Tag / 100.0);
                };
                m.Items.Add(item);
            }
            m.Items.Add(new ToolStripSeparator());

            pauseItem = new ToolStripMenuItem("Pause", null, delegate { TogglePause(); });
            m.Items.Add(pauseItem);

            startupItem = new ToolStripMenuItem("Start automatically after");
            ToolStripMenuItem off = new ToolStripMenuItem("Off", null, delegate { DisableAutoStart(); });
            off.Tag = "off";
            startupItem.DropDownItems.Add(off);
            startupItem.DropDownItems.Add(new ToolStripSeparator());
            for (int h = FirstHour; h <= LastHour; h++)
            {
                ToolStripMenuItem hour = new ToolStripMenuItem(Program.HourLabel(h));
                hour.Tag = h;
                hour.Click += delegate(object s, EventArgs e) {
                    SetStartHour((int)((ToolStripMenuItem)s).Tag);
                };
                startupItem.DropDownItems.Add(hour);
            }
            m.Items.Add(startupItem);

            m.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitApp(); }));

            m.Opening += delegate { RefreshMenu(); };
            return m;
        }

        // Checkmarks are computed lazily, only when the menu is about to show.
        void RefreshMenu()
        {
            int cur = (int)Math.Round(brightness * 100);
            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi != null && mi.Tag is int) mi.Checked = (int)mi.Tag == cur;
            }
            pauseItem.Checked = paused;

            startupItem.Text = startupOn
                ? "Start automatically after " + Program.HourLabel(startHour)
                : "Start automatically (off)";
            foreach (ToolStripItem item in startupItem.DropDownItems)
            {
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi == null) continue;
                if (mi.Tag is int) mi.Checked = startupOn && (int)mi.Tag == startHour;
                else if ((mi.Tag as string) == "off") mi.Checked = !startupOn;
            }
        }

        void UpdateTrayText()
        {
            int pct = (int)Math.Round(brightness * 100);
            string s = pct >= 100 ? "VistaCare" : "VistaCare - " + pct + "%";
            tray.Text = paused ? s + " (paused)" : s;
        }

        // Picking an hour both enables auto-start and points the task at that time.
        void SetStartHour(int hour)
        {
            startHour = hour;
            Config.SaveStartHour(hour);
            Startup.Enable(hour);
            startupOn = Startup.IsEnabled();
        }

        void DisableAutoStart()
        {
            Startup.Disable();
            startupOn = Startup.IsEnabled();
        }

        // ---- re-assert after the OS resets gamma ----
        void OnReassert(object sender, EventArgs e) { try { ApplyCurrent(); } catch { } }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) OnReassert(sender, e);
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock ||
                e.Reason == SessionSwitchReason.ConsoleConnect)
                OnReassert(sender, e);
        }

        // ---- tray icon (drawn at runtime; no .ico file needed) ----
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

        static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    Rectangle rc = new Rectangle(3, 3, 26, 26);
                    using (LinearGradientBrush br = new LinearGradientBrush(
                        rc, Color.Gainsboro, Color.FromArgb(40, 40, 40), 0f))
                        g.FillEllipse(br, rc);
                    using (Pen pen = new Pen(Color.FromArgb(190, 190, 190), 2f))
                        g.DrawEllipse(pen, rc);
                }
                IntPtr hicon = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(hicon).Clone(); } // own a copy...
                finally { DestroyIcon(hicon); }                      // ...so the temp HICON can be freed
            }
        }

        // ---- shutdown ----
        void ExitApp()
        {
            Gamma.Apply(1.0); // restore full brightness
            tray.Visible = false;
            ExitThread();     // ends Application.Run -> Dispose cleans up the rest
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.DisplaySettingsChanged -= OnReassert;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                try { Gamma.Apply(1.0); } catch { }
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (slider != null) slider.Dispose();
                if (trayIcon != null) trayIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ----------------------------------------------------------------
    //  Volume-style slider popup (also hosts the global hotkeys)
    // ----------------------------------------------------------------
    class SliderForm : Form
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        const uint ModAlt = 0x1, ModControl = 0x2;
        const uint VkPageUp = 0x21, VkPageDown = 0x22, VkHome = 0x24;
        const int WmHotkey = 0x0312;

        readonly TrayApp app;
        readonly TrackBar bar;
        readonly Label label;
        bool suppress; // true while we set bar.Value programmatically, to ignore the echo

        public SliderForm(TrayApp owner)
        {
            app = owner;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(66, 210);
            BackColor = Color.FromArgb(32, 32, 32);

            label = new Label();
            label.Dock = DockStyle.Top;
            label.Height = 30;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.ForeColor = Color.White;
            label.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            label.Text = "100%";

            bar = new TrackBar();
            bar.Orientation = Orientation.Vertical;
            bar.Minimum = 10;
            bar.Maximum = 100;
            bar.TickFrequency = 10;
            bar.LargeChange = 10;
            bar.SmallChange = 5;
            bar.Dock = DockStyle.Fill;
            bar.ValueChanged += OnBarChanged;

            Controls.Add(bar);
            Controls.Add(label);
        }

        // Touch Handle so the window is created now (and OnHandleCreated registers the hotkeys).
        public void ForceHandle() { GC.KeepAlive(Handle); }

        void OnBarChanged(object sender, EventArgs e)
        {
            label.Text = bar.Value + "%";
            if (!suppress) app.SetBrightness(bar.Value / 100.0);
        }

        public void SyncValue()
        {
            int v = (int)Math.Round(app.Brightness * 100);
            if (v < bar.Minimum) v = bar.Minimum;
            else if (v > bar.Maximum) v = bar.Maximum;
            suppress = true;
            bar.Value = v;
            suppress = false;
            label.Text = v + "%";
        }

        public void ToggleNearTray()
        {
            if (Visible) { Hide(); return; }
            // Open right next to the cursor (which sits on the tray icon when clicked).
            Point c = Cursor.Position;
            Rectangle wa = Screen.FromPoint(c).WorkingArea;
            int x = c.X - 8;          // hug the cursor, nudged a touch left
            int y = c.Y - Height / 2; // vertically centered on the cursor
            if (x + Width > wa.Right) x = c.X - Width - 3; // no room on the right -> flip left
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            else if (y + Height > wa.Bottom) y = wa.Bottom - Height;
            Location = new Point(x, y);
            SyncValue();
            Show();
            Activate();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Hide(); // dismiss when it loses focus, like the volume flyout
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotKey(Handle, 1, ModControl | ModAlt, VkPageUp);   // Ctrl+Alt+PageUp   -> brighter
            RegisterHotKey(Handle, 2, ModControl | ModAlt, VkPageDown); // Ctrl+Alt+PageDown -> dimmer
            RegisterHotKey(Handle, 3, ModControl | ModAlt, VkHome);     // Ctrl+Alt+Home     -> reset 100%
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey(Handle, 1);
            UnregisterHotKey(Handle, 2);
            UnregisterHotKey(Handle, 3);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey)
            {
                switch (m.WParam.ToInt32())
                {
                    case 1: app.Step(+0.05); break;
                    case 2: app.Step(-0.05); break;
                    case 3: app.SetBrightness(1.0); break;
                }
            }
            base.WndProc(ref m);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: keep out of Alt-Tab
                return cp;
            }
        }
    }

    // ----------------------------------------------------------------
    //  "Start automatically after <hour>" -- a per-user Scheduled Task
    //  (no admin needed).
    //
    //  We use Task Scheduler rather than an HKCU Run entry because the Run
    //  key fires very early in logon, before the shell / GPU / a secondary
    //  drive may be ready, so it can silently fail. The task launches
    //  "VistaCare.exe /auto" from two triggers -- daily at the chosen hour
    //  and at logon -- with StartWhenAvailable to catch a run missed while
    //  the PC was off/asleep. The exe only stays open for an /auto launch at
    //  or after that hour (see Program.Main), so it never auto-dims earlier.
    // ----------------------------------------------------------------
    static class Startup
    {
        const string TaskName = "VistaCare";

        public static bool IsEnabled() { return Run("/Query /TN \"" + TaskName + "\"") == 0; }

        public static void Disable() { Run("/Delete /F /TN \"" + TaskName + "\""); }

        public static void Enable(int hour)
        {
            try
            {
                string file = Path.GetTempFileName();
                File.WriteAllText(file, BuildXml(Application.ExecutablePath, hour), Encoding.Unicode);
                Run("/Create /F /TN \"" + TaskName + "\" /XML \"" + file + "\"");
                try { File.Delete(file); } catch { }
            }
            catch { }
        }

        // Runs schtasks.exe hidden and returns its exit code (-1 if it couldn't start).
        static int Run(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(
                    Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd(); // drain the pipes so schtasks can't block
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        static string BuildXml(string exePath, int hour)
        {
            string user = WindowsIdentity.GetCurrent().Name;
            string label = Program.HourLabel(hour);
            string daily = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                         + "T" + hour.ToString("00", CultureInfo.InvariantCulture) + ":00:00";
            return
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo>\r\n" +
"    <Description>Starts VistaCare automatically from " + label + ".</Description>\r\n" +
"  </RegistrationInfo>\r\n" +
"  <Triggers>\r\n" +
"    <CalendarTrigger>\r\n" +
"      <StartBoundary>" + daily + "</StartBoundary>\r\n" +
"      <Enabled>true</Enabled>\r\n" +
"      <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>\r\n" +
"    </CalendarTrigger>\r\n" +
"    <LogonTrigger>\r\n" +
"      <Enabled>true</Enabled>\r\n" +
"      <UserId>" + Xml(user) + "</UserId>\r\n" +
"    </LogonTrigger>\r\n" +
"  </Triggers>\r\n" +
"  <Principals>\r\n" +
"    <Principal id=\"Author\">\r\n" +
"      <UserId>" + Xml(user) + "</UserId>\r\n" +
"      <LogonType>InteractiveToken</LogonType>\r\n" +
"      <RunLevel>LeastPrivilege</RunLevel>\r\n" +
"    </Principal>\r\n" +
"  </Principals>\r\n" +
"  <Settings>\r\n" +
"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
"    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
"    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
"    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
"    <Enabled>true</Enabled>\r\n" +
"    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" + // no time limit -> don't kill the tray app
"  </Settings>\r\n" +
"  <Actions Context=\"Author\">\r\n" +
"    <Exec>\r\n" +
"      <Command>" + Xml(exePath) + "</Command>\r\n" +
"      <Arguments>/auto</Arguments>\r\n" +
"    </Exec>\r\n" +
"  </Actions>\r\n" +
"</Task>\r\n";
        }

        static string Xml(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                            .Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
