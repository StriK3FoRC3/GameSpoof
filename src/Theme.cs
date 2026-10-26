using System;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Theme
{
    public static readonly Color Background = Color.FromArgb(17, 18, 22);
    public static readonly Color Panel = Color.FromArgb(26, 28, 35);
    public static readonly Color Input = Color.FromArgb(35, 37, 46);
    public static readonly Color Border = Color.FromArgb(48, 48, 59);
    public static readonly Color Text = Color.FromArgb(235, 240, 249);
    public static readonly Color Muted = Color.FromArgb(153, 147, 169);
    public static readonly Color Accent = Color.FromArgb(124, 108, 245);
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    public static void Initialize() { try { SetPreferredAppMode(2); } catch { } }
    public static void Window(Form form)
    {
        try { int yes = 1; DwmSetWindowAttribute(form.Handle, 20, ref yes, 4); } catch { }
    }
    public static void Scrollbar(IntPtr handle) { try { SetWindowTheme(handle, "DarkMode_Explorer", null); } catch { } }
    public static void SearchHint(TextBox box) { SendMessage(box.Handle, 0x1501, new IntPtr(1), "Search games..."); }
    public static Icon AppIcon()
    { return Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr handle, string name, string list);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr parameter, string text);
}

sealed class Card : Panel
{
    public Card() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); BackColor = Theme.Panel; Padding = new Padding(12); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
        using (var shape = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10))
        using (var brush = new SolidBrush(BackColor)) using (var pen = new Pen(Theme.Border)) { e.Graphics.FillPath(brush, shape); e.Graphics.DrawPath(pen, shape); }
        base.OnPaint(e);
    }
}

