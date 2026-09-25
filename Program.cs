using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NetworkMonitor;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly Dictionary<string, AdapterView> views = new();
    private readonly TableLayoutPanel comparison = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(6) };
    private readonly TabControl ethernetTabs = new() { Dock = DockStyle.Fill };
    private readonly TabControl wlanTabs = new() { Dock = DockStyle.Fill };
    private readonly CheckBox floatingToggle = new() { Appearance = Appearance.Button, Text = "打开流量浮窗", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
    private FloatingForm? floating;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 32, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Dictionary<string, Capture> captures = new();
    private readonly Dictionary<string, (long sent, long received)> previous = new();
    private readonly ConcurrentDictionary<string, string> domains = new();
    private Dictionary<FlowKey, int> owners = new();

    public MainForm()
    {
        Text = "网络流量监控 · 以太网 / WLAN";
        Width = 1500;
        Height = 780;
        MinimumSize = new Size(1050, 600);
        comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        comparison.Controls.Add(Section("以太网", ethernetTabs), 0, 0);
        comparison.Controls.Add(Section("WLAN", wlanTabs), 1, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 5, 0, 0) };
        toolbar.Controls.Add(floatingToggle);
        floatingToggle.CheckedChanged += (_, _) => ToggleFloating();
        Controls.Add(comparison);
        Controls.Add(toolbar);
        Controls.Add(status);
        status.Text = "正在发现网卡…";
        Load += (_, _) => Start();
        FormClosing += (_, _) => { timer.Stop(); floating?.Dispose(); foreach (var c in captures.Values) c.Dispose(); };
        timer.Tick += (_, _) => RefreshData();
    }

    private static Control Section(string title, Control content)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold) };
        box.Controls.Add(content);
        return box;
    }

    private void ToggleFloating()
    {
        if (floatingToggle.Checked)
        {
            floating = new FloatingForm();
            floating.FormClosed += (_, _) => { floating = null; floatingToggle.Checked = false; };
            floatingToggle.Text = "关闭流量浮窗";
            floating.Show(this);
        }
        else
        {
            floatingToggle.Text = "打开流量浮窗";
            floating?.Close();
        }
    }

    private void Start()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1).ThenBy(n => n.Name).ToList();
        foreach (var nic in adapters)
        {
            var label = nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "以太网" : "WLAN";
            var view = new AdapterView(nic, label);
            views[nic.Id] = view;
            (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? ethernetTabs : wlanTabs)
                .TabPages.Add(new TabPage(nic.Name) { Controls = { view } });
            try
            {
                var ip = nic.GetIPProperties().UnicastAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
                if (ip != null && nic.OperationalStatus == OperationalStatus.Up)
                    captures[nic.Id] = new Capture(ip, domains);
            }
            catch (Exception ex) { view.Note = "抓包不可用：" + ex.Message; }
        }
        if (ethernetTabs.TabPages.Count == 0) ethernetTabs.TabPages.Add("未找到以太网网卡");
        if (wlanTabs.TabPages.Count == 0) wlanTabs.TabPages.Add("未找到 WLAN 网卡");
        if (adapters.Count == 0) status.Text = "未找到以太网或 WLAN 网卡。";
        timer.Start();
        RefreshData();
    }

    private void RefreshData()
    {
        try { owners = ConnectionOwners.Read(); }
        catch (Exception ex) { status.Text = "无法读取进程连接：" + ex.Message; }
        long ethernetUp = 0, ethernetDown = 0, wlanUp = 0, wlanDown = 0;
        foreach (var (id, view) in views)
        {
            try
            {
                var stats = view.Nic.GetIPv4Statistics();
                var now = (stats.BytesSent, stats.BytesReceived);
                if (!previous.TryGetValue(id, out var prior)) prior = now;
                previous[id] = now;
                var up = Math.Max(0, now.BytesSent - prior.sent);
                var down = Math.Max(0, now.BytesReceived - prior.received);
                if (view.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                { ethernetUp += up; ethernetDown += down; }
                else { wlanUp += up; wlanDown += down; }
                CaptureSnapshot snapshot = captures.TryGetValue(id, out var cap) ? cap.Drain(owners) : new();
                view.UpdateView(up, down, snapshot, domains);
            }
            catch (Exception ex) { view.Note = "读取网卡失败：" + ex.Message; }
        }
        floating?.UpdateRates(ethernetUp, ethernetDown, wlanUp, wlanDown);
        if (views.Count > 0) status.Text = "每秒更新 · 图表为网卡总流量；软件与域名统计为本机 IPv4 抓包流量 · HTTPS 只显示域名，不显示路径";
    }
}

