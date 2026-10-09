using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace NetworkMonitor;

internal sealed class FloatingForm : Form
{
    private readonly RateComparison display = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer pin = new() { Interval = 1500 };
    private Point dragOrigin, windowOrigin;
    private Size sizeOrigin;
    private bool dragging, resizing;
    public event Action? BoundsCommitted;
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    public FloatingForm()
    {
        Text = "实时流量 · 始终置顶";
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(430, 338);
        MinimumSize = new Size(240, 144);
        ControlBox = false;
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 800);
        Location = new Point(area.Right - Width - 20, area.Top + 60);
        TopMost = true; ShowInTaskbar = false; BackColor = Theme.Background;
        SizeChanged += (_, _) => { ApplyRoundedShape(); display.Invalidate(); };
        Controls.Add(display);
        ApplyRoundedShape();
        AttachGestures(this);
        AttachGestures(display);
        pin.Tick += (_, _) => EnsureTopMost();
        Shown += (_, _) => { EnsureTopMost(); pin.Start(); };
        FormClosed += (_, _) => { pin.Stop(); pin.Dispose(); };
        ApplyTheme();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindow(this, floating: true);
    }
    private void ApplyRoundedShape()
    {
        if (Width <= 0 || Height <= 0) return;
        float diameter = Math.Min(Math.Min(Width, Height), 36 * DeviceDpi / 96f);
        using var path = new GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(Width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(Width - diameter, Height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, Height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }
    private void AttachGestures(Control surface)
    {
        surface.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || e.Clicks != 1) return;
            dragOrigin = surface.PointToScreen(e.Location); windowOrigin = Location;
            sizeOrigin = Size;
            resizing = IsResizeGrip(PointToClient(dragOrigin)); dragging = !resizing;
            surface.Cursor = resizing ? Cursors.SizeNWSE : Cursors.SizeAll;
            surface.Capture = true;
        };
        surface.MouseMove += (_, e) =>
        {
            var current = surface.PointToScreen(e.Location);
            if (!dragging && !resizing)
            { surface.Cursor = IsResizeGrip(PointToClient(current)) ? Cursors.SizeNWSE : Cursors.Default; return; }
            if (e.Button != MouseButtons.Left) return;
            int dx = current.X - dragOrigin.X, dy = current.Y - dragOrigin.Y;
            if (resizing)
            {
                var area = Screen.FromControl(this).WorkingArea;
                Size = new Size(Math.Clamp(sizeOrigin.Width + dx, MinimumSize.Width, Math.Max(MinimumSize.Width, area.Right - Left)),
                    Math.Clamp(sizeOrigin.Height + dy, MinimumSize.Height, Math.Max(MinimumSize.Height, area.Bottom - Top)));
            }
            else Location = new Point(windowOrigin.X + dx, windowOrigin.Y + dy);
        };
        surface.MouseUp += (_, _) =>
        {
            bool changed = dragging || resizing;
            dragging = resizing = false; surface.Capture = false;
            surface.Cursor = Cursors.Default;
            if (changed) BoundsCommitted?.Invoke();
        };
        surface.MouseCaptureChanged += (_, _) => { if (!surface.Capture) dragging = resizing = false; };
        surface.MouseDoubleClick += (_, e) =>
        { if (e.Button == MouseButtons.Left && !IsResizeGrip(PointToClient(surface.PointToScreen(e.Location)))) Close(); };
    }
    private bool IsResizeGrip(Point point)
    {
        int edge = (int)Math.Round(30 * DeviceDpi / 96d);
        return point.X >= ClientSize.Width - edge && point.Y >= ClientSize.Height - edge;
    }
    public Size LogicalSize => new((int)Math.Round(ClientSize.Width * 96d / DeviceDpi), (int)Math.Round(ClientSize.Height * 96d / DeviceDpi));
    public void RestoreSize(Size logicalSize)
    {
        ClientSize = new Size((int)Math.Round(Math.Clamp(logicalSize.Width, 240, 1600) * DeviceDpi / 96d),
            (int)Math.Round(Math.Clamp(logicalSize.Height, 144, 1200) * DeviceDpi / 96d));
    }
    public void EnsureTopMost()
    {
        if (IsHandleCreated && Visible) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
    }
    public void ApplyTheme()
    {
        BackColor = Theme.Background; Theme.ApplyTree(display);
        Theme.ApplyWindow(this, floating: true); Invalidate(true);
    }
    public void UpdateRates(long eu, long ed, long wu, long wd, long et, long wt, long limit, long monthUsed) => display.UpdateRates(eu, ed, wu, wd, et, wt, limit, monthUsed);
}

