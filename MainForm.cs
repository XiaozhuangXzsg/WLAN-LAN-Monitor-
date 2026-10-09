using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace NetworkMonitor;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--verify-usage") { Verification.RunUsage(args[1]); return; }
        if (args.Length == 2 && args[0] == "--verify-ui") { Verification.Run(args[1]); return; }
        if (args.Length == 2 && args[0] == "--verify-shutdown") { Verification.RunShutdown(args[1]); return; }
        if (args.Contains("--remove-startup"))
        {
            try { StartupManager.SetEnabled(false); }
            catch { Environment.ExitCode = 1; }
            return;
        }
        using var mutex = new Mutex(true, "Local\\NetworkMonitor-" + Environment.UserName, out bool first);
        if (!first) { MessageBox.Show("网络流量监控已在运行，请双击系统托盘图标打开。", "网络流量监控"); return; }
        Application.Run(new MainForm(args.Contains("--tray")));
    }
}

internal sealed class MainForm : Form
{
    private readonly Dictionary<string, AdapterView> views = new();
    private readonly Dictionary<string, CounterSampler> samplers = new();
    private readonly Dictionary<string, Capture> captures = new();
    private readonly ConcurrentDictionary<string, string> domains = new();
    private readonly AdapterPanel ethernetTabs = new(), wlanTabs = new();
    private readonly RoundedToggle floatingToggle = Theme.Toggle("置顶流量浮窗");
    private readonly RoundedToggle trayToggle = Theme.Toggle("最小化到托盘");
    private readonly RoundedToggle startupToggle = Theme.Toggle("跟随系统启动");
    private readonly RoundedButton optimizeButton = Theme.Button("一键优化网络");
    private readonly Label status = Theme.Label("正在发现网卡…", 9, Theme.Muted);
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly NotifyIcon tray = new();
    private readonly StateStore store;
    private readonly AppState state;
    private readonly bool preview, startInTray, persistPreview;
    private FloatingForm? floating;
    private bool initializing = true, quitting, sessionEnding;
    private int ticks;
    private long ethernetUp, ethernetDown, wlanUp, wlanDown, ethernetTotal, wlanTotal;
    private string? saveError;