sealed class ActionButton : Button
{
    public bool Primary { get; set; }
    public bool Danger { get; set; }
    public bool TrashIcon { get; set; }
    public bool PlaylistIcon { get; set; }
    public bool ShuffleIcon { get; set; }
    public bool Selected { get; set; }
    bool hover;
    double hoverLevel;
    readonly Timer hoverAnimation = new Timer { Interval = 16 };
    long lastAnimationTick;
    public ActionButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        hoverAnimation.Tick += delegate {
            long now = Stopwatch.GetTimestamp();
            double step = (now - lastAnimationTick) / (double)Stopwatch.Frequency / .10;
            lastAnimationTick = now;
            double target = hover && Enabled ? 1 : 0;
            hoverLevel = target > hoverLevel ? Math.Min(target, hoverLevel + step) : Math.Max(target, hoverLevel - step);
            Invalidate(); if (hoverLevel == target) hoverAnimation.Stop();
        };
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    void AnimateHover(bool value)
    {
        hover = value;
        if (!hoverAnimation.Enabled) { lastAnimationTick = Stopwatch.GetTimestamp(); hoverAnimation.Start(); }
    }
    void ResetHover() { hover = false; hoverLevel = 0; hoverAnimation.Stop(); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { if (Enabled) AnimateHover(true); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { AnimateHover(false); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) ResetHover(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) ResetHover(); }
    protected override void Dispose(bool disposing) { if (disposing) hoverAnimation.Dispose(); base.Dispose(disposing); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var list = Parent as DarkList;
        e.Graphics.Clear(list == null ? (Parent == null ? Theme.Background : Parent.BackColor) : list.RowBackColor(list.HoveredIndex));
        Color fill = Primary && Enabled ? Theme.Accent : (Selected ? Color.FromArgb(53, 47, 79) : Theme.Input);
        if (Enabled && hoverLevel > 0) {
            Color highlight = Danger ? Color.FromArgb(175, 48, 62) : ControlPaint.Light(fill, .12f);
            double progress = hoverLevel * hoverLevel * (3 - 2 * hoverLevel);
            fill = Color.FromArgb((int)Math.Round(fill.R + (highlight.R - fill.R) * progress), (int)Math.Round(fill.G + (highlight.G - fill.G) * progress), (int)Math.Round(fill.B + (highlight.B - fill.B) * progress));
        }
        using (var shape = Theme.Round(new RectangleF(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), 6))
        using (var brush = new SolidBrush(fill)) using (var pen = new Pen(Enabled && (Selected || (Focused && !ShuffleIcon)) ? Theme.Accent : Theme.Border)) {
            e.Graphics.FillPath(brush, shape); e.Graphics.DrawPath(pen, shape);
        }
        if (ShuffleIcon) {
            var state = e.Graphics.Save();
            e.Graphics.TranslateTransform(Width / 2f, Height / 2f);
            e.Graphics.ScaleTransform(.75f, .75f);
            using (var pen = new Pen(Enabled ? Theme.Text : Theme.Muted, 1.5f)) {
                pen.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(pen, new[] { new PointF(-9, -6), new PointF(-5, -6), new PointF(5, 6), new PointF(10, 6) });
                e.Graphics.DrawLines(pen, new[] { new PointF(-9, 6), new PointF(-5, 6), new PointF(-2, 2) });
                e.Graphics.DrawLines(pen, new[] { new PointF(2, -2), new PointF(5, -6), new PointF(10, -6) });
                e.Graphics.DrawLines(pen, new[] { new PointF(6, -10), new PointF(10, -6), new PointF(6, -2) });
                e.Graphics.DrawLines(pen, new[] { new PointF(6, 2), new PointF(10, 6), new PointF(6, 10) });
            }
            e.Graphics.Restore(state);
        } else if (PlaylistIcon) {
            using (var pen = new Pen(Enabled ? Theme.Text : Theme.Muted, 1.5f)) {
                float x = Width / 2f - 7, y = Height / 2f - 6;
                e.Graphics.DrawLine(pen, x, y, x + 13, y); e.Graphics.DrawLine(pen, x, y + 5, x + 9, y + 5); e.Graphics.DrawLine(pen, x, y + 10, x + 6, y + 10);
                e.Graphics.DrawLine(pen, x + 13, y + 6, x + 13, y + 14); e.Graphics.DrawLine(pen, x + 9, y + 10, x + 17, y + 10);
            }
        } else if (TrashIcon) {
            float scale = Math.Min(Width, Height) / 34f;
            var state = e.Graphics.Save(); e.Graphics.TranslateTransform(Width / 2f, Height / 2f); e.Graphics.ScaleTransform(scale, scale);
            using (var pen = new Pen(Enabled ? Theme.Text : Theme.Muted, 1.5f)) {
                e.Graphics.DrawLine(pen, -7, -5, 7, -5); e.Graphics.DrawRectangle(pen, -3, -8, 6, 3);
                e.Graphics.DrawLine(pen, -5, -3, -4, 8); e.Graphics.DrawLine(pen, 5, -3, 4, 8); e.Graphics.DrawLine(pen, -4, 8, 4, 8);
                e.Graphics.DrawLine(pen, -2, -1, -2, 5); e.Graphics.DrawLine(pen, 2, -1, 2, 5);
            }
            e.Graphics.Restore(state);
        } else TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? Theme.Text : Color.FromArgb(108, 108, 126), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

// Native ListBox windows cannot use WS_EX_COMPOSITED directly. Apply it to
// their host so the list and its hover buttons are presented in one frame.
sealed class BufferedListPanel : Panel
{
    public BufferedListPanel(ListBox list)
    {
        Dock = DockStyle.Fill; Margin = list.Margin; list.Margin = Padding.Empty;
        BackColor = list.BackColor; Controls.Add(list);
    }
    protected override CreateParams CreateParams
    {
        get {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return parameters;
        }
    }
}

sealed class DarkList : ListBox
{
    public event Action<int, bool> RowAction;
    public event Action<int> RowInfo;
    public Func<int, bool> HasRowInfo { get; set; }
    public bool RowActions { get; set; }
    bool selectingAll;
    readonly ActionButton add = new ActionButton { PlaylistIcon = true, AccessibleName = "Add selected games to playlist", Cursor = Cursors.Hand };
    readonly ActionButton remove = new ActionButton { TrashIcon = true, Danger = true, AccessibleName = "Remove selected games", Cursor = Cursors.Hand };
    int hovered = -1;
    readonly Dictionary<int, double> hoverLevels = new Dictionary<int, double>();
    readonly Timer hoverAnimation = new Timer { Interval = 16 };
    long lastAnimationTick;
    public int HoveredIndex { get { return hovered; } }
    public Color RowBackColor(int index)
    {
        return RowBackColor(index, index >= 0 && index < Items.Count && GetSelected(index));
    }
    public Color RowBackColor(int index, bool selected)
    {
        if (index < 0 || index >= Items.Count) return BackColor;
        double progress;
        if (!hoverLevels.TryGetValue(index, out progress)) progress = 0;
        progress = progress * progress * (3 - 2 * progress);
        Color start = selected ? Color.FromArgb(53, 47, 79) : BackColor;
        Color end = selected ? Color.FromArgb(65, 57, 94) : Color.FromArgb(36, 37, 49);
        return Color.FromArgb((int)Math.Round(start.R + (end.R - start.R) * progress), (int)Math.Round(start.G + (end.G - start.G) * progress), (int)Math.Round(start.B + (end.B - start.B) * progress));
    }
    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        if (selectingAll) return;
        base.OnSelectedIndexChanged(e);
        // Native range selection may draw before the managed selection cache updates.
        // Repaint the entire visible range once the selection change is complete.
        Invalidate(); add.Invalidate(); remove.Invalidate();
    }
    public void SelectAllItems()
    {
        BeginUpdate(); selectingAll = true;
        try { for (int i = 0; i < Items.Count; i++) SetSelected(i, true); }
        finally { selectingAll = false; EndUpdate(); OnSelectedIndexChanged(EventArgs.Empty); }
    }
    public DarkList()
    {
        hoverAnimation.Tick += delegate { AnimateHover(); };
        Controls.Add(add); Controls.Add(remove); add.Visible = remove.Visible = false;
        add.MouseLeave += delegate { if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) HideActions(); };
        remove.MouseLeave += delegate { if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) HideActions(); };
        add.Click += delegate { if (RowAction != null && hovered >= 0) RowAction(hovered, false); };
        remove.Click += delegate { if (RowAction != null && hovered >= 0) RowAction(hovered, true); };
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.Scrollbar(Handle); }
    bool HasInfo(int index) { return index >= 0 && index < Items.Count && HasRowInfo != null && HasRowInfo(index); }
    Rectangle InfoBounds(int index) { var row = GetItemRectangle(index); return new Rectangle(ClientSize.Width - 40, row.Top + (row.Height - 30) / 2, 32, 30); }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        base.OnDrawItem(e);
        if (!HasInfo(e.Index)) return;
        var bounds = InfoBounds(e.Index);
        var state = e.Graphics.Save(); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var shape = Theme.Round(new RectangleF(bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3), 6))
        using (var fill = new SolidBrush(Theme.Input)) using (var border = new Pen(Theme.Border)) {
            e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
        }
        using (var pen = new Pen(Theme.Text, 1.4f)) {
            float x = bounds.X + bounds.Width / 2f, y = bounds.Y + bounds.Height / 2f;
            e.Graphics.DrawEllipse(pen, x - 7, y - 7, 14, 14);
            e.Graphics.DrawLine(pen, x, y - 1, x, y + 4);
            e.Graphics.DrawEllipse(pen, x - .4f, y - 4, .8f, .8f);
        }
        e.Graphics.Restore(state);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int index = IndexFromPoint(e.Location);
        if (e.Button == MouseButtons.Left && HasInfo(index) && InfoBounds(index).Contains(e.Location) && RowInfo != null) RowInfo(index);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F1 && HasInfo(SelectedIndex) && RowInfo != null) { RowInfo(SelectedIndex); e.Handled = e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexFromPoint(e.Location);
        Cursor = HasInfo(index) && InfoBounds(index).Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        SetHovered(index); if (!RowActions) return;
        if (index < 0) { HideActions(); return; }
        hovered = index; var row = GetItemRectangle(index);
        add.SetBounds(ClientSize.Width - 76, row.Top + 6, 32, 30); remove.SetBounds(ClientSize.Width - 40, row.Top + 6, 32, 30);
        add.Visible = remove.Visible = true; add.BringToFront(); remove.BringToFront();
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) HideActions(); }
    void SetHovered(int index)
    {
        if (hovered == index) return;
        hovered = index;
        if (hovered >= 0 && !hoverLevels.ContainsKey(hovered)) hoverLevels[hovered] = 0;
        if (!hoverAnimation.Enabled) { lastAnimationTick = Stopwatch.GetTimestamp(); hoverAnimation.Start(); }
        add.Invalidate(); remove.Invalidate();
    }
    void AnimateHover()
    {
        long now = Stopwatch.GetTimestamp();
        double step = (now - lastAnimationTick) / (double)Stopwatch.Frequency / .10;
        lastAnimationTick = now; bool moving = false;
        foreach (int index in new List<int>(hoverLevels.Keys)) {
            if (index >= Items.Count) { hoverLevels.Remove(index); continue; }
            double target = index == hovered ? 1 : 0;
            double value = hoverLevels[index];
            value = target > value ? Math.Min(target, value + step) : Math.Max(target, value - step);
            if (value == 0) hoverLevels.Remove(index); else hoverLevels[index] = value;
            if (value != target) moving = true;
            Invalidate(GetItemRectangle(index));
        }
        add.Invalidate(); remove.Invalidate();
        if (!moving) hoverAnimation.Stop();
    }
    public void HideActions() { SetHovered(-1); add.Visible = remove.Visible = false; }
    public void ResetHover() { hovered = -1; hoverLevels.Clear(); hoverAnimation.Stop(); add.Visible = remove.Visible = false; Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) hoverAnimation.Dispose(); base.Dispose(disposing); }
    protected override void WndProc(ref Message m) { if (m.Msg == 0x115 || m.Msg == 0x20A || m.Msg == 5) ResetHover(); base.WndProc(ref m); }
}