internal sealed class RateComparison : Control
{
    internal enum LayoutDensity { Full, Compact, Minimal }
    internal LayoutDensity Density => Width * 96d / DeviceDpi >= 360 && Height * 96d / DeviceDpi >= 300 ? LayoutDensity.Full
        : Width * 96d / DeviceDpi >= 300 && Height * 96d / DeviceDpi >= 210 ? LayoutDensity.Compact : LayoutDensity.Minimal;
    private long ethernetUp, ethernetDown, wlanUp, wlanDown, ethernetTotal, wlanTotal, ethernetLimit, ethernetMonth;
    public RateComparison()
    {
        DoubleBuffered = true; BackColor = Theme.Background;
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
    }
    public void UpdateRates(long eu, long ed, long wu, long wd, long et, long wt, long limit, long monthUsed)
    {
        ethernetUp = eu; ethernetDown = ed; wlanUp = wu; wlanDown = wd; ethernetTotal = et; wlanTotal = wt;
        ethernetLimit = limit; ethernetMonth = monthUsed; Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var saved = g.Save();
        float scale = DeviceDpi / 96f;
        g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        int width = (int)(Width / scale), height = (int)(Height / scale);
        var density = Density;
        using var heading = new Font(Theme.FontFamily, 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var normal = new Font(Theme.FontFamily, 12, FontStyle.Regular, GraphicsUnit.Pixel);
        using var large = new Font(Theme.FontFamily, density == LayoutDensity.Full ? 22 : density == LayoutDensity.Compact ? 17 : 14, FontStyle.Bold, GraphicsUnit.Pixel);
        using var track = new SolidBrush(Theme.Border);
        using var surface = new SolidBrush(Theme.Surface);
        using var textFormat = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        var maximum = Math.Max(1024, Math.Max(ethernetUp + ethernetDown, wlanUp + wlanDown));
        int quotaHeight = density == LayoutDensity.Full ? 80 : density == LayoutDensity.Compact ? 34 : 0;
        int rowHeight = (height - 40 - (quotaHeight > 0 ? 20 + quotaHeight : 10)) / 2;
        DrawRow("以太网", ethernetUp, ethernetDown, ethernetTotal, Theme.Blue, 10);
        DrawRow("WLAN", wlanUp, wlanDown, wlanTotal, Theme.Teal, 20 + rowHeight);
        if (quotaHeight > 0) DrawEthernetQuota(30 + rowHeight * 2);
        using (var grip = new Pen(Theme.Muted, 1.4f))
        {
            for (int i = 0; i < 3; i++) g.DrawLine(grip, width - 20 + i * 4, height - 8, width - 8, height - 20 + i * 4);
        }
        g.Restore(saved);
        void DrawText(string text, Font font, Color color, Rectangle rect, bool right = false)
        {
            using var brush = new SolidBrush(color);
            textFormat.Alignment = right ? StringAlignment.Far : StringAlignment.Near;
            g.DrawString(text, font, brush, rect, textFormat);
        }
        void DrawRow(string name, long up, long down, long total, Color accent, int y)
        {
            using var path = Theme.RoundedPath(new Rectangle(10, y, width - 20, rowHeight), 12);
            g.FillPath(surface, path);
            DrawText(name, heading, accent, new Rectangle(22, y + 5, 72, 20));
            if (density != LayoutDensity.Minimal) DrawText("本月累计 " + Fmt.Bytes(total), normal, Theme.Muted, new Rectangle(102, y + 5, width - 124, 20), right: true);
            int column = (width - 44) / 2;
            int speedY = y + (density == LayoutDensity.Full ? Math.Max(30, rowHeight / 2 - 10) : 24);
            DrawText("↓ " + Fmt.Rate(down), large, Theme.Text, new Rectangle(22, speedY, column - 4, density == LayoutDensity.Full ? 30 : 22));
            DrawText("↑ " + Fmt.Rate(up), density == LayoutDensity.Full ? normal : large, Theme.Muted, new Rectangle(22 + column, speedY, column, density == LayoutDensity.Full ? 30 : 22));
            if (density != LayoutDensity.Full) return;
            using var barPath = Theme.RoundedPath(new Rectangle(24, y + rowHeight - 18, width - 48, 5), 2);
            g.FillPath(track, barPath);
            using var fill = new SolidBrush(accent);
            using var fillPath = Theme.RoundedPath(new Rectangle(24, y + rowHeight - 18, Math.Max(4, (int)((width - 48) * ((up + down) / (double)maximum))), 5), 2);
            g.FillPath(fill, fillPath);
        }
        void DrawEthernetQuota(int y)
        {
            using var path = Theme.RoundedPath(new Rectangle(10, y, width - 20, quotaHeight), 12);
            g.FillPath(surface, path);
            string text = ethernetLimit <= 0 ? $"以太网月额度未设置 · 已用 {Fmt.Bytes(ethernetMonth)}" : $"以太网剩余 {Fmt.Bytes(Math.Max(0, ethernetLimit - ethernetMonth))} / {Fmt.Bytes(ethernetLimit)}";
            DrawText(text, normal, Theme.Gold, new Rectangle(22, y + (density == LayoutDensity.Full ? 10 : 5), width - 44, 24));
            if (density != LayoutDensity.Full) return;
            int barWidth = width - 44;
            using var trackPath = Theme.RoundedPath(new Rectangle(22, y + 47, barWidth, 10), 5);
            g.FillPath(track, trackPath);
            if (ethernetLimit > 0)
            {
                var remaining = Math.Max(0, ethernetLimit - ethernetMonth);
                int remainingWidth = (int)(barWidth * Math.Min(1, remaining / (double)ethernetLimit));
                if (remainingWidth > 0)
                {
                    using var remainingPath = Theme.RoundedPath(new Rectangle(22, y + 47, Math.Max(10, remainingWidth), 10), 5);
                    using var gradient = new LinearGradientBrush(new Rectangle(22, y + 47, barWidth, 10), Theme.Teal, Theme.Gold, 0f);
                    g.FillPath(gradient, remainingPath);
                }
            }
        }
    }
}
