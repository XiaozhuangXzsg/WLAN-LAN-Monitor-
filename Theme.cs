using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace NetworkMonitor;

internal static class Theme
{
    public const string WinUITheme = "WinUI 3 半透明";
    public static IReadOnlyList<string> Names { get; } = new[] { "原神暖白", "夜色深蓝", "晨曦浅金", WinUITheme };
    public static bool IsTranslucent => Current == WinUITheme;
    public static Color Hover => IsTranslucent ? Color.FromArgb(232, 240, 250) : Current == "夜色深蓝" ? Color.FromArgb(49, 63, 85) : Color.FromArgb(242, 230, 202);
    private enum ColorRole { Text, Muted, Blue, Teal, Gold }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public static void DarkTitleBar(Form form) { int enabled = 1; DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)); }
    public static Color Background { get; private set; }
    public static Color Surface { get; private set; }
    public static Color Border { get; private set; }
    public static Color Text { get; private set; }
    public static Color Muted { get; private set; }
    public static Color Blue { get; private set; }
    public static Color Teal { get; private set; }
    public static Color Gold { get; private set; }
    public static Color FloatingSecondaryText => Color.FromArgb((Text.R + Muted.R) / 2, (Text.G + Muted.G) / 2, (Text.B + Muted.B) / 2);
    public static string Current { get; private set; } = "原神暖白";
    public static event Action? Changed;
    static Theme() => Set("原神暖白", false);
    public static void Set(string name, bool notify = true)
    {
        Current = name;
        if (name == WinUITheme)
        {
            Background = Color.FromArgb(243, 246, 250); Surface = Color.FromArgb(252, 253, 255); Border = Color.FromArgb(212, 222, 234);
            Text = Color.FromArgb(26, 35, 48); Muted = Color.FromArgb(86, 101, 122); Blue = Color.FromArgb(0, 103, 192);
            Teal = Color.FromArgb(0, 128, 121); Gold = Color.FromArgb(54, 105, 165);
        }
        else if (name == "夜色深蓝")
        {
            Background = Color.FromArgb(24, 31, 46); Surface = Color.FromArgb(37, 47, 66); Border = Color.FromArgb(102, 135, 167);
            Text = Color.FromArgb(231, 237, 244); Muted = Color.FromArgb(165, 181, 198); Blue = Color.FromArgb(111, 184, 229);
            Teal = Color.FromArgb(107, 195, 164); Gold = Color.FromArgb(229, 188, 105);
        }
        else if (name == "晨曦浅金")
        {
            Background = Color.FromArgb(248, 246, 240); Surface = Color.FromArgb(255, 254, 250); Border = Color.FromArgb(205, 194, 169);
            Text = Color.FromArgb(51, 56, 64); Muted = Color.FromArgb(121, 119, 113); Blue = Color.FromArgb(71, 145, 194);
            Teal = Color.FromArgb(81, 158, 123); Gold = Color.FromArgb(173, 132, 59);
        }
        else
        {
            Current = "原神暖白";
            Background = Color.FromArgb(237, 231, 216); Surface = Color.FromArgb(250, 247, 238); Border = Color.FromArgb(203, 181, 126);
            Text = Color.FromArgb(47, 51, 63); Muted = Color.FromArgb(113, 111, 109); Blue = Color.FromArgb(74, 151, 194);
            Teal = Color.FromArgb(91, 157, 119); Gold = Color.FromArgb(179, 137, 61);
        }
        if (notify) Changed?.Invoke();
    }
    public static void PaintParent(Control control, PaintEventArgs e)
    {
        if (control.Parent is null) { e.Graphics.Clear(Theme.Background); return; }
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(-control.Left, -control.Top);
        using var args = new PaintEventArgs(e.Graphics, control.Parent.ClientRectangle);
        InvokeBackground(control.Parent, args);
        InvokeForeground(control.Parent, args);
        e.Graphics.Restore(state);
    }
    private static void InvokeBackground(Control control, PaintEventArgs e) => typeof(Control).GetMethod("OnPaintBackground", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, new object[] { e });
    private static void InvokeForeground(Control control, PaintEventArgs e) => typeof(Control).GetMethod("OnPaint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, new object[] { e });
    public static Color ControlBackground(Control control)
    {
        Color color = Background;
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is RoundedCard) { color = Surface; break; }
            if (parent.BackColor.A == 255) { color = parent.BackColor; break; }
        }
        return color;
    }
    public static void PaintButtonBackground(Control control, PaintEventArgs e) => e.Graphics.Clear(ControlBackground(control));
    public static void ClipRoundedControl(Control control)
    {
        if (control.Width <= 0 || control.Height <= 0) return;
        using var path = RoundedPath(control.ClientRectangle, 9);
        var oldRegion = control.Region; control.Region = new Region(path); oldRegion?.Dispose();
    }
    public static void ApplyTree(Control root)
    {
        if (root is Form || root.Name is "DashboardLayout" or "DashboardHeader" or "DashboardToolbar" or "DashboardColumns") { root.BackColor = Background; root.ForeColor = Text; }
        else if (root is RoundedCard) root.BackColor = Color.Transparent;
        else if (root is RoundedButton or RoundedToggle) { root.BackColor = ControlBackground(root); root.ForeColor = Text; }
        else if (root is Label label && label.Tag is ColorRole role)
        {
            label.ForeColor = role switch { ColorRole.Muted => Muted, ColorRole.Blue => Blue, ColorRole.Teal => Teal, ColorRole.Gold => Gold, _ => Text };
        }
        else if (root is ListView) { root.BackColor = Surface; root.ForeColor = Text; }
        else if (root is NumericUpDown) { root.BackColor = Surface; root.ForeColor = Text; }
        else if (root is TextBox) { root.BackColor = Surface; root.ForeColor = Text; }
        else if (root is RateComparison) { root.BackColor = Background; root.ForeColor = Text; }
        else if (root is Panel or UserControl or QuotaMeter or Chart) { root.BackColor = Surface; root.ForeColor = Text; }
        if (root.Font.FontFamily.Name != FontFamily) root.Font = new Font(FontFamily, root.Font.Size, root.Font.Style);
        foreach (Control child in root.Controls) ApplyTree(child);
        root.Invalidate(true);
    }
    public static void ApplyWindow(Form form, bool floating = false)
    {
        // Layered-window alpha works with the existing WinForms controls on Windows 10 and 11.
        double opacity = IsTranslucent ? floating ? 0.96 : 0.92 : 1;
        if (Math.Abs(form.Opacity - opacity) > 0.005) form.Opacity = opacity;
        if (!form.IsHandleCreated) return;
        SetWindowAttribute(form.Handle, 20, Current == "夜色深蓝" ? 1 : 0);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            SetWindowAttribute(form.Handle, 33, 2);
            SetWindowAttribute(form.Handle, 35, ColorTranslator.ToWin32(Background));
            SetWindowAttribute(form.Handle, 36, ColorTranslator.ToWin32(Text));
        }
    }
    private static void SetWindowAttribute(IntPtr window, int attribute, int value) => DwmSetWindowAttribute(window, attribute, ref value, sizeof(int));
    private static readonly string ClassicFontFamily = GetFontFamily();
    public static string FloatingFontFamily { get; } = GetFloatingFontFamily();
    public static string FontFamily => IsTranslucent ? "Segoe UI" : ClassicFontFamily;
    private static string GetFloatingFontFamily()
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        var installed = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var family in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" })
            if (installed.Contains(family)) return family;
        return SystemFonts.MessageBoxFont!.FontFamily.Name;
    }
    private static string GetFontFamily()
    {
        var installed = new System.Drawing.Text.InstalledFontCollection().Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (installed.Contains("HYWenHei-85W")) return "HYWenHei-85W";
        if (installed.Contains("方正黑体")) return "方正黑体";
        if (installed.Contains("黑体")) return "黑体";
        return "Microsoft YaHei UI";
    }
    public static Label Label(string text, float size = 10, Color? color = null, bool bold = false) => new()
    {
        Text = text, ForeColor = color ?? Text, Tag = color == Muted ? ColorRole.Muted : color == Blue ? ColorRole.Blue : color == Teal ? ColorRole.Teal : color == Gold ? ColorRole.Gold : ColorRole.Text,
        Font = new Font(FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular),
        AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
    };
    public static RoundedToggle Toggle(string text) => new(text);
    public static RoundedButton Button(string text) => new(text);
    public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    public static TabControl Tabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(170, 30), Padding = new Point(12, 4) };
        tabs.DrawItem += (_, e) =>
        {
            using var brush = new SolidBrush(e.Index == tabs.SelectedIndex ? Border : Surface);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        return tabs;
    }
}

