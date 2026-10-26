using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Keep the game-named window used by Discord's application discovery.
sealed class BackgroundGameWindow : Form
{
    readonly Label countdown = new BufferedLabel();
    bool background;
    Rectangle foregroundBounds;
    FormWindowState foregroundState;
    public BackgroundGameWindow(string name, bool hidden)
    {
        background = hidden;
        Text = name;
        StartPosition = background ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
        ClientSize = new Size(330, 116);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = Theme.AppIcon();
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(18,18), Location = new Point(0,0) });
        heading.Controls.Add(new Label { Text = "Your activity is ready", AutoSize = true, Location = new Point(25,0), Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        layout.Controls.Add(heading);
        layout.Controls.Add(new Label { Text = "Discord should now show you playing " + name + ".", Dock = DockStyle.Fill });
        countdown.Text = "Minimize to keep playing. Close to stop."; countdown.Dock = DockStyle.Fill; countdown.Font = new Font("Segoe UI", 8, FontStyle.Bold); countdown.ForeColor = Theme.Accent;
        layout.Controls.Add(countdown);
        Controls.Add(layout);
        if (background) Location = BackgroundLocation();
    }
    Point BackgroundLocation()
    {
        var desktop = SystemInformation.VirtualScreen;
        return new Point(desktop.Left - Width - 100, desktop.Top - Height - 100);
    }
    protected override bool ShowWithoutActivation { get { return background; } }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (background) {
            var area = Screen.PrimaryScreen.WorkingArea;
            foregroundBounds = new Rectangle(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2, Width, Height);
            Location = BackgroundLocation();
        }
    }
    void SetBackground(bool enabled)
    {
        if (background == enabled) return;
        if (enabled) {
            foregroundState = WindowState;
            foregroundBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            background = true;
            WindowState = FormWindowState.Normal;
            Location = BackgroundLocation();
        } else {
            background = false;
            if (!Array.Exists(Screen.AllScreens, screen => screen.WorkingArea.IntersectsWith(foregroundBounds))) {
                var area = Screen.PrimaryScreen.WorkingArea;
                foregroundBounds.Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            }
            Bounds = foregroundBounds;
            WindowState = foregroundState;
        }
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.Window(this); }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == ActivityMessages.BackgroundMessage) {
            SetBackground(message.WParam != IntPtr.Zero);
            message.Result = new IntPtr(1); return;
        }
        if (message.Msg == 0x4A && message.LParam != IntPtr.Zero) {
            var data = (ActivityMessages.CopyData)Marshal.PtrToStructure(message.LParam, typeof(ActivityMessages.CopyData));
            if (data.Id == new IntPtr(ActivityMessages.Id) && data.Size >= 2 && data.Size <= 512 && data.Size % 2 == 0 && data.Text != IntPtr.Zero) {
                string text = Marshal.PtrToStringUni(data.Text, data.Size / 2 - 1); if (countdown.Text != text) countdown.Text = text; message.Result = new IntPtr(1); return;
            }
        }
        base.WndProc(ref message);
    }

}

static class ActivityMessages
{
    public const int Id = 0x47535046;
    public const int BackgroundMessage = 0x800B;
    delegate bool WindowCallback(IntPtr handle, IntPtr parameter);
    static IntPtr FindWindow(Process process)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr unused) { uint pid; GetWindowThreadProcessId(window, out pid); if (pid == process.Id) { var className = new System.Text.StringBuilder(128); GetClassName(window, className, className.Capacity); var title = new System.Text.StringBuilder(256); GetWindowText(window, title, title.Capacity); if (title.Length > 0 && className.ToString().StartsWith("WindowsForms10.Window", StringComparison.Ordinal)) { found = window; return false; } } return true; }, IntPtr.Zero);
        return found;
    }
    public static void SetBackground(Process process, bool hidden)
    {
        if (process == null) return;
        try { IntPtr window = FindWindow(process); if (window != IntPtr.Zero) { IntPtr result; SendMessageTimeout(window, BackgroundMessage, hidden ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero, 2, 50, out result); } }
        catch (InvalidOperationException) { }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr sender, IntPtr data, uint flags, uint timeout, out IntPtr result);
    [StructLayout(LayoutKind.Sequential)] public struct CopyData { public IntPtr Id; public int Size; public IntPtr Text; }
    public static void Send(Process process, IntPtr sender, string text)
    {
        IntPtr buffer = IntPtr.Zero;
        try {
            IntPtr window = FindWindow(process); if (window == IntPtr.Zero) return;
            buffer = Marshal.StringToHGlobalUni(text);
            var data = new CopyData { Id = new IntPtr(Id), Size = (text.Length + 1) * 2, Text = buffer };
            IntPtr result; SendMessageTimeout(window, 0x4A, sender, ref data, 2, 50, out result);
        } catch (InvalidOperationException) { }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr sender, ref CopyData data, uint flags, uint timeout, out IntPtr result);
}