sealed class DarkInformationDialog : Form
{
    public DarkInformationDialog(string game)
    {
        Text = "Game detection"; ClientSize = new Size(400, 230); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MaximizeBox = MinimizeBox = false;
        BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9); Icon = Theme.AppIcon();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(new Label { Text = game, Font = new Font("Segoe UI", 12, FontStyle.Bold), Dock = DockStyle.Fill, AutoEllipsis = true });
        layout.Controls.Add(new Label { Text = "Discord's catalogue has no supported Windows executable for this game. It most likely uses another detection method, such as Rich Presence, or its executable details are missing.\n\nGameSpoof currently has no supported way to spoof this game.", ForeColor = Theme.Muted, Dock = DockStyle.Fill });
        var close = new ActionButton { Text = "OK", Primary = true, DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 90, Margin = Padding.Empty };
        layout.Controls.Add(close); Controls.Add(layout); AcceptButton = CancelButton = close;
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.Window(this); }
}

public sealed class MinutesSelector : UserControl
{
    readonly TextBox editor = new TextBox();
    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; }
    public event EventHandler ValueChanged;
    public event EventHandler ValueCommitted;
    bool normalizing, automaticMinutes;
    public decimal Value
    {
        get {
            decimal value;
            if (!TryParse(editor.Text, out value) || value < Minimum || value > Maximum)
                throw new ArgumentException("Enter a duration such as 30s, 5m or 2h (a bare number means minutes).");
            return value;
        }
        set {
            if (value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException("value");
            editor.Text = Format(value);
        }
    }
    public string InputText { get { return editor.Text; } set { editor.Text = value; } }
    public static bool TryParse(string text, out decimal minutes)
    {
        minutes = 0;
        var match = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^([0-9]+(?:[.,][0-9]+)?)\s*([smh]?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        decimal number;
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out number)) return false;
        try {
            string unit = match.Groups[2].Value.ToLowerInvariant();
            minutes = unit == "s" ? number / 60m : unit == "h" ? number * 60m : number;
            return minutes >= 0 && minutes <= 10080;
        } catch (OverflowException) { return false; }
    }
    public static string Format(decimal minutes)
    {
        if (minutes == 0) return "0s";
        if (minutes % 60m == 0) return (minutes / 60m).ToString("0.###") + "h";
        if (minutes % 1m == 0) return minutes.ToString("0.###") + "m";
        return (minutes * 60m).ToString("0.###") + "s";
    }
    public MinutesSelector()
    {
        Minimum = 1m / 60m; Maximum = 10080;
        DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); BackColor = Theme.Input; MinimumSize = new Size(50, 28);
        editor.BorderStyle = BorderStyle.None; editor.BackColor = Theme.Input; editor.ForeColor = Theme.Text; editor.TextAlign = HorizontalAlignment.Center;
        editor.AccessibleName = "Duration: s seconds, m minutes, h hours";
        editor.TextChanged += delegate {
            if (normalizing) return;
            string text = editor.Text;
            int caret = editor.SelectionStart, selected = editor.SelectionLength;
            if (automaticMinutes && System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*[0-9]+(?:[.,][0-9]*)?\s*[smh]m$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) {
                text = text.Substring(0, text.Length - 1); automaticMinutes = false;
            } else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*[0-9]+(?:[.,][0-9]*)?\s*$")) {
                text = text.TrimEnd() + "m"; automaticMinutes = true;
            } else if (!text.EndsWith("m", StringComparison.OrdinalIgnoreCase)) automaticMinutes = false;
            if (text != editor.Text) {
                normalizing = true;
                try { editor.Text = text; editor.Select(Math.Min(caret, text.Length), Math.Min(selected, Math.Max(0, text.Length - caret))); }
                finally { normalizing = false; }
            }
            decimal parsed; bool valid = TryParse(editor.Text, out parsed) && parsed >= Minimum && parsed <= Maximum;
            editor.ForeColor = valid ? Theme.Text : Color.FromArgb(255, 159, 163);
            if (valid && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        };
        editor.Enter += delegate { editor.SelectAll(); };
        editor.KeyDown += delegate(object sender, KeyEventArgs e) {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            decimal parsed;
            if (!TryParse(editor.Text, out parsed) || parsed < Minimum || parsed > Maximum) { editor.SelectAll(); return; }
            editor.Text = editor.Text.Trim().ToLowerInvariant();
            if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty);
        };
        Controls.Add(editor); Value = 10;
    }
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); editor.SetBounds(5, Math.Max(3, (Height - editor.PreferredHeight) / 2), Math.Max(1, Width - 10), editor.PreferredHeight); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (editor != null) editor.Font = Font; PerformLayout(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent == null ? Theme.Panel : Parent.BackColor);
        using (var shape = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 6))
        using (var fill = new SolidBrush(Theme.Input)) using (var pen = new Pen(ContainsFocus ? Theme.Accent : Theme.Border)) { e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(pen, shape); }
    }
}