internal sealed class RoundedButton : Button
{
    private bool hovering;
    public RoundedButton(string text)
    {
        Text = text; AutoSize = true; MinimumSize = new Size(110, 36); Padding = new Padding(12, 5, 12, 5);
        FlatStyle = FlatStyle.Standard; UseVisualStyleBackColor = false; BackColor = Theme.Surface; ForeColor = Theme.Text;
        Font = new Font(Theme.FontFamily, 9); Cursor = Cursors.Hand; Margin = new Padding(0, 0, 10, 0);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Surface;
        MouseEnter += (_, _) => { hovering = true; Invalidate(); };
        MouseLeave += (_, _) => { hovering = false; Invalidate(); };
        Theme.ClipRoundedControl(this);
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Theme.ClipRoundedControl(this); }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintButtonBackground(this, e);
    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.PaintButtonBackground(this, e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 9);
        using var fill = new SolidBrush(Enabled ? hovering ? Theme.Hover : Theme.Surface : Theme.Background);
        using var pen = new Pen(hovering ? Theme.Gold : Theme.Border, 1.2f);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class RoundedToggle : Button
{
    private bool hovering;
    private bool isChecked;
    public event EventHandler? CheckedChanged;
    public bool Checked
    {
        get => isChecked;
        set { if (isChecked == value) return; isChecked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public RoundedToggle(string text)
    {
        Text = text; AutoSize = true; MinimumSize = new Size(150, 36); Padding = new Padding(12, 5, 12, 5);
        FlatStyle = FlatStyle.Standard; UseVisualStyleBackColor = false; BackColor = Theme.Surface; ForeColor = Theme.Text;
        Font = new Font(Theme.FontFamily, 9); Cursor = Cursors.Hand; Margin = new Padding(0, 0, 10, 0);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Surface;
        Click += (_, _) => Checked = !Checked;
        MouseEnter += (_, _) => { hovering = true; Invalidate(); };
        MouseLeave += (_, _) => { hovering = false; Invalidate(); };
        Theme.ClipRoundedControl(this);
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Theme.ClipRoundedControl(this); }
    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintButtonBackground(this, e);
    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.PaintButtonBackground(this, e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 9);
        using var fill = new SolidBrush(hovering ? Theme.Hover : Theme.Surface);
        using var pen = new Pen(Checked ? Theme.Gold : Theme.Border, 1.2f);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, (Checked ? "●  " : "○  ") + Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class QuotaMeter : Control
{
    private long limit, used;
    public QuotaMeter() { DoubleBuffered = true; BackColor = Theme.Surface; Height = 42; }
    public void SetUsage(long cap, long current) { limit = cap; used = Math.Max(0, current); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        string line = limit <= 0 ? $"本月已用 {Fmt.Bytes(used)}   ·   尚未设置月额度" : $"剩余 {Fmt.Bytes(Math.Max(0, limit - used))} / {Fmt.Bytes(limit)}   ·   {Math.Max(0, 100 - Math.Min(100, used * 100d / limit)):F0}%";
        TextRenderer.DrawText(e.Graphics, line, Font, new Rectangle(4, 0, Width - 8, 23), Theme.Gold, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        int y = Math.Min(Height - 9, 27), width = Math.Max(1, Width - 12);
        using var path = Theme.RoundedPath(new Rectangle(4, y, width, 7), 3);
        using var track = new SolidBrush(Theme.Border); e.Graphics.FillPath(track, path);
        if (limit > 0)
        {
            int left = (int)(width * (Math.Max(0, limit - used) / (double)limit));
            if (left > 0)
            {
                using var active = Theme.RoundedPath(new Rectangle(4, y, Math.Max(6, left), 7), 3);
                using var gradient = new LinearGradientBrush(new Rectangle(4, y, width, 7), Theme.Teal, Theme.Gold, 0f);
                e.Graphics.FillPath(gradient, active);
            }
        }
    }
}

internal sealed class RoundedCard : Panel
{
    public RoundedCard()
    {
        DoubleBuffered = true; BackColor = Color.Transparent; Padding = new Padding(14);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        SizeChanged += (_, _) => { using var path = Theme.RoundedPath(new Rectangle(0, 0, Width, Height), 15); Region = new Region(path); };
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Theme.PaintParent(this, e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 15);
        using var brush = new SolidBrush(Theme.Surface);
        e.Graphics.FillPath(brush, path);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 15);
        using var pen = new Pen(Theme.Border, 1.3f); e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class AdapterPanel : Panel
{
    private readonly RoundedButton selector = Theme.Button("选择网卡  ▾");
    private readonly ContextMenuStrip menu = new() { BackColor = Theme.Surface, ForeColor = Theme.Text, ShowImageMargin = false };
    private readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
    private readonly List<Control> pages = new();
    public int Count => pages.Count;
    public AdapterPanel()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Surface;
        selector.Dock = DockStyle.Top; selector.AutoSize = false; selector.Height = 38; selector.Text = "暂无网卡  ▾";
        Controls.Add(content); Controls.Add(selector);
        selector.Click += (_, _) =>
        {
            menu.BackColor = Theme.Surface; menu.ForeColor = Theme.Text;
            foreach (ToolStripItem item in menu.Items) { item.BackColor = Theme.Surface; item.ForeColor = Theme.Text; item.Font = new Font(Theme.FontFamily, 9); }
            menu.Show(selector, new Point(0, selector.Height));
        };
    }
    public void Add(string name, Control view)
    {
        view.Dock = DockStyle.Fill; view.Visible = false;
        pages.Add(view); content.Controls.Add(view);
        var item = new ToolStripMenuItem(name) { ForeColor = Theme.Text, BackColor = Theme.Surface, Font = new Font(Theme.FontFamily, 9) };
        int index = pages.Count - 1;
        item.Click += (_, _) => Select(index);
        menu.Items.Add(item);
        if (pages.Count == 1) Select(0);
    }
    private void Select(int index)
    {
        for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == index;
        selector.Text = menu.Items[index].Text + "  ▾";
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
    public long Peak => Math.Max(1024, points.Select(p => Math.Max(p.up, p.down)).DefaultIfEmpty().Max());
    public long SharedMaximum { get; set; } = 1024;
    public Chart() { DoubleBuffered = true; BackColor = Theme.Surface; }
    public void Add(long up, long down) { points.Enqueue((up, down)); while (points.Count > 120) points.Dequeue(); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var data = points.ToArray();
        int bottom = Height - 24, top = 32;
        using var grid = new Pen(Theme.Border);
        for (var i = 0; i <= 3; i++) { var y = top + i * (bottom - top) / 3f; g.DrawLine(grid, 12, y, Width - 12, y); }
        var max = Math.Max(Peak, SharedMaximum);
        Draw(data.Select(p => p.down).ToArray(), Theme.Blue);
        Draw(data.Select(p => p.up).ToArray(), Theme.Teal);
        TextRenderer.DrawText(g, $"↓ 下载     ↑ 上传                  刻度 {Fmt.Rate(max)}", Font, new Rectangle(12, 5, Width - 24, 23), Theme.Muted, TextFormatFlags.Left);
        TextRenderer.DrawText(g, "最近 120 次采样", Font, new Point(12, Height - 20), Theme.Muted);
        void Draw(long[] values, Color color)
        {
            if (values.Length < 2) return;
            using var pen = new Pen(color, 2);
            var pts = values.Select((v, i) => new PointF(Width - 12 - (values.Length - 1 - i) * (Width - 24) / 119f, bottom - (bottom - top) * (float)(v / (double)max))).ToArray();
            g.DrawLines(pen, pts);
        }
    }
}