internal sealed class AdapterView : UserControl
{
    public NetworkInterface Nic { get; }
    private readonly Label title = new() { Dock = DockStyle.Top, Height = 60, Font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold) };
    private readonly Label note = new() { Dock = DockStyle.Top, Height = 42 };
    private readonly Chart chart = new() { Dock = DockStyle.Top, Height = 175 };
    private readonly ListView apps = MakeList("软件", "上传", "下载", "总计");
    private readonly ListView hosts = MakeList("域名 / 远端 IP", "软件", "上传", "下载", "总计");
    private readonly Dictionary<string, Traffic> appTotals = new();
    private readonly Dictionary<(string host, string app), Traffic> hostTotals = new();
    public string Note { set => note.Text = value; }

    public AdapterView(NetworkInterface nic, string kind)
    {
        Nic = nic;
        Dock = DockStyle.Fill;
        title.Text = $"{kind} · {nic.Name}";
        note.Text = "统计从软件启动时开始。软件和域名流量需要管理员权限及有效的 IPv4 地址。";
        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        lists.Controls.Add(Group("按软件统计", apps), 0, 0);
        lists.Controls.Add(Group("按域名 / 远端 IP 统计", hosts), 0, 1);
        Controls.Add(lists);
        Controls.Add(chart);
        Controls.Add(note);
        Controls.Add(title);
    }

    private static Control Group(string name, Control child)
    {
        var box = new GroupBox { Text = name, Dock = DockStyle.Fill, Padding = new Padding(8) };
        box.Controls.Add(child);
        return box;
    }

    private static ListView MakeList(params string[] columns)
    {
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        foreach (var name in columns) list.Columns.Add(name, name == columns[0] ? 185 : 92);
        return list;
    }

    public void UpdateView(long up, long down, CaptureSnapshot snap, ConcurrentDictionary<string, string> domains)
    {
        title.Text = $"{(Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "以太网" : "WLAN")} · {Nic.Name}    ↑ {Fmt.Rate(up)}    ↓ {Fmt.Rate(down)}";
        chart.Add(up, down);
        foreach (var row in snap.Rows)
        {
            var app = row.Pid == 0 ? "未归属进程" : ProcessName(row.Pid);
            Add(appTotals, app, row.Upload, row.Download);
            var host = domains.TryGetValue(row.RemoteIp, out var domain) ? domain : row.RemoteIp;
            Add(hostTotals, (host, app), row.Upload, row.Download);
        }
        Fill(apps, appTotals.OrderByDescending(x => x.Value.Total).Take(100)
            .Select(x => new[] { x.Key, Fmt.Bytes(x.Value.Up), Fmt.Bytes(x.Value.Down), Fmt.Bytes(x.Value.Total) }));
        Fill(hosts, hostTotals.OrderByDescending(x => x.Value.Total).Take(150)
            .Select(x => new[] { x.Key.host, x.Key.app, Fmt.Bytes(x.Value.Up), Fmt.Bytes(x.Value.Down), Fmt.Bytes(x.Value.Total) }));
    }

    private static string ProcessName(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return $"{p.ProcessName} ({pid})"; }
        catch { return $"已退出进程 ({pid})"; }
    }
    private static void Add<TKey>(Dictionary<TKey, Traffic> map, TKey key, long up, long down) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var value)) map[key] = value = new Traffic();
        value.Up += up; value.Down += down;
    }
    private static void Fill(ListView list, IEnumerable<string[]> rows)
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var row in rows) list.Items.Add(new ListViewItem(row));
        list.EndUpdate();
    }
}

internal sealed class Traffic { public long Up; public long Down; public long Total => Up + Down; }
internal static class Fmt
{
    public static string Bytes(long n) => n < 1024 ? $"{n} B" : n < 1048576 ? $"{n / 1024d:F1} KB" : n < 1073741824 ? $"{n / 1048576d:F1} MB" : $"{n / 1073741824d:F2} GB";
    public static string Rate(long n) => Bytes(n) + "/s";
}

internal sealed class Chart : Control
{
    private readonly Queue<(long up, long down)> points = new();
    public Chart() { DoubleBuffered = true; BackColor = Color.FromArgb(22, 29, 42); ForeColor = Color.White; }
    public void Add(long up, long down) { points.Enqueue((up, down)); while (points.Count > 120) points.Dequeue(); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var data = points.ToArray();
        using var grid = new Pen(Color.FromArgb(55, 65, 80));
        for (var i = 1; i <= 4; i++) { var y = i * Height / 5f; g.DrawLine(grid, 0, y, Width, y); }
        var max = Math.Max(1024, data.Select(p => Math.Max(p.up, p.down)).DefaultIfEmpty().Max());
        Draw(data.Select(p => p.down).ToArray(), Color.DeepSkyBlue);
        Draw(data.Select(p => p.up).ToArray(), Color.Orange);
        using var font = new Font("Microsoft YaHei UI", 9);
        g.DrawString($"↓ 下载  ↑ 上传     峰值 {Fmt.Rate(max)}     最近 120 秒", font, Brushes.White, 12, 8);
        void Draw(long[] values, Color color)
        {
            if (values.Length < 2) return;
            using var pen = new Pen(color, 2.5f);
            var pts = values.Select((v, i) => new PointF(Width - (values.Length - 1 - i) * Math.Max(1, Width / 119f), Height - 15 - (Height - 42) * v / max)).ToArray();
            g.DrawLines(pen, pts);
        }
    }
}
