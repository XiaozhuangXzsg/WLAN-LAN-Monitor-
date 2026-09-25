using System.Drawing.Drawing2D;

namespace NetworkMonitor;

internal sealed class FloatingForm : Form
{
    private readonly RateComparison display = new() { Dock = DockStyle.Fill };

    public FloatingForm()
    {
        Text = "以太网 / WLAN 流量对比";
        Size = new Size(460, 245);
        MinimumSize = new Size(360, 210);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 800);
        Location = new Point(area.Right - Width - 20, area.Top + 60);
        TopMost = true;
        ShowInTaskbar = false;
        Controls.Add(display);
    }

    public void UpdateRates(long ethernetUp, long ethernetDown, long wlanUp, long wlanDown) =>
        display.UpdateRates(ethernetUp, ethernetDown, wlanUp, wlanDown);
}

internal sealed class RateComparison : Control
{
    private long ethernetUp, ethernetDown, wlanUp, wlanDown;

    public RateComparison()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(22, 29, 42);
        ForeColor = Color.White;
    }

    public void UpdateRates(long eu, long ed, long wu, long wd)
    {
        ethernetUp = eu; ethernetDown = ed; wlanUp = wu; wlanDown = wd;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var titleFont = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
        using var valueFont = new Font("Microsoft YaHei UI", 9);
        using var line = new Pen(Color.FromArgb(64, 77, 96));
        g.DrawString("实时流量对比", titleFont, Brushes.White, 15, 10);
        g.DrawLine(line, 15, 38, Width - 15, 38);
        var max = Math.Max(1024, Math.Max(Math.Max(ethernetUp, ethernetDown), Math.Max(wlanUp, wlanDown)));
        PaintRow("以太网", ethernetUp, ethernetDown, 48);
        PaintRow("WLAN", wlanUp, wlanDown, 125);

        void PaintRow(string name, long up, long down, int y)
        {
            g.DrawString(name, titleFont, Brushes.White, 15, y);
            var label = $"↑ {Fmt.Rate(up)}     ↓ {Fmt.Rate(down)}";
            g.DrawString(label, valueFont, Brushes.Gainsboro, 92, y + 3);
            int barWidth = Math.Max(10, Width - 112);
            using var track = new SolidBrush(Color.FromArgb(45, 56, 72));
            using var downBrush = new SolidBrush(Color.DeepSkyBlue);
            using var upBrush = new SolidBrush(Color.Orange);
            g.FillRectangle(track, 92, y + 33, barWidth, 12);
            g.FillRectangle(downBrush, 92, y + 33, (float)(barWidth * down / (double)max), 12);
            g.FillRectangle(track, 92, y + 51, barWidth, 12);
            g.FillRectangle(upBrush, 92, y + 51, (float)(barWidth * up / (double)max), 12);
        }
    }
}
