using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace NetworkMonitor;

internal sealed class AdapterView : UserControl
{
    public NetworkInterface Nic { get; }
    private readonly Label note = Theme.Label("", 8, Theme.Muted);
    private readonly Label download = Theme.Label("0 B/s", 17, Theme.Blue, true);
    private readonly Label upload = Theme.Label("0 B/s", 17, Theme.Teal, true);
    private readonly Label total = Theme.Label("0 B", 17, bold: true);
    private readonly Label today = Theme.Label("0 B", 17, bold: true);
    private readonly Chart chart = new() { Dock = DockStyle.Top, Height = 142 };
    private readonly ListView apps = MakeList("软件", "总流量");
    private readonly ListView ips = MakeList("远端 IP", "总流量");
    private readonly ImageList appIcons = new() { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
    private readonly Dictionary<string, Traffic> appTotals = new();
    private readonly Dictionary<string, Traffic> ipTotals = new();
    private readonly Dictionary<string, string> appNames = new();
    private readonly Dictionary<string, string?> appPaths = new();
    private readonly Dictionary<string, string> appIconKeys = new();
    private readonly Button adapterToggle = Theme.Button("…");
    private readonly Panel quotaPanel = new() { Dock = DockStyle.Top, Height = 66, Visible = false, BackColor = Theme.Surface };
    private readonly QuotaMeter quotaMeter = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown quotaInput = new() { DecimalPlaces = 2, Increment = 5, Minimum = 0, Maximum = 100000, Width = 110, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text };
    private readonly Button quotaApply = Theme.Button("设置限额");
    private bool adapterEnabled;
    public event Action<AdapterView>? AdapterToggleRequested;
    public event Action<long>? QuotaChanged;
    public bool ToggleEnabled { set => adapterToggle.Enabled = value; }
    public bool AdapterEnabled => adapterEnabled;
    public void SetAdapterEnabled(bool enabled)
    {
        adapterEnabled = enabled;
        adapterToggle.Text = enabled ? "禁用此网卡" : "启用此网卡";
    }
    public string Note { set => note.Text = value; }
    public long ChartPeak => chart.Peak;
    public void SetChartScale(long maximum) { chart.SharedMaximum = maximum; chart.Invalidate(); }

    public AdapterView(NetworkInterface nic, string kind)
    {
        Nic = nic;
        adapterEnabled = nic.OperationalStatus == OperationalStatus.Up;
        Dock = DockStyle.Fill;
        BackColor = Theme.Surface; ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 9);
        note.Dock = DockStyle.Top; note.Height = 22;
        note.Text = "域名来自 DNS；HTTPS 路径不可见。";
        var metrics = new TableLayoutPanel { Dock = DockStyle.Top, Height = 104, ColumnCount = 2, RowCount = 4, BackColor = Theme.Surface };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 4; i++) metrics.RowStyles.Add(new RowStyle(SizeType.Percent, i % 2 == 0 ? 18 : 32));
        metrics.Controls.Add(Theme.Label("↓ 下载速率", 9, Theme.Muted), 0, 0); metrics.Controls.Add(Theme.Label("↑ 上传速率", 9, Theme.Muted), 1, 0);
        metrics.Controls.Add(download, 0, 1); metrics.Controls.Add(upload, 1, 1);
        metrics.Controls.Add(Theme.Label("今日已用", 9, Theme.Muted), 0, 2); metrics.Controls.Add(Theme.Label("本月累计 · 上传 + 下载", 9, Theme.Muted), 1, 2);
        metrics.Controls.Add(today, 0, 3); metrics.Controls.Add(total, 1, 3);
        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 6, 0, 0), BackColor = Theme.Surface };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57)); lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        apps.SmallImageList = appIcons;
        lists.Controls.Add(Group("软件流量 · 前三名", apps), 0, 0);
        lists.Controls.Add(Group("IP 流量 · 前三名", ips), 1, 0);
        adapterToggle.Click += (_, _) => AdapterToggleRequested?.Invoke(this);
        quotaApply.Click += (_, _) => QuotaChanged?.Invoke((long)(quotaInput.Value * 1024m * 1024m * 1024m));
        var adapterBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, Padding = new Padding(0, 2, 0, 0), BackColor = Theme.Surface };
        var adapterLabel = Theme.Label("网卡开关", 9, Theme.Muted); adapterLabel.Width = 85; adapterBar.Controls.Add(adapterLabel); adapterBar.Controls.Add(adapterToggle);
        var quotaLayout = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false, Padding = new Padding(0, 2, 0, 0), BackColor = Theme.Surface };
        var quotaLabel = Theme.Label("月限额 (GiB)", 9, Theme.Muted); quotaLabel.Width = 95; quotaLayout.Controls.Add(quotaLabel); quotaLayout.Controls.Add(quotaInput); quotaLayout.Controls.Add(quotaApply);
        quotaPanel.Controls.Add(quotaMeter); quotaPanel.Controls.Add(quotaLayout);
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet) quotaPanel.Visible = true;
        Controls.Add(lists);
        Controls.Add(chart);
        Controls.Add(note);
        Controls.Add(quotaPanel);
        Controls.Add(adapterBar);
        Controls.Add(metrics);
    }

    private static Control Group(string name, Control child)
    {
        var box = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 3), BackColor = Theme.Surface };
        var heading = Theme.Label(name + "  ·  本次运行", 9, Theme.Muted); heading.Dock = DockStyle.Top; heading.Height = 26;
        box.Controls.Add(child); box.Controls.Add(heading);
        return box;
    }

    private static ListView MakeList(params string[] columns)
    {
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, OwnerDraw = true };
        foreach (var name in columns) list.Columns.Add(name, name == columns[0] ? 150 : 64);
        list.SizeChanged += (_, _) =>
        {
            if (list.Columns.Count == 0) return;
            list.Columns[0].Width = Math.Max(80, list.ClientSize.Width - 88);
            if (list.Columns.Count > 1) list.Columns[1].Width = 84;
        };
        list.DrawColumnHeader += (_, e) =>
        {
            using var brush = new SolidBrush(Theme.Border); e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, list.Font, e.Bounds, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        list.DrawItem += (_, e) => { };
        list.DrawSubItem += (_, e) =>
        {
            if (e.Item is null || e.SubItem is null) return;
            var bg = e.Item.Selected ? Theme.Border : (e.ItemIndex % 2 == 0 ? Theme.Surface : Color.FromArgb(Math.Max(0, Theme.Surface.R - 5), Math.Max(0, Theme.Surface.G - 5), Math.Max(0, Theme.Surface.B - 5)));
            using var brush = new SolidBrush(bg); e.Graphics.FillRectangle(brush, e.Bounds);
            int left = e.Bounds.Left + 6;
            if (e.ColumnIndex == 0 && list.SmallImageList is not null && e.Item.ImageIndex >= 0)
            {
                var image = list.SmallImageList.Images[e.Item.ImageIndex];
                e.Graphics.DrawImage(image, left, e.Bounds.Top + (e.Bounds.Height - 20) / 2, 20, 20); left += 25;
            }
            var rect = Rectangle.FromLTRB(left, e.Bounds.Top, e.Bounds.Right - 4, e.Bounds.Bottom);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, rect, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (e.ColumnIndex == 0 ? TextFormatFlags.Left : TextFormatFlags.Right));
        };
        return list;
    }

    public void SetQuota(long bytes) => quotaInput.Value = Math.Min(quotaInput.Maximum, Math.Max(quotaInput.Minimum, (decimal)bytes / 1024m / 1024m / 1024m));
    public void UpdateView(long up, long down, CaptureSnapshot snap, ConcurrentDictionary<string, string> domains, Usage usage, long ethernetMonthlyLimit, long ethernetMonth)
    {
        download.Text = Fmt.Rate(down); upload.Text = Fmt.Rate(up);
        UpdateUsage(usage, ethernetMonthlyLimit, ethernetMonth);
        // Keep the state changed by our own operation; NetworkInterface instances are snapshots
        // and may continue reporting the old status after Windows disables the adapter.
        adapterToggle.Text = adapterEnabled ? "禁用此网卡" : "启用此网卡";
        chart.Add(up, down);
        foreach (var row in snap.Rows)
        {
            var (key, name, path) = AppIdentity(row.Pid);
            appNames[key] = name;
            if (!appPaths.ContainsKey(key)) appPaths[key] = path;
            Add(appTotals, key, row.Upload, row.Download);
            Add(ipTotals, row.RemoteIp, row.Upload, row.Download);
        }
        var topApps = appTotals.OrderByDescending(x => x.Value.Total).Take(3).ToList();
        foreach (var x in topApps) EnsureAppIcon(x.Key);
        FillApps(topApps);
        Fill(ips, ipTotals.OrderByDescending(x => x.Value.Total).Take(3)
            .Select(x => new[] { x.Key, Fmt.Bytes(x.Value.Total) }));
    }
    public void UpdateUsage(Usage usage, long ethernetMonthlyLimit, long ethernetMonth)
    {
        total.Text = Fmt.Bytes(usage.Total); today.Text = Fmt.Bytes(usage.Today);
        quotaPanel.Visible = Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet;
        if (Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            note.Text = ethernetMonthlyLimit <= 0 ? $"本月已用 {Fmt.Bytes(ethernetMonth)} · 未设置月限额" : $"本月已用 {Fmt.Bytes(ethernetMonth)} · 剩余 {Fmt.Bytes(Math.Max(0, ethernetMonthlyLimit - ethernetMonth))}";
        quotaMeter.SetUsage(ethernetMonthlyLimit, ethernetMonth);
    }

    private static (string key, string name, string? path) AppIdentity(int pid)
    {
        if (pid <= 0) return ("unassigned", "未归属进程", null);
        try
        {
            using var p = Process.GetProcessById(pid);
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { }
            string name = path == null ? p.ProcessName : Path.GetFileNameWithoutExtension(path);
            return (path?.ToLowerInvariant() ?? name.ToLowerInvariant(), name, path);
        }
        catch { return ($"exited-{pid}", $"已退出进程 ({pid})", null); }
    }
    private void EnsureAppIcon(string key)
    {
        if (appIconKeys.ContainsKey(key)) return;
        string iconKey = "default";
        if (appPaths.TryGetValue(key, out string? path) && path != null)
        {
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null) { iconKey = key; appIcons.Images.Add(iconKey, icon.ToBitmap()); }
            }
            catch { }
        }
        if (appIcons.Images.Count == 0) appIcons.Images.Add("default", SystemIcons.Application.ToBitmap());
        appIconKeys[key] = appIcons.Images.ContainsKey(iconKey) ? iconKey : "default";
    }
    private void FillApps(List<KeyValuePair<string, Traffic>> rows)
    {
        apps.BeginUpdate(); apps.Items.Clear();
        foreach (var x in rows)
        {
            var item = new ListViewItem(appNames[x.Key], appIconKeys[x.Key]);
            item.SubItems.Add(Fmt.Bytes(x.Value.Total));
            apps.Items.Add(item);
        }
        apps.EndUpdate();
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
        foreach (var row in rows)
        {
            list.Items.Add(new ListViewItem(row));
        }
        list.EndUpdate();
    }
}