sealed class DarkConfirmationDialog : Form
{
    public DarkConfirmationDialog(string title, string message, string confirmText)
    {
        Text = title; ClientSize = new Size(380, 184); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MaximizeBox = false; MinimizeBox = false;
        BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9); Icon = Theme.AppIcon();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 13, FontStyle.Bold), Dock = DockStyle.Fill });
        layout.Controls.Add(new Label { Text = message, ForeColor = Theme.Muted, Dock = DockStyle.Fill });
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 3 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        var cancel = new ActionButton { Text = "Cancel", DialogResult = DialogResult.No, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
        var confirm = new ActionButton { Text = confirmText, Primary = true, DialogResult = DialogResult.Yes, Dock = DockStyle.Fill, Margin = Padding.Empty };
        buttons.Controls.Add(cancel, 1, 0); buttons.Controls.Add(confirm, 2, 0); layout.Controls.Add(buttons);
        AcceptButton = cancel; CancelButton = cancel; Controls.Add(layout);
        Shown += delegate { cancel.Focus(); };
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.Window(this); }
}

sealed class DarkToolTip : ToolTip
{
    public DarkToolTip()
    {
        OwnerDraw = true; BackColor = Theme.Input; ForeColor = Theme.Text;
        Popup += delegate(object sender, PopupEventArgs e) {
            string text = e.AssociatedControl == null ? "" : GetToolTip(e.AssociatedControl);
            var size = TextRenderer.MeasureText(text, SystemFonts.MessageBoxFont, new Size(360, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            e.ToolTipSize = new Size(size.Width + 18, size.Height + 14);
        };
        Draw += delegate(object sender, DrawToolTipEventArgs e) {
            using (var fill = new SolidBrush(Theme.Input)) e.Graphics.FillRectangle(fill, e.Bounds);
            using (var border = new Pen(Theme.Border)) e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            var textBounds = e.Bounds; textBounds.Inflate(-9, -7);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, SystemFonts.MessageBoxFont, textBounds, Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        };
    }
}

sealed class RepeatSwitch : CheckBox
{
    bool pointerClick, acceptPointerClick;
    Rectangle SwitchBounds { get { return new Rectangle(1, (Height - 18) / 2, 32, 18); } }
    bool HitSwitch(Point point) { using (var shape = Theme.Round(SwitchBounds, 9)) return shape.IsVisible(point); }
    public RepeatSwitch() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) {
            if (!HitSwitch(e.Location)) return;
            pointerClick = acceptPointerClick = true;
        }
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (pointerClick && e.Button == MouseButtons.Left) {
            acceptPointerClick = HitSwitch(e.Location);
            try { base.OnMouseUp(e); } finally { pointerClick = false; }
        } else base.OnMouseUp(e);
    }
    protected override void OnClick(EventArgs e) { if (!pointerClick || acceptPointerClick) base.OnClick(e); }
    protected override void OnMouseMove(MouseEventArgs e) { Cursor = HitSwitch(e.Location) ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int top = (Height - 18) / 2;
        using (var path = Theme.Round(new RectangleF(1, top, 32, 18), 9)) using (var fill = new SolidBrush(Checked ? Theme.Accent : Theme.Input)) using (var pen = new Pen(Focused ? Theme.Accent : Theme.Border)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path); }
        using (var fill = new SolidBrush(Theme.Text)) e.Graphics.FillEllipse(fill, Checked ? 17 : 4, top + 3, 12, 12);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(40, 0, Width - 40, Height), Theme.Text, TextFormatFlags.VerticalCenter);
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