    public MainForm(bool startInTray = false, bool preview = false, string? statePath = null, bool persistPreview = false)
    {
        // Lifecycle verification writes only to an explicitly supplied fixture path.
        if (persistPreview && (!preview || string.IsNullOrWhiteSpace(statePath))) throw new ArgumentException("Persistent previews require an isolated state path.");
        this.preview = preview; this.startInTray = startInTray; this.persistPreview = persistPreview;
        store = new StateStore(statePath); state = store.Load();
        Theme.Set(state.ThemeName, false);
        Text = "网络流量监控";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Font = new Font(Theme.FontFamily, 9);
        BackColor = Theme.Background; ForeColor = Theme.Text;
        ClientSize = new Size(1340, 850); MinimumSize = new Size(1120, 740);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel { Name = "DashboardLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 12, 22, 10), BackColor = Theme.Background };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var header = new TableLayoutPanel { Name = "DashboardHeader", Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Theme.Background };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 62)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        header.Controls.Add(Theme.Label("网络流量概览", 23, bold: true), 0, 0);
        header.Controls.Add(Theme.Label("以太网 × WLAN   /   实时对比 · 每月 1 日清零", 10, Theme.Muted), 0, 1);
        layout.Controls.Add(header, 0, 0);
        var toolbar = new FlowLayoutPanel { Name = "DashboardToolbar", Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 5, 0, 0), BackColor = Theme.Background };
        trayToggle.Checked = state.MinimizeToTray;
        floatingToggle.Checked = state.ShowFloating;
        if (!preview)
        {
            try { startupToggle.Checked = StartupManager.IsEnabled(); }
            catch { startupToggle.Enabled = false; startupToggle.Text = "开机启动（无法读取）"; }
        }
        var hide = Theme.Button("收起到托盘"); hide.Click += (_, _) => HideToTray();
        var github = Theme.Button("GitHub 项目");
        github.Click += (_, _) =>
        {
            try { AppInfo.OpenGitHub(); }
            catch (Exception ex) { MessageBox.Show(this, "无法打开 GitHub 链接：" + ex.Message, "网络流量监控"); }
        };
        var themeButton = Theme.Button("主题 ▾");
        var themeMenu = new ContextMenuStrip { ShowImageMargin = false };
        foreach (var themeName in Theme.Names)
            themeMenu.Items.Add(themeName, null, (_, _) =>
            {
                SetTheme(themeName);
            });
        themeMenu.Opening += (_, _) =>
        {
            themeMenu.BackColor = Theme.Surface; themeMenu.ForeColor = Theme.Text;
            foreach (ToolStripMenuItem item in themeMenu.Items)
            {
                item.BackColor = Theme.Surface; item.ForeColor = Theme.Text;
                item.Checked = item.Text == Theme.Current;
                item.Font = new Font(Theme.FontFamily, 9);
            }
        };
        themeButton.Click += (_, _) => themeMenu.Show(themeButton, new Point(0, themeButton.Height));
        optimizeButton.Click += (_, _) => { _ = OptimizeNetworkAsync(); };
        toolbar.Controls.AddRange(new Control[] { floatingToggle, trayToggle, startupToggle, optimizeButton, github, themeButton, hide });
        layout.Controls.Add(toolbar, 0, 1);
        var columns = new TableLayoutPanel { Name = "DashboardColumns", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        columns.Controls.Add(Section("以太网", "ETHERNET", ethernetTabs, Theme.Blue), 0, 0);
        columns.Controls.Add(Section("校园无线网络", "WLAN", wlanTabs, Theme.Teal), 1, 0);
        layout.Controls.Add(columns, 0, 2); layout.Controls.Add(status, 0, 3);
        Controls.Add(layout);
        floatingToggle.CheckedChanged += (_, _) => { if (!initializing) ToggleFloating(); };
        trayToggle.CheckedChanged += (_, _) => { state.MinimizeToTray = trayToggle.Checked; Save(); };
        startupToggle.CheckedChanged += (_, _) => ChangeStartup();
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => RestoreFromTray());
        menu.Items.Add("打开 / 关闭浮窗", null, (_, _) => floatingToggle.Checked = !floatingToggle.Checked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        tray.Icon = Icon; tray.Text = "网络流量监控"; tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => RestoreFromTray();
        Theme.Changed += ApplyTheme;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && state.MinimizeToTray) HideToTray(); };
        FormClosing += OnClosing;
        FormClosed += OnClosed;
        Load += (_, _) =>
        {
            initializing = false;
            tray.Visible = !preview;
            if (!preview) StartMonitoring();
            if (state.ShowFloating) ToggleFloating();
        };
        Shown += (_, _) =>
        {
            if (startInTray) { BeginInvoke(new Action(HideToTray)); return; }
            if (preview) return;
            BeginInvoke(new Action(() =>
            {
                if (!state.GithubWelcomeShown)
                {
                    using var welcome = new WelcomeForm();
                    welcome.ShowDialog(this);
                    state.GithubWelcomeShown = true;
                    Save();
                }
            }));
        };
        timer.Tick += (_, _) => RefreshData();
        ApplyTheme();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindow(this);
    }
    private void ApplyTheme()
    {
        Theme.ApplyTree(this);
        Theme.ApplyWindow(this);
        floating?.ApplyTheme();
    }
    internal void SetTheme(string name)
    {
        Theme.Set(name); state.ThemeName = Theme.Current; Save();
    }

    private static Control Section(string name, string code, Control child, Color accent)
    {
        var panel = new RoundedCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        var title = Theme.Label($"{name}    /    {code}", 14, accent, true); title.Dock = DockStyle.Top; title.Height = 46;
        panel.Controls.Add(child); panel.Controls.Add(title); return panel;
    }
    private void ChangeStartup()
    {
        if (initializing || preview) return;
        try { StartupManager.SetEnabled(startupToggle.Checked); }
        catch (Exception ex)
        {
            initializing = true; startupToggle.Checked = !startupToggle.Checked; initializing = false;
            MessageBox.Show(this, "无法更改开机启动：" + ex.Message, "网络流量监控", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void Save(bool writeLog = false)
    {
        if (preview && !persistPreview) return;
        try { store.Save(state, writeLog); saveError = null; }
        catch (Exception ex) { saveError = "累计数据未保存：" + ex.Message; status.Text = saveError; }
    }
    private void SaveLatestUsage()
    {
        if (preview && !persistPreview) return;
        var now = DateTime.Now;
        state.RollOver(now);
        // Take the final counter delta without reading process tables, draining captures
        // or redrawing charts. Windows may already be disconnecting the adapters.
        foreach (var (id, view) in views)
        {
            try
            {
                var stats = view.Nic.GetIPStatistics();
                var sample = samplers[id].Sample(stats.BytesSent, stats.BytesReceived, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
                state.GetUsage(id, now).Add(sample.up, sample.down, now);
            }
            catch { samplers[id].Reset(); } // Preserve the last valid totals if a NIC disappears.
        }
        RememberFloatingBounds();
        Save(writeLog: true);
    }
    private void ToggleFloating()
    {
        if (floatingToggle.Checked && floating == null)
        {
            floating = new FloatingForm();
            if (state.FloatingWidth.HasValue && state.FloatingHeight.HasValue)
                floating.RestoreSize(new Size(state.FloatingWidth.Value, state.FloatingHeight.Value));
            var preferred = state.FloatingX.HasValue && state.FloatingY.HasValue ? new Point(state.FloatingX.Value, state.FloatingY.Value) : floating.Location;
            var area = Screen.FromPoint(preferred).WorkingArea;
            floating.Size = new Size(Math.Min(floating.Width, area.Width), Math.Min(floating.Height, area.Height));
            floating.Location = new Point(Math.Clamp(preferred.X, area.Left, Math.Max(area.Left, area.Right - floating.Width)),
                Math.Clamp(preferred.Y, area.Top, Math.Max(area.Top, area.Bottom - floating.Height)));
            floating.BoundsCommitted += () => { RememberFloatingBounds(); Save(); };
            floating.FormClosed += (_, _) =>
            {
                RememberFloatingBounds();
                floating = null;
                if (!quitting && !sessionEnding) { floatingToggle.Checked = false; state.ShowFloating = false; Save(); }
            };
            floating.Show(); // No owner: minimizing the dashboard must not hide the floating window.
            UpdateFloating();
        }
        else if (!floatingToggle.Checked) floating?.Close();
        state.ShowFloating = floatingToggle.Checked; Save();
    }
    private void UpdateFloating()
    {
        long ethernetMonth = state.EthernetMonth;
        floating?.UpdateRates(ethernetUp, ethernetDown, wlanUp, wlanDown, ethernetTotal, wlanTotal, state.EthernetMonthlyLimitBytes, ethernetMonth);
    }
    private void RememberFloatingBounds()
    {
        if (floating == null) return;
        state.FloatingX = floating.Left; state.FloatingY = floating.Top;
        state.FloatingWidth = floating.LogicalSize.Width; state.FloatingHeight = floating.LogicalSize.Height;
    }
    internal void HideToTray() { SaveLatestUsage(); Hide(); ShowInTaskbar = false; floating?.EnsureTopMost(); }
    internal void RestoreFromTray() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
    internal void ExitApplication() { quitting = true; Close(); }
    protected override void WndProc(ref Message m)
    {
        const int WM_QUERYENDSESSION = 0x0011, WM_ENDSESSION = 0x0016;
        if (m.Msg == WM_QUERYENDSESSION)
        {
            sessionEnding = true;
            SaveLatestUsage(); // Save before acknowledging shutdown, restart or logoff.
        }
        else if (m.Msg == WM_ENDSESSION && m.WParam == IntPtr.Zero)
            sessionEnding = false; // A cancelled shutdown must leave monitoring running.
        base.WndProc(ref m);
    }
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if ((!preview || persistPreview) && !quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        // FormClosing also runs for WM_QUERYENDSESSION, which can still be cancelled.
        // Only a confirmed FormClosed may tear down the monitor and floating window.
        if (e.CloseReason != CloseReason.WindowsShutDown) SaveLatestUsage();
    }
    private void OnClosed(object? sender, FormClosedEventArgs e)
    {
        quitting = true; timer.Stop();
        SaveLatestUsage(); floating?.Close();
        foreach (var capture in captures.Values) capture.Dispose();
        tray.Visible = false; tray.Dispose(); timer.Dispose();
        Theme.Changed -= ApplyTheme;
    }
    private void StartMonitoring()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
        {
            var view = AddAdapter(nic);
            try
            {
                var ip = nic.GetIPProperties().UnicastAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
                if (ip != null && nic.OperationalStatus == OperationalStatus.Up) captures[nic.Id] = new Capture(ip, domains);
                else view.Note = "网卡未连接，或没有 IPv4 地址。";
            }
            catch (Exception ex) { view.Note = "明细捕获不可用：" + ex.Message; }
        }
        foreach (var tabs in new[] { ethernetTabs, wlanTabs })
            if (tabs.Count == 0)
            {
                tabs.Add("暂无网卡", Theme.Label("尚未发现此类型网卡", 12, Theme.Muted));
            }
        RefreshData();
        Save(writeLog: true);
        timer.Start();
    }
    private AdapterView AddAdapter(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet) state.EthernetAdapterIds.Add(nic.Id);
        else state.EthernetAdapterIds.Remove(nic.Id);
        var view = new AdapterView(nic, nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "以太网" : "WLAN");
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet) view.SetQuota(state.EthernetMonthlyLimitBytes);
        view.AdapterToggleRequested += requested => { _ = ToggleAdapterAsync(requested); };
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            view.QuotaChanged += bytes =>
            {
                state.EthernetMonthlyLimitBytes = bytes;
                foreach (var wired in views.Values.Where(v => v.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)) wired.SetQuota(bytes);
                Save(writeLog: true); RefreshData();
            };
        views[nic.Id] = view; samplers[nic.Id] = new CounterSampler();
        (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? ethernetTabs : wlanTabs).Add(nic.Name, view);
        return view;
    }
    private void RefreshData()
    {
        var now = DateTime.Now;
        bool periodChanged = state.RollOver(now);
        Dictionary<FlowKey, int> owners;
        string? error = null;
        try { owners = ConnectionOwners.Read(); }
        catch (Exception ex) { owners = new(); error = "进程连接读取失败：" + ex.Message; }
        ethernetUp = ethernetDown = wlanUp = wlanDown = ethernetTotal = wlanTotal = 0;
        long ethernetMonth = state.EthernetMonth;
        var readFailures = new Dictionary<AdapterView, string>();
        foreach (var (id, view) in views)
        {
            var usage = state.GetUsage(id, now);
            try
            {
                var stats = view.Nic.GetIPStatistics();
                var sample = samplers[id].Sample(stats.BytesSent, stats.BytesReceived, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
                usage.Add(sample.up, sample.down, now);
                if (view.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                { ethernetUp += sample.upRate; ethernetDown += sample.downRate; }
                else { wlanUp += sample.upRate; wlanDown += sample.downRate; }
                view.UpdateView(sample.upRate, sample.downRate, captures.TryGetValue(id, out var cap) ? cap.Drain(owners) : new(), domains, usage, state.EthernetMonthlyLimitBytes, ethernetMonth);
            }
            catch (Exception ex)
            {
                samplers[id].Reset();
                view.UpdateUsage(usage, state.EthernetMonthlyLimitBytes, ethernetMonth);
                view.Note = "读取失败：" + ex.Message;
                readFailures[view] = "读取失败：" + ex.Message;
            }
            if (view.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet) ethernetTotal += usage.Total;
            else wlanTotal += usage.Total;
        }
        ethernetTotal = ethernetMonth = state.EthernetMonth;
        foreach (var view in views.Values.Where(v => v.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
            view.UpdateUsage(state.GetUsage(view.Nic.Id, now), state.EthernetMonthlyLimitBytes, ethernetMonth);
        foreach (var (view, message) in readFailures) view.Note = message;
        var scale = views.Values.Select(v => v.ChartPeak).DefaultIfEmpty(1024).Max();
        foreach (var view in views.Values) view.SetChartScale(scale);
        UpdateFloating();
        ticks++;
        if (periodChanged || ticks % 10 == 0) Save(periodChanged || ticks % 60 == 0);
        status.Text = saveError ?? store.EthernetLogWarning ?? store.LogWarning ?? error ?? store.Warning ?? "● 每秒更新   ·   本地日志累计   ·   软件 / IP 排名仅统计本次 IPv4 捕获";
    }
    private async Task ToggleAdapterAsync(AdapterView view)
    {
        bool disable = view.AdapterEnabled;
        if (disable && MessageBox.Show(this, $"确定禁用“{view.Nic.Name}”？此操作会立即断开该网卡的网络连接。", "确认禁用网卡", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        view.ToggleEnabled = false;
        status.Text = disable ? "正在禁用网卡…" : "正在启用网卡…";
        try
        {
            string name = view.Nic.Name.Replace("'", "''");
            string verb = disable ? "Disable" : "Enable";
            string script = $"$ErrorActionPreference='Stop'; {verb}-NetAdapter -Name '{name}' -Confirm:$false -ErrorAction Stop";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动网络适配器控制程序。");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            _ = await output;
            string detail = await error;
            if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? $"系统命令退出码：{process.ExitCode}" : detail.Trim());
            view.SetAdapterEnabled(!disable);
            status.Text = disable ? "网卡已禁用，点击“启用此网卡”即可恢复。" : "网卡已启用。";
            await Task.Delay(900); RefreshData();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "网卡操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { view.ToggleEnabled = true; }
    }
    private async Task OptimizeNetworkAsync()
    {
        optimizeButton.Enabled = false;
        status.Text = "正在优化网络设置…";
        try
        {
            const string script = "$ErrorActionPreference='Stop'; Clear-DnsClientCache -ErrorAction Stop; $p=Start-Process netsh.exe -ArgumentList @('int','tcp','set','global','autotuninglevel=normal') -NoNewWindow -Wait -PassThru; if($p.ExitCode -ne 0){throw ('TCP 自动调节设置失败，退出码 '+$p.ExitCode)}; Write-Output '已清除 DNS 缓存，并将 TCP 接收窗口自动调节恢复为 Windows 默认值（Normal）。没有更改校园网 DNS 或断开网卡。'";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动网络优化程序。");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var detail = await output; var failure = await error;
            if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(failure) ? $"系统命令退出码：{process.ExitCode}" : failure.Trim());
            MessageBox.Show(this, detail.Trim(), "网络优化完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "网络优化失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { optimizeButton.Enabled = true; }
    }
    internal FloatingForm? Floating => floating;
    internal void SetFloating(bool enabled) => floatingToggle.Checked = enabled;
    internal void AddPreview(NetworkInterface nic, bool wireless)
    {
        var view = new AdapterView(nic, wireless ? "WLAN" : "以太网");
        (wireless ? wlanTabs : ethernetTabs).Add(wireless ? "校园网 WLAN" : "以太网", view);
        var sample = new CaptureSnapshot();
        sample.Rows.Add(new FlowRow(Environment.ProcessId, "203.0.113.10", 345678, 4567890));
        for (int i = 0; i < 120; i++) view.UpdateView(20000 + i * 900, 800000 + (long)(Math.Sin(i / 7d) * 600000), sample, domains, new Usage { Upload = 45678120, Download = 4567891234, TodayDownload = 234567890 }, 12L * 1024 * 1024 * 1024, 4L * 1024 * 1024 * 1024);
        view.Note = "界面预览示例数据";
    }
}
