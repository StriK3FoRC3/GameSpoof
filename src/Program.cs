using System;
using System.Diagnostics;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Theme.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && args[0] == "--placeholder")
        {
            if (args.Length != 4 && args.Length != 5) return;
            int pid; long ticks;
            if (!int.TryParse(args[1], out pid) || !long.TryParse(args[2], out ticks)) return;
            Process parent = null;
            try { parent = Process.GetProcessById(pid); if (parent.StartTime.ToUniversalTime().Ticks != ticks) { parent.Dispose(); return; } }
            catch { if (parent != null) parent.Dispose(); return; }
            using (parent)
            using (var window = new BackgroundGameWindow(args[3], args.Length == 5 && args[4] == "background"))
            using (var timer = new Timer())
            {
                timer.Interval = 1000;
                timer.Tick += delegate { try { if (parent.HasExited) window.Close(); } catch { window.Close(); } };
                timer.Start();
                Application.Run(window);
            }
            return;
        }
        Application.Run(new Launcher());
    }
}
