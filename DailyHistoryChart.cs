using System.Drawing.Drawing2D;

namespace NetworkMonitor;

internal sealed class DailyHistoryChart : Control
{
    private readonly ToolTip tooltip = new();
    private List<DailyPoint> points = new();
    private int hovered = -1;
    internal bool Bars { get; set; } = true;
    internal bool ShowWlan { get; set; } = true;
    internal IReadOnlyList<DailyPoint> Points => points;
    public DailyHistoryChart()
    {
        DoubleBuffered = true; BackColor = Theme.Surface; Dock = DockStyle.Fill;
        MouseMove += (_, e) =>
        {
            var plot = Plot();
            int index = plot.Contains(e.Location) && points.Count > 0 ? Math.Clamp((e.X - plot.Left) * points.Count / plot.Width, 0, points.Count - 1) : -1;
            if (index == hovered) return;
            hovered = index;
            if (index < 0) { tooltip.SetToolTip(this, ""); return; }
            var point = points[index];
            tooltip.SetToolTip(this, $"{point.Date:yyyy-MM-dd}\n以太网：↑ {Fmt.Bytes(point.EthernetUpload)}  ↓ {Fmt.Bytes(point.EthernetDownload)}  合计 {Fmt.Bytes(point.Ethernet)}"
                + (ShowWlan ? $"\nWLAN：↑ {Fmt.Bytes(point.WlanUpload)}  ↓ {Fmt.Bytes(point.WlanDownload)}  合计 {Fmt.Bytes(point.Wlan)}" : ""));
        };
        MouseLeave += (_, _) => { hovered = -1; tooltip.SetToolTip(this, ""); };
    }
    internal void UpdatePoints(List<DailyPoint> daily) { points = daily; hovered = -1; Invalidate(); }
    private Rectangle Plot()
    {
        int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        return new Rectangle(Scale(85), Scale(48), Math.Max(1, Width - Scale(110)), Math.Max(1, Height - Scale(92)));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 160 || Height < 120 || points.Count == 0) return;
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var plot = Plot(); float scale = DeviceDpi / 96f;
        using var font = new Font(Theme.FloatingFontFamily, 9);
        using var bold = new Font(Theme.FloatingFontFamily, 10, FontStyle.Bold);
        using var grid = new Pen(Theme.Border) { DashStyle = DashStyle.Dot };
        using var ethernet = new SolidBrush(Theme.Blue); using var wlan = new SolidBrush(Theme.Teal);
        int padding = (int)(10 * scale);
        TextRenderer.DrawText(g, "● 以太网", bold, new Point(plot.Left, padding), Theme.Blue);
        if (ShowWlan) TextRenderer.DrawText(g, "● WLAN", bold, new Point(plot.Left + (int)(115 * scale), padding), Theme.Teal);
        long maximum = Math.Max(1024, points.Max(point => Math.Max(point.Ethernet, ShowWlan ? point.Wlan : 0)));
        for (int i = 0; i <= 4; i++)
        {
            int y = plot.Bottom - plot.Height * i / 4;
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            TextRenderer.DrawText(g, Fmt.Bytes((long)(maximum * (i / 4d))), font,
                new Rectangle(0, y - padding, plot.Left - padding, padding * 2), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        float step = plot.Width / (float)points.Count;
        float barWidth = Math.Max(1, step * (ShowWlan ? 0.34f : 0.58f));
        float Y(long bytes) => plot.Bottom - plot.Height * (bytes / (float)maximum);
        var ethernetLine = new List<PointF>(); var wlanLine = new List<PointF>();
        int labelEvery = points.Count > 7 ? 7 : 1;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i]; float x = plot.Left + (i + 0.5f) * step;
            if (Bars)
            {
                float left = x - (ShowWlan ? barWidth : barWidth / 2);
                if (point.Ethernet > 0) g.FillRectangle(ethernet, left, Y(point.Ethernet), barWidth, Math.Max(1, plot.Bottom - Y(point.Ethernet)));
                if (ShowWlan && point.Wlan > 0) g.FillRectangle(wlan, x, Y(point.Wlan), barWidth, Math.Max(1, plot.Bottom - Y(point.Wlan)));
            }
            ethernetLine.Add(new PointF(x, Y(point.Ethernet))); wlanLine.Add(new PointF(x, Y(point.Wlan)));
            if ((i % labelEvery == 0 && (points.Count <= 7 || i < points.Count - 3)) || i == points.Count - 1)
                TextRenderer.DrawText(g, point.Date.ToString("MM/dd", System.Globalization.CultureInfo.InvariantCulture), font,
                    new Rectangle(Math.Clamp((int)(x - 26 * scale), plot.Left, Math.Max(plot.Left, plot.Right - (int)(52 * scale))), plot.Bottom + padding, (int)(52 * scale), padding * 2), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        if (!Bars && points.Count > 1)
        {
            using var ethernetPen = new Pen(Theme.Blue, 2 * scale); using var wlanPen = new Pen(Theme.Teal, 2 * scale);
            g.DrawLines(ethernetPen, ethernetLine.ToArray()); if (ShowWlan) g.DrawLines(wlanPen, wlanLine.ToArray());
            foreach (var point in ethernetLine) g.FillEllipse(ethernet, point.X - 2 * scale, point.Y - 2 * scale, 4 * scale, 4 * scale);
            if (ShowWlan) foreach (var point in wlanLine) g.FillEllipse(wlan, point.X - 2 * scale, point.Y - 2 * scale, 4 * scale, 4 * scale);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) tooltip.Dispose(); base.Dispose(disposing); }
}
