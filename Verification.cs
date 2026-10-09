using System.Net.NetworkInformation;
using System.Text.Json;

namespace NetworkMonitor;

internal static class Verification
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    private static bool IsUncovered(Form window, Point point) => GetAncestor(WindowFromPoint(window.PointToScreen(point)), 2) == window.Handle;
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = VerifyUsage(directory);
        results.AddRange(VerifyStartup(directory));
        void Check(bool value, string name) { if (!value) throw new Exception(name); results.Add("PASS " + name); }
        Check(Application.HighDpiMode == HighDpiMode.PerMonitorV2, "application renders natively for each monitor's DPI");
        using (var welcome = new WelcomeForm())
        {
            welcome.Show(); Application.DoEvents();
            using var bitmap = new Bitmap(welcome.Width, welcome.Height);
            welcome.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, "github-welcome.png"));
            welcome.Close();
        }
        using var form = new MainForm(preview: true, statePath: Path.Combine(directory, "preview-state.json"));
        var nic = NetworkInterface.GetAllNetworkInterfaces().First();
        form.AddPreview(nic, false); form.AddPreview(nic, true);
        form.Show(); Application.DoEvents();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, form.ClientRectangle with { Width = form.Width, Height = form.Height }); bitmap.Save(Path.Combine(directory, "dashboard.png")); }
        form.SetFloating(true); Application.DoEvents();
        Check(form.Floating is { Visible: true, TopMost: true, Owner: null }, "floating window is independent and topmost");
        Check(form.Floating!.FormBorderStyle == FormBorderStyle.None && !form.Floating.ControlBox, "floating has no border, title bar or close button");
        Check(form.Floating.Region != null && !form.Floating.Region.IsVisible(0, 0) && form.Floating.Region.IsVisible(form.Floating.Width / 2, form.Floating.Height / 2), "floating clips corners with a rounded region");
        form.Floating!.UpdateRates(98765, 2345678, 34567, 789012, 4567891234, 234567891, 50L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024);
        using (var bitmap = new Bitmap(form.Floating.Width, form.Floating.Height))
        {
            form.Floating.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            using var rounded = new Bitmap(bitmap.Width, bitmap.Height);
            using var graphics = Graphics.FromImage(rounded);
            graphics.SetClip(form.Floating.Region!, System.Drawing.Drawing2D.CombineMode.Replace); graphics.DrawImageUnscaled(bitmap, 0, 0);
            rounded.Save(Path.Combine(directory, "floating.png"));
        }
        var originalTheme = Theme.Current;
        form.SetTheme(Theme.WinUITheme); Application.DoEvents();
        Check(form.Opacity < 1 && form.Opacity > 0.85 && form.Floating.Opacity < 1 && form.Floating.Opacity >= 0.96, "WinUI keeps both windows translucent with a more opaque floating window for legibility");
        Check(form.BackColor == Theme.Background && form.Floating.BackColor == Theme.Background && form.Floating.Controls[0].BackColor == Theme.Background, "live theme switch updates both windows and floating background");
        IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Descendants(x)));
        Check(Descendants(form).OfType<Label>().Where(x => x.Text == "↓ 下载速率").All(x => x.ForeColor == Theme.Muted), "live switch updates label color roles");
        Check(Descendants(form).OfType<Label>().Where(x => x.Text == "本月累计 · 上传 + 下载").All(x => x.Font.FontFamily.Name == Theme.FontFamily), "live switch updates fonts without changing metric labels");
        using (var backdrop = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = Screen.FromControl(form).WorkingArea, BackColor = Color.FromArgb(35, 63, 99) })
        {
            backdrop.Show(); form.BringToFront(); form.Activate(); form.Floating.EnsureTopMost();
            Application.DoEvents(); Thread.Sleep(250); Application.DoEvents();
            form.Refresh(); form.Floating.Refresh(); Application.DoEvents();
            void CaptureScreen(Form window, string name)
            {
                using var screen = new Bitmap(window.Width, window.Height);
                var samples = new[] { new Point(20, 20), new Point(window.ClientSize.Width - 20, 20), new Point(window.ClientSize.Width / 2, window.ClientSize.Height / 2), new Point(20, window.ClientSize.Height - 20), new Point(window.ClientSize.Width - 20, window.ClientSize.Height - 20) };
                if (samples.All(p => IsUncovered(window, p)))
                {
                    using var graphics = Graphics.FromImage(screen);
                    graphics.CopyFromScreen(window.Location, Point.Empty, window.Size);
                }
                else window.DrawToBitmap(screen, new Rectangle(Point.Empty, screen.Size));
                screen.Save(Path.Combine(directory, name));
            }
            form.Floating.Hide(); Application.DoEvents();
            CaptureScreen(form, "winui-dashboard.png");
            form.Floating.Show(); form.Floating.EnsureTopMost(); Application.DoEvents();
            CaptureScreen(form.Floating, "winui-floating.png");
            if (IsUncovered(form, new Point(10, 10)))
            {
                using var pixel = new Bitmap(1, 1);
                using (var graphics = Graphics.FromImage(pixel)) graphics.CopyFromScreen(form.PointToScreen(new Point(10, 10)), Point.Empty, pixel.Size);
                var actual = pixel.GetPixel(0, 0);
                int Blend(byte foreground, byte background) => (int)Math.Round(foreground * form.Opacity + background * (1 - form.Opacity));
                Check(Math.Abs(actual.R - Blend(Theme.Background.R, backdrop.BackColor.R)) < 8 && Math.Abs(actual.G - Blend(Theme.Background.G, backdrop.BackColor.G)) < 8 && Math.Abs(actual.B - Blend(Theme.Background.B, backdrop.BackColor.B)) < 8, "desktop compositor actually blends the translucent window with its background");
            }
            else results.Add("SKIP desktop compositor pixel check: another window covers the test window; control rendering and opacity are checked separately");
        }
        foreach (var name in Theme.Names.Where(x => x != Theme.WinUITheme))
        {
            form.SetTheme(name); Application.DoEvents();
            Check(form.Opacity == 1 && form.Floating.Opacity == 1 && form.Floating.Controls[0].BackColor == Theme.Background, "switching to " + name + " restores opaque windows and matching backgrounds");
        }
        foreach (var name in Theme.Names)
        {
            form.SetTheme(name); Application.DoEvents();
            var toolbar = Descendants(form).Single(x => x.Name == "DashboardToolbar");
            Check(toolbar.BackColor == Theme.Background && toolbar.Controls.Cast<Control>().All(x => x.BackColor == Theme.Background && x.Region != null && !x.Region.IsVisible(0, 0)), "toolbar and rounded button exteriors match the window in " + name);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, "dashboard-" + Array.IndexOf(Theme.Names.ToArray(), name) + ".png"));
        }
        form.SetTheme(Theme.WinUITheme); Application.DoEvents();
        form.HideToTray(); Application.DoEvents();
        Check(!form.Visible && form.Floating.Visible, "hiding dashboard leaves floating window visible");
        form.RestoreFromTray(); Application.DoEvents();
        Check(form.Visible, "dashboard restores from tray");
        var floatingSurface = form.Floating.Controls[0];
        var original = form.Floating.Location;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Mouse(string name, MouseEventArgs args) => typeof(Control).GetMethod(name, flags)!.Invoke(floatingSurface, new object[] { args });
        Mouse("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 50, 50, 0));
        Mouse("OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 80, 70, 0));
        Mouse("OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 80, 70, 0));
        Check(form.Floating.Location == new Point(original.X + 30, original.Y + 20), "borderless floating can be dragged from its content");
        var floating = form.Floating;
        var area = Screen.FromControl(floating).WorkingArea;
        floating.Location = new Point(area.Left + 60, area.Top + 60);
        var beforeResize = floating.Size; var resizeOrigin = floating.Location;
        var gripPoint = new Point(floating.Width - 17, floating.Height - 17);
        Mouse("OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, gripPoint.X, gripPoint.Y, 0));
        Check(floatingSurface.Cursor == Cursors.SizeNWSE, "bottom-right grip advertises diagonal resizing");
        int commits = 0; floating.BoundsCommitted += () => commits++;
        Mouse("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, gripPoint.X, gripPoint.Y, 0));
        Mouse("OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, gripPoint.X + 60, gripPoint.Y + 40, 0));
        Mouse("OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, gripPoint.X + 60, gripPoint.Y + 40, 0));
        Check(floating.Size == new Size(beforeResize.Width + 60, beforeResize.Height + 40) && floating.Location == resizeOrigin && commits == 1, "bottom-right dragging resizes without moving and commits once");
        gripPoint = new Point(floating.Width - 17, floating.Height - 17);
        Mouse("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, gripPoint.X, gripPoint.Y, 0));
        Mouse("OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, -1000, -1000, 0));
        Mouse("OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, -1000, -1000, 0));
        Check(floating.Size == floating.MinimumSize && floating.Location == resizeOrigin, "resizing clamps at a readable minimum size");
        gripPoint = new Point(floating.Width - 17, floating.Height - 17);
        Mouse("OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left, 2, gripPoint.X, gripPoint.Y, 0));
        Check(form.Floating == floating, "double-clicking the resize grip keeps the floating window open");
        var comparison = (RateComparison)floating.Controls[0];
        foreach (var item in new[]
        {
            (name: "full", size: new Size(430, 338), density: RateComparison.LayoutDensity.Full),
            (name: "compact", size: new Size(320, 230), density: RateComparison.LayoutDensity.Compact),
            (name: "minimal", size: new Size(240, 144), density: RateComparison.LayoutDensity.Minimal),
            (name: "large", size: new Size(640, 450), density: RateComparison.LayoutDensity.Full)
        })
        {
            floating.RestoreSize(item.size); Application.DoEvents();
            Check(comparison.Density == item.density && floating.Region!.IsVisible(floating.Width / 2, floating.Height / 2), "floating adapts content and rounded shape at " + item.name + " size");
            using var bitmap = new Bitmap(floating.Width, floating.Height);
            floating.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, "floating-" + item.name + ".png"));
        }
        // Render at real device pixel dimensions without changing the user's display settings.
        foreach (int dpi in new[] { 96, 120, 144, 192 })
        foreach (var logical in new[] { new Size(430, 338), new Size(320, 230), new Size(240, 144) })
        {
            var pixels = new Size(logical.Width * dpi / 96, logical.Height * dpi / 96);
            using var bitmap = new Bitmap(pixels.Width, pixels.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Theme.Background);
                comparison.Render(graphics, pixels, dpi);
            }
            bitmap.Save(Path.Combine(directory, $"floating-dpi-{dpi}-{logical.Width}.png"));
        }
        var rememberedSize = floating.LogicalSize;
        Mouse("OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left, 2, 50, 50, 0));
        Check(form.Floating == null, "double click closes floating and synchronizes toggle");
        form.SetFloating(true); Check(form.Floating != null, "floating can reopen after closing");
        Check(form.Floating!.Opacity < 1, "reopened floating retains translucent theme");
        Check(form.Floating.LogicalSize == rememberedSize, "closing and reopening the floating window preserves its size");
        form.Close(); Check(form.Floating == null, "exit closes floating window");
        var themePath = Path.Combine(directory, "theme-state.json");
        var themeStore = new StateStore(themePath); themeStore.Save(new AppState { ThemeName = Theme.WinUITheme, FloatingWidth = 320, FloatingHeight = 230, FloatingX = int.MaxValue, FloatingY = int.MaxValue });
        Theme.Set(originalTheme, false);
        using (var restored = new MainForm(preview: true, statePath: themePath))
        {
            restored.Show(); restored.SetFloating(true); Application.DoEvents();
            Check(Theme.Current == Theme.WinUITheme && restored.Opacity < 1 && restored.Floating!.Opacity < 1, "saved theme restores both windows on restart");
            Check(restored.Floating!.LogicalSize == new Size(320, 230) && Screen.FromControl(restored.Floating).WorkingArea.Contains(restored.Floating.Bounds), "restart restores saved floating size and brings offscreen bounds into view");
            restored.Close();
        }
        var trayPath = Path.Combine(directory, "tray-state.json");
        new StateStore(trayPath).Save(new AppState { ShowFloating = true, ThemeName = Theme.WinUITheme });
        using (var trayed = new MainForm(startInTray: true, preview: true, statePath: trayPath))
        {
            trayed.Show(); Application.DoEvents();
            Check(!trayed.Visible && trayed.Floating is { Visible: true } && !Application.OpenForms.Cast<Form>().Any(x => x is WelcomeForm), "login startup hides the dashboard without a welcome dialog and preserves the floating window preference");
            trayed.RestoreFromTray(); Application.DoEvents();
            Check(trayed.Visible, "dashboard launched at login can later restore from tray");
            trayed.Close();
        }
        Theme.Set(originalTheme, false);
        File.WriteAllLines(Path.Combine(directory, "verification.txt"), results);
    }

    public static void RunUsage(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "usage-verification.txt"), VerifyUsage(directory));
    }

    public static void RunShutdown(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); results.Add("PASS " + name); }
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        T Field<T>(MainForm form, string name) => (T)typeof(MainForm).GetField(name, flags)!.GetValue(form)!;
        var fixture = Path.Combine(directory, "shutdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        string StatePath(string name) => Path.Combine(fixture, name, "state.json");
        MainForm Create(string name, out TestNetworkInterface ethernet, out TestNetworkInterface wlan)
        {
            var form = new MainForm(preview: true, statePath: StatePath(name), persistPreview: true);
            var state = Field<AppState>(form, "state");
            state.EthernetMonthlyLimitBytes = 5000;
            state.GetUsage("ethernet").Add(100, 200, DateTime.Now);
            state.GetUsage("wlan").Add(300, 400, DateTime.Now);
            ethernet = new TestNetworkInterface("ethernet", NetworkInterfaceType.Ethernet);
            wlan = new TestNetworkInterface("wlan", NetworkInterfaceType.Wireless80211);
            foreach (var nic in new[] { ethernet, wlan })
            {
                typeof(MainForm).GetMethod("AddAdapter", flags)!.Invoke(form, new object[] { nic });
                Field<Dictionary<string, CounterSampler>>(form, "samplers")[nic.Id].Sample(nic.Upload, nic.Download,
                    System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency - 1);
            }
            form.Show(); Application.DoEvents();
            form.SetFloating(true); Application.DoEvents();
            return form;
        }
        void CheckSaved(string name, long ethernet, long wlan, string reason)
        {
            var path = StatePath(name);
            var loaded = new StateStore(path).Load();
            Check(loaded.EthernetMonth == ethernet && loaded.Adapters["wlan"].Month == wlan, reason + " restores both monthly totals");
            using var log = System.Text.Json.JsonDocument.Parse(File.ReadLines(Path.Combine(Path.GetDirectoryName(path)!, "ethernet-usage.log")).Last(x => !string.IsNullOrWhiteSpace(x)));
            Check(log.RootElement.GetProperty("MonthUsedBytes").GetInt64() == ethernet && log.RootElement.GetProperty("RemainingBytes").GetInt64() == 5000 - ethernet,
                reason + " saves the latest Ethernet total and remaining quota independently");
            var monthLog = Directory.GetFiles(Path.GetDirectoryName(path)!, "usage-*.log").Single();
            using var snapshot = System.Text.Json.JsonDocument.Parse(File.ReadLines(monthLog).Last(x => !string.IsNullOrWhiteSpace(x)));
            var saved = snapshot.RootElement.GetProperty("State").Deserialize<AppState>()!;
            Check(saved.EthernetMonth == ethernet && saved.Adapters["wlan"].Month == wlan, reason + " flushes both adapters to the monthly log");
        }
        using (var form = Create("close", out var ethernet, out var wlan))
        {
            ethernet.Upload += 50; ethernet.Download += 70; wlan.Upload += 20; wlan.Download += 30;
            form.Close();
            Check(!form.IsDisposed && !form.Visible && form.Floating is { Visible: true }, "close button still hides to tray and keeps the floating window");
            CheckSaved("close", 420, 750, "close button");
            ethernet.Upload += 20; ethernet.Download += 30; wlan.Upload += 10; wlan.Download += 40;
            form.ExitApplication();
            Check(form.IsDisposed && form.Floating == null, "tray exit closes both windows");
            CheckSaved("close", 470, 800, "tray exit after hiding");
        }
        foreach (var ending in new[] { (name: "shutdown-restart", flags: IntPtr.Zero), (name: "logoff", flags: new IntPtr(unchecked((int)0x80000000))) })
        {
            using var form = Create(ending.name, out var ethernet, out var wlan);
            var timer = Field<System.Windows.Forms.Timer>(form, "timer"); timer.Start();
            form.HideToTray();
            ethernet.Upload += 50; ethernet.Download += 70; wlan.Upload += 20; wlan.Download += 30;
            var accepted = SendMessage(form.Handle, 0x0011, IntPtr.Zero, ending.flags);
            Check(accepted != IntPtr.Zero && !form.IsDisposed && timer.Enabled && form.Floating is { Visible: true }, ending.name + " query saves without disposing or stopping monitoring");
            CheckSaved(ending.name, 420, 750, ending.name + " query");
            SendMessage(form.Handle, 0x0016, IntPtr.Zero, ending.flags);
            Check(!form.IsDisposed && timer.Enabled && form.Floating is { Visible: true }, "cancelled " + ending.name + " leaves both windows and monitoring usable");
            form.RestoreFromTray();
            SendMessage(form.Handle, 0x0112, new IntPtr(0xF060), IntPtr.Zero); // SC_CLOSE: the title-bar close command.
            Check(!form.IsDisposed && !form.Visible && timer.Enabled, "close button after cancelled " + ending.name + " still saves and hides to tray");
            ethernet.Upload += 20; ethernet.Download += 30; wlan.Upload += 10; wlan.Download += 40;
            SendMessage(form.Handle, 0x0011, IntPtr.Zero, ending.flags);
            // Simulate Windows closing the independent floating window before the dashboard.
            form.Floating!.Close();
            ethernet.Upload += 10; ethernet.Download += 20; wlan.Upload += 5; wlan.Download += 15;
            SendMessage(form.Handle, 0x0016, new IntPtr(1), ending.flags);
            Check(form.IsDisposed && form.Floating == null, "confirmed " + ending.name + " cleans up the monitor");
            CheckSaved(ending.name, 500, 820, "confirmed " + ending.name);
            Check(new StateStore(StatePath(ending.name)).Load().ShowFloating, ending.name + " preserves the floating preference when Windows closes it first");
        }
        using (var form = Create("unavailable-nic", out var ethernet, out var wlan))
        {
            ethernet.FailRead = true; wlan.Download += 30;
            form.ExitApplication();
            CheckSaved("unavailable-nic", 300, 730, "disconnected NIC during exit");
        }
        File.WriteAllLines(Path.Combine(directory, "shutdown-verification.txt"), results);
    }

    private sealed class TestNetworkInterface(string id, NetworkInterfaceType type) : NetworkInterface
    {
        public long Upload = 1000, Download = 2000;
        public bool FailRead;
        public override string Id => id;
        public override string Name => id;
        public override string Description => "Verification adapter";
        public override NetworkInterfaceType NetworkInterfaceType => type;
        public override OperationalStatus OperationalStatus => OperationalStatus.Up;
        public override IPInterfaceStatistics GetIPStatistics() => FailRead ? throw new NetworkInformationException() : new TestStatistics(Upload, Download);
    }

    private sealed class TestStatistics(long upload, long download) : IPInterfaceStatistics
    {
        public override long BytesSent => upload;
        public override long BytesReceived => download;
        public override long IncomingPacketsDiscarded => 0;
        public override long IncomingPacketsWithErrors => 0;
        public override long IncomingUnknownProtocolPackets => 0;
        public override long NonUnicastPacketsReceived => 0;
        public override long NonUnicastPacketsSent => 0;
        public override long OutgoingPacketsDiscarded => 0;
        public override long OutgoingPacketsWithErrors => 0;
        public override long OutputQueueLength => 0;
        public override long UnicastPacketsReceived => 0;
        public override long UnicastPacketsSent => 0;
    }

    private static List<string> VerifyStartup(string directory)
    {
        var results = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); results.Add("PASS " + name); }
        var executable = Path.Combine(AppContext.BaseDirectory, "NetworkMonitor.exe");
        bool previous = StartupTaskService.IsEnabled();
        dynamic task = StartupTaskService.CreateDefinition(executable);
        Check((int)task.Principal.RunLevel == 1 && (int)task.Principal.LogonType == 3, "startup definition requests highest privileges without storing a password");
        Check((int)task.Triggers.Item(1).Type == 9 && (string)task.Triggers.Item(1).Delay == "PT5S", "startup triggers after the current user logs on");
        Check((string)task.Actions.Item(1).Path == executable && (string)task.Actions.Item(1).Arguments == "--tray" && (string)task.Actions.Item(1).WorkingDirectory == AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "startup launches the installed executable into tray with the correct working directory");
        Check(!(bool)task.Settings.DisallowStartIfOnBatteries && !(bool)task.Settings.StopIfGoingOnBatteries && (string)task.Settings.ExecutionTimeLimit == "PT0S", "startup monitoring continues on battery without a time limit");
        File.WriteAllText(Path.Combine(directory, "startup-definition.xml"), (string)task.XmlText);
        string name = "NetworkMonitor-Verification-" + Guid.NewGuid().ToString("N");
        try
        {
            // Exercise the same registration and deletion using a temporary ordinary task.
            // The highest-privilege production definition above requires installation-time UAC.
            task.Principal.RunLevel = 0;
            StartupTaskService.RegisterDefinition(name, task);
            Check(StartupTaskService.IsEnabled(name), "task scheduler registers the temporary login task");
            StartupTaskService.SetEnabled(false, executable, name);
            Check(!StartupTaskService.IsEnabled(name), "task scheduler removes the temporary login task");
        }
        finally { StartupTaskService.SetEnabled(false, executable, name); }
        Check(StartupTaskService.IsEnabled() == previous, "verification leaves the user's actual startup setting unchanged");
        return results;
    }

    private static List<string> VerifyUsage(string directory)
    {
        var results = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); results.Add("PASS " + name); }
        var sampler = new CounterSampler();
        Check(sampler.Sample(100, 200, 1) == default, "first sample is a baseline");
        Check(sampler.Sample(300, 800, 3) == (200, 600, 100, 300), "rates use actual elapsed time");
        Check(sampler.Sample(0, 0, 4) == default, "counter reset does not inflate usage");

        var jan = new DateTime(2026, 1, 2);
        var feb = new DateTime(2026, 2, 1);
        var usage = new Usage();
        usage.Add(100, 200, jan.AddDays(-1));
        usage.Add(50, 70, jan);
        Check(usage.Upload == 150 && usage.Download == 270 && usage.Total == 420 && usage.Today == 120 && usage.Month == 420, "day rollover preserves monthly totals");
        var state = new AppState { GithubWelcomeShown = true, EthernetMonthlyLimitBytes = 1024 };
        state.Adapters["ethernet"] = usage;
        var wlan = state.GetUsage("wlan", jan); wlan.Add(300, 400, jan);
        var path = Path.Combine(directory, "test-state.json");
        var store = new StateStore(path); store.Save(state);
        var restarted = store.Load(jan);
        Check(restarted.Adapters["ethernet"].Total == 420 && restarted.Adapters["wlan"].Total == 700 && restarted.GithubWelcomeShown, "same-month restart preserves both adapter totals and preferences");
        Check(!restarted.RollOver(jan), "same-day checks do not change usage");
        store.Save(state); File.WriteAllText(path, "broken JSON");
        Check(store.Load(jan).Adapters["ethernet"].Total == 420 && store.Warning != null, "corrupt state recovers backup");

        usage.RollOver(feb);
        Check(usage.Upload == 0 && usage.Download == 0 && usage.Month == 0 && usage.Today == 0 && usage.MonthKey == "2026-02", "month boundary clears upload download daily and monthly usage without traffic");
        usage.Add(5, 7, feb);
        usage.Add(2, 3, feb.AddHours(10));
        Check(usage.Total == 17 && usage.Month == 17 && usage.Today == 17, "first day resets once and keeps new traffic");
        usage.Add(1, 2, feb.AddDays(1));
        Check(usage.Total == 20 && usage.Today == 3, "second day preserves monthly usage");

        Check(restarted.RollOver(feb), "runtime month rollover is reported for immediate saving");
        Check(restarted.Adapters.Values.All(x => x.Total == 0 && x.Month == 0 && x.Today == 0), "runtime rollover clears Ethernet WLAN and inactive adapters together");
        Check(restarted.EthernetMonthlyLimitBytes == 1024 && restarted.GithubWelcomeShown, "rollover preserves quota and preferences");
        Check(!restarted.RollOver(feb.AddHours(1)), "repeat month-boundary check does not reset again");
        var afterBackup = store.Load(feb);
        Check(afterBackup.Adapters.Values.All(x => x.Total == 0 && x.Month == 0), "previous-month backup resets before use");
        var afterGap = store.Load(new DateTime(2026, 4, 15));
        Check(afterGap.Adapters.Values.All(x => x.Total == 0 && x.MonthKey == "2026-04"), "restart after missing several months resets before counting");
        store.Save(afterGap);
        Check(store.Load(new DateTime(2026, 4, 15)).Adapters.Values.All(x => x.Total == 0), "reset state survives saving and restarting");

        var yearEnd = new Usage();
        yearEnd.Add(100, 200, new DateTime(2026, 12, 31, 23, 59, 59));
        yearEnd.Add(10, 20, new DateTime(2027, 1, 1));
        Check(yearEnd.Total == 30 && yearEnd.Month == 30 && yearEnd.Today == 30, "year boundary starts fresh before adding traffic");

        var legacy = new AppState();
        legacy.Adapters["ethernet"] = new Usage { Upload = 9000, Download = 8000, MonthUpload = 100, MonthDownload = 200, MonthKey = "2026-01", Day = "2026-01-02", TodayUpload = 10, TodayDownload = 20 };
        legacy.Adapters["wlan"] = new Usage { Upload = 7000, Download = 6000, MonthUpload = 300, MonthDownload = 400, MonthKey = "2026-01", Day = "2026-01-02" };
        store.Save(legacy);
        var migrated = store.Load(jan);
        Check(migrated.Adapters["ethernet"].Total == 300 && migrated.Adapters["ethernet"].Today == 30 && migrated.Adapters["wlan"].Total == 700, "upgrade replaces lifetime totals with saved current-month totals");
        store.Save(migrated);
        Check(store.Load(jan).Adapters["ethernet"].Total == 300, "migration remains stable after restart");
        var undated = new Usage { Upload = 1000, Download = 2000 };
        undated.RollOver(jan);
        Check(undated.Total == 0 && undated.MonthKey == "2026-01", "undated legacy usage does not leak into current month");

        var logDirectory = Path.Combine(directory, "log-recovery");
        var logPath = Path.Combine(logDirectory, "state.json");
        var logStore = new StateStore(logPath);
        logStore.Save(legacy, writeLog: true);
        File.WriteAllText(logPath, "broken JSON"); File.WriteAllText(logPath + ".bak", "broken JSON");
        Check(logStore.Load(jan).Adapters["ethernet"].Total == 300, "log recovery migrates same-month legacy totals");
        Check(logStore.Load(feb).Adapters.Values.All(x => x.Total == 0 && x.Month == 0), "previous-month log recovery cannot restore old totals");

        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            var regional = new Usage(); regional.Add(1, 2, jan);
            Check(regional.MonthKey == "2026-01" && regional.Day == "2026-01-02", "month keys use Gregorian dates regardless of Windows regional calendar");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = originalCulture; }
        results.AddRange(VerifyEthernetRecovery(Path.Combine(directory, "ethernet-recovery")));
        return results;
    }

    private static List<string> VerifyEthernetRecovery(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); results.Add("PASS " + name); }
        var now = new DateTime(2026, 1, 15, 10, 0, 0);
        var path = Path.Combine(directory, "state.json");
        var store = new StateStore(path);
        // This fixture owns only its temporary files and starts fresh when verification is repeated.
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Check(store.Load(now).Adapters.Count == 0 && store.Warning == null, "first run with no records starts cleanly");
        var state = new AppState { ThemeName = "夜色深蓝", EthernetMonthlyLimitBytes = 1000 };
        state.EthernetAdapterIds.UnionWith(new[] { "wired-1", "wired-2" });
        state.GetUsage("wired-1", now).Add(100, 200, now);
        state.GetUsage("wired-2", now).Add(50, 70, now);
        state.GetUsage("wlan", now).Add(9000, 8000, now);
        store.Save(state, writeLog: true, date: now);
        using (var record = System.Text.Json.JsonDocument.Parse(File.ReadLines(store.EthernetLogPath).Last(x => !string.IsNullOrWhiteSpace(x))))
        {
            var json = record.RootElement;
            Check(json.GetProperty("MonthKey").GetString() == "2026-01" && json.GetProperty("MonthUsedBytes").GetInt64() == 420 && json.GetProperty("RemainingBytes").GetInt64() == 580, "independent log records month used and remaining quota");
            Check(json.GetProperty("Adapters").EnumerateObject().Count() == 2 && !json.GetProperty("Adapters").TryGetProperty("wlan", out _), "independent log aggregates all Ethernet adapters and excludes WLAN");
        }
        state.GetUsage("wired-1", now).Add(10, 20, now);
        store.Save(state, writeLog: true, date: now.AddMinutes(1));
        Check(File.ReadLines(store.EthernetLogPath).Count(x => !string.IsNullOrWhiteSpace(x)) == 2, "independent log appends history instead of overwriting it");
        Check(store.Load(now).EthernetMonth == 450, "normal restart does not double count independent log usage");

        store.Save(new AppState { ThemeName = "晨曦浅金" }, date: now.AddMinutes(2));
        var recovered = store.Load(now.AddMinutes(3));
        Check(recovered.EthernetMonth == 450 && recovered.EthernetMonthlyLimitBytes == 1000 && recovered.ThemeName == "晨曦浅金", "upgrade with an empty primary state restores Ethernet totals quota and preserves current theme");
        store.Save(recovered, date: now.AddMinutes(3));
        Check(store.Load(now.AddMinutes(4)).EthernetMonth == 450, "recovered usage remains stable across repeated restarts");
        recovered.EthernetMonthlyLimitBytes = 2000;
        recovered.GetUsage("wired-1", now).Add(3, 7, now);
        store.Save(recovered, date: now.AddMinutes(4));
        var newer = store.Load(now.AddMinutes(5));
        Check(newer.EthernetMonth == 460 && newer.EthernetMonthlyLimitBytes == 2000, "older log never replaces a newer valid quota or decreases usage");
        newer.EthernetMonthlyLimitBytes = 0;
        store.Save(newer, writeLog: true, date: now.AddMinutes(6));
        using (var record = System.Text.Json.JsonDocument.Parse(File.ReadLines(store.EthernetLogPath).Last(x => !string.IsNullOrWhiteSpace(x))))
            Check(record.RootElement.GetProperty("RemainingBytes").ValueKind == System.Text.Json.JsonValueKind.Null, "unlimited quota is recorded as unset rather than exhausted");
        newer.EthernetMonthlyLimitBytes = 100;
        store.Save(newer, writeLog: true, date: now.AddMinutes(7));
        using (var record = System.Text.Json.JsonDocument.Parse(File.ReadLines(store.EthernetLogPath).Last(x => !string.IsNullOrWhiteSpace(x))))
            Check(record.RootElement.GetProperty("RemainingBytes").GetInt64() == 0, "exhausted quota is clamped to zero in the independent log");

        File.Delete(path); File.Delete(path + ".bak");
        foreach (var file in Directory.GetFiles(directory, "usage-*.log")) File.Delete(file);
        File.AppendAllText(store.EthernetLogPath, "{}\n{partial");
        recovered = store.Load(now.AddMinutes(8));
        Check(recovered.EthernetMonth == 460 && recovered.EthernetMonthlyLimitBytes == 100, "independent log alone restores missing primary backup and usage logs despite a damaged tail");
        store.Save(recovered, writeLog: true, date: now.AddMinutes(8));
        Check(store.Load(now.AddMinutes(9)).EthernetMonth == 460, "append after a partial tail keeps the new record recoverable");
        File.WriteAllText(path, "invalid JSON"); File.WriteAllText(path + ".bak", "invalid JSON");
        foreach (var file in Directory.GetFiles(directory, "usage-*.log")) File.Delete(file);
        Check(store.Load(now.AddMinutes(9)).EthernetMonth == 460, "independent log restores corrupt primary and backup");
        var feb = store.Load(new DateTime(2026, 2, 3));
        Check(feb.EthernetMonth == 0 && feb.EthernetMonthlyLimitBytes == 100, "next-month recovery preserves quota and never revives previous-month usage");

        var largeId = new string('a', 5000);
        recovered.EthernetAdapterIds.Add(largeId); recovered.GetUsage(largeId, now).Add(2, 3, now);
        store.Save(recovered, writeLog: true, date: now.AddMinutes(10));
        File.Delete(path); File.Delete(path + ".bak");
        foreach (var file in Directory.GetFiles(directory, "usage-*.log")) File.Delete(file);
        Check(store.Load(now.AddMinutes(11)).EthernetMonth == 465, "reverse log reading handles records spanning multiple buffer blocks");

        var failingPath = Path.Combine(directory, "failed-state.json");
        Directory.CreateDirectory(failingPath);
        var failingStore = new StateStore(failingPath);
        bool failed = false;
        try { failingStore.Save(recovered, writeLog: true, date: now.AddMinutes(12)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        Check(failed && failingStore.Load(now.AddMinutes(13)).EthernetMonth == 465, "independent log survives a failed primary state save");
        Directory.Delete(failingPath);

        var failureDirectory = Path.Combine(directory, "log-write-failure");
        Directory.CreateDirectory(failureDirectory);
        var warningStore = new StateStore(Path.Combine(failureDirectory, "state.json"));
        Directory.CreateDirectory(warningStore.EthernetLogPath);
        warningStore.Save(recovered, writeLog: true, date: now);
        Check(warningStore.EthernetLogWarning != null && warningStore.Load(now).EthernetMonth == 465, "independent log write failure is reported while primary state remains usable");
        return results;
    }
}