static class IconArt
{
    public static void Draw(Graphics graphics, int size)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(size / 64f, size / 64f);
        using (var tile = Theme.Round(new RectangleF(2, 2, 60, 60), 16))
        using (var fill = new LinearGradientBrush(new Point(8, 4), new Point(56, 60), Color.FromArgb(155, 139, 255), Color.FromArgb(93, 72, 205))) graphics.FillPath(fill, tile);
        using (var ghost = new GraphicsPath()) {
            ghost.AddBezier(17, 45, 17, 38, 16, 29, 20, 23);
            ghost.AddBezier(20, 23, 25, 15, 39, 15, 44, 23);
            ghost.AddBezier(44, 23, 48, 29, 47, 38, 47, 45);
            ghost.AddLines(new[] { new PointF(47,45), new PointF(39,41), new PointF(32,46), new PointF(25,41), new PointF(17,45) });
            ghost.CloseFigure(); using (var fill = new SolidBrush(Color.FromArgb(249, 247, 255))) graphics.FillPath(fill, ghost);
        }
        using (var pen = new Pen(Color.FromArgb(110, 88, 222), 3)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; graphics.DrawLine(pen, 23, 30, 29, 30); graphics.DrawLine(pen, 26, 27, 26, 33); }
        using (var fill = new SolidBrush(Color.FromArgb(110, 88, 222))) { graphics.FillEllipse(fill, 36, 26, 4, 4); graphics.FillEllipse(fill, 40, 31, 4, 4); }
        graphics.ResetTransform();
    }
}

sealed class BufferedLabel : Label
{
    public BufferedLabel() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true); }
}
