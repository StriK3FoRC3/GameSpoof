using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Tests
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(Form form, string name) { return form.GetType().GetField(name, Flags).GetValue(form); }
    static object Call(Form form, string name, params object[] args) { return form.GetType().GetMethod(name, Flags).Invoke(form, args); }
    static Form Create(string root) { return (Form)typeof(PlaylistRun).Assembly.GetType("Launcher").GetConstructor(Flags, null, new[] { typeof(string) }, null).Invoke(new object[] { root }); }
    static void Check(bool valid, string name) { if (!valid) throw new Exception(name); Console.WriteLine("PASS: " + name); }
    static void Pump(int ms) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(20); } }
    static bool Until(Func<bool> condition, int ms) { var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < ms) Pump(25); return condition(); }
    delegate bool WindowCallback(IntPtr window, IntPtr unused);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] static extern bool SetKeyboardState(byte[] state);
    [StructLayout(LayoutKind.Sequential)] struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    static bool GameWindow(Process process, bool onScreen)
    {
        bool found = false;
        EnumWindows(delegate(IntPtr window, IntPtr unused) {
            uint pid; GetWindowThreadProcessId(window, out pid);
            var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
            WindowRect rect;
            if (pid == process.Id && (title.ToString() == "Crab Game" || title.ToString() == "Unturned") && IsWindowVisible(window) && GetWindowRect(window, out rect)) {
                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(bounds)) == onScreen) found = true;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    static void Render(Form form, string name) { Application.DoEvents(); using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(name); } }
    static void Confirm(Form form, string method, bool yes)
    {
        bool seen = false;
        using (var closer = new System.Windows.Forms.Timer { Interval = 30 }) {
            closer.Tick += delegate { var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(x => x.Text == "Remove games?"); if (dialog != null) { seen = dialog.BackColor.R < 40 && dialog.AcceptButton.DialogResult == DialogResult.No; dialog.DialogResult = yes ? DialogResult.Yes : DialogResult.No; dialog.Close(); } };
            closer.Start(); Call(form, method);
        }
        Check(seen, "destructive action keeps dark confirmation and Cancel default");
    }
    static void Range(Form form, string name, string value) { ((MinutesSelector)Field(form, name)).InputText = value; }
    static void Toggle(Form form, string name, bool value) { ((CheckBox)Field(form, name)).Checked = value; }
    static void CheckTogglePersistence(string root)
    {
        Directory.CreateDirectory(root);
        string[] toggles = { "backgroundMode", "timerEnabled", "repeat", "randomPlay", "randomPause" };
        using (var form = Create(root)) {
            Check(toggles.All(name => !((CheckBox)Field(form, name)).Checked), "new installations default all switches off");
            foreach (string name in toggles) Toggle(form, name, true);
            Call(form, "AddItem", new PlaylistItem { Name = "One", Path = "one.exe", Minutes = 1 }, 1m);
            Call(form, "AddItem", new PlaylistItem { Name = "Two", Path = "two.exe", Minutes = 1 }, 1m);
            Call(form, "ShufflePlaylist");
        }
        using (var form = Create(root)) {
            Check(toggles.All(name => ((CheckBox)Field(form, name)).Checked) && (bool)Field(form, "shuffled"), "all enabled toggles including shuffle survive restart");
            Check(Field(form, "active") == null && Field(form, "run") == null, "loading saved toggles does not start playback");
            foreach (string name in toggles) Toggle(form, name, false);
            Call(form, "ShufflePlaylist");
        }
        using (var form = Create(root)) {
            Check(toggles.All(name => !((CheckBox)Field(form, name)).Checked) && !(bool)Field(form, "shuffled"), "all disabled toggles survive restart");
            Toggle(form, "randomPlay", true); Range(form, "playMin", "20m");
            Toggle(form, "backgroundMode", true); Toggle(form, "timerEnabled", true);
        }
        using (var form = Create(root))
            Check(((CheckBox)Field(form, "backgroundMode")).Checked && ((CheckBox)Field(form, "timerEnabled")).Checked, "global switches save independently of an unfinished playlist range edit");
        File.WriteAllText(Path.Combine(root, "app-settings.xml"), "invalid");
        using (var form = Create(root))
            Check(!((CheckBox)Field(form, "backgroundMode")).Checked && !((CheckBox)Field(form, "timerEnabled")).Checked, "damaged settings fall back to safe defaults");
    }
    static void ClickRow(ListBox list, int index, bool shift)
    {
        var row = list.GetItemRectangle(index);
        var point = new IntPtr((row.Top + row.Height / 2) << 16 | 12);
        byte[] original = new byte[256]; GetKeyboardState(original);
        byte[] keys = (byte[])original.Clone(); keys[(int)Keys.ShiftKey] = shift ? (byte)128 : (byte)0;
        try {
            SetKeyboardState(keys);
            SendMessage(list.Handle, 0x201, new IntPtr(shift ? 5 : 1), point);
            SendMessage(list.Handle, 0x202, new IntPtr(shift ? 4 : 0), point);
        } finally { SetKeyboardState(original); }
    }
    static void CheckListSelection(string root)
    {
        Directory.CreateDirectory(root);
        using (var form = Create(root)) {
            form.Show();
            string[] names = { "games", "queue", "catalogResults" };
            for (int page = 0; page < names.Length; page++) {
                Call(form, "ShowPage", page);
                var list = (ListBox)Field(form, names[page]);
                list.Items.Clear();
                for (int i = 0; i < 80; i++) list.Items.Add(new PlaylistItem { Name = "Selection test " + i, Path = "test" + i + ".exe", Minutes = 1 });
                Pump(50);
                // Draw callbacks are authoritative even before the managed cache catches up.
                using (var bitmap = new Bitmap(list.Width, list.ItemHeight))
                using (var graphics = Graphics.FromImage(bitmap)) {
                    list.GetType().GetMethod("OnDrawItem", Flags).Invoke(list, new object[] { new DrawItemEventArgs(graphics, list.Font, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, DrawItemState.Selected) });
                    Check(bitmap.GetPixel(3, 3).ToArgb() == Color.FromArgb(53, 47, 79).ToArgb(), names[page] + " paints native selection before managed cache updates");
                }
                var painted = new System.Collections.Generic.Dictionary<int, bool>();
                list.DrawItem += delegate(object sender, DrawItemEventArgs e) { if (e.Index >= 0) painted[e.Index] = (e.State & DrawItemState.Selected) != 0; };
                list.Focus(); list.TopIndex = 0; ClickRow(list, 0, false);
                list.TopIndex = 79; Pump(30); painted.Clear(); ClickRow(list, 79, true); Pump(80);
                Check(list.SelectedIndices.Count == 80, names[page] + " native Shift-click selects first through last across scrolling");
                int top = list.TopIndex;
                Check(Enumerable.Range(top, 80 - top).All(i => painted.ContainsKey(i) && painted[i]), names[page] + " immediately repaints every visible selected row without hover");
                ClickRow(list, 79, false); Pump(50);
                Check(list.SelectedIndices.Count == 1 && Enumerable.Range(top, 79 - top).All(i => painted.ContainsKey(i) && !painted[i]), names[page] + " clears old range highlights on single selection");
                list.GetType().GetMethod("SelectAllItems").Invoke(list, null); Pump(50);
                Check(list.SelectedIndices.Count == 80, names[page] + " select all still works with buffered painting");
                list.TopIndex = 0; Pump(50);
                Render(form, names[page] + "-selection.png");
            }
            form.Close();
        }
    }
    [STAThread] static int Main()
    {
        try {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles(); decimal parsed;
            Check(MinutesSelector.TryParse("30s", out parsed) && parsed == 0.5m && MinutesSelector.TryParse("2h", out parsed) && parsed == 120 && MinutesSelector.TryParse("3", out parsed) && parsed == 3, "seconds, hours and default minutes parse");
            Check(MinutesSelector.TryParse("1s", out parsed) && parsed == 1m / 60m && !MinutesSelector.TryParse("oops", out parsed) && !MinutesSelector.TryParse("-1m", out parsed), "one second supported and invalid durations rejected");
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa-output", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            CheckTogglePersistence(Path.Combine(root, "toggle-settings"));
            CheckListSelection(Path.Combine(root, "selection-painting"));
            File.WriteAllText(Path.Combine(root, "games.tsv"), "Crab Game\tcrab game/crab game.exe\nUnturned\tunturned/unturned.exe\nUnsupported\t\n");
            var crab = new PlaylistItem { Name = "Crab Game", Path = "crab game/crab game.exe", Minutes = 1m / 60m };
            var unturned = new PlaylistItem { Name = "Unturned", Path = "unturned/unturned.exe", Minutes = 1m / 60m };
            string failedSaveRoot = Path.Combine(root, "failed-save"); Directory.CreateDirectory(Path.Combine(failedSaveRoot, "playlist.xml"));
            using (var form = Create(failedSaveRoot)) {
                Call(form, "AddItem", crab, 1m);
                Check(((Label)Field(form, "status")).Text.Contains("Playlist could not be saved"), "save failure is not replaced by an added-success message");
                Check(!Directory.GetFiles(failedSaveRoot, "*.tmp").Any(), "failed atomic save leaves no temporary file");
            }
            using (var form = Create(root)) {
                form.Show(); Pump(100);
                Check(form.ClientSize == new Size(560, 661), "default window size");
                var games = (ListBox)Field(form, "games"); var queue = (ListBox)Field(form, "queue");
                Check(games.Items.Count == 2, "unsupported entries excluded from library");
                games.SelectedIndex = 0; Range(form, "minutes", "invalid");
                Call(form, "PlaySelected");
                Check(Field(form, "active") == null && ((Label)Field(form, "status")).Text.Contains("Enter a duration"), "invalid manual duration is reported without an unhandled error");
                Range(form, "minutes", "10m");
                Call(form, "AddItem", crab, 1m / 60m); Call(form, "AddItem", crab, 2m);
                Check(queue.Items.Count == 1, "duplicate playlist adds are ignored");
                Call(form, "AddItem", new PlaylistItem { Name = "Crab alias", Path = "CRAB GAME\\CRAB GAME.EXE", Minutes = 1 }, 1m);
                Check(queue.Items.Count == 1, "same executable under another name cannot duplicate playlist game");
                games.SetSelected(0, true); games.SetSelected(1, true); Range(form, "minutes", "1s"); Call(form, "AddSelectedGames");
                Check(queue.Items.Count == 2, "batch add only adds new games");
                var originalOrder = queue.Items.Cast<PlaylistItem>().Select(item => item.Path).ToArray();
                Call(form, "ShufflePlaylist");
                Check((bool)Field(form, "shuffled") && (bool)Field(form, "shuffleButton").GetType().GetProperty("Selected").GetValue(Field(form, "shuffleButton"), null), "shuffle button toggles on");
                Call(form, "ShufflePlaylist");
                Check(!(bool)Field(form, "shuffled") && queue.Items.Cast<PlaylistItem>().Select(item => item.Path).SequenceEqual(originalOrder), "disabling shuffle restores the previous order");
                queue.ClearSelected(); queue.SetSelected(0, true); queue.SetSelected(1, true); Range(form, "editMinutes", "2s"); Call(form, "UpdateDuration");
                Check(queue.Items.Cast<PlaylistItem>().All(x => x.Minutes == 2m / 60m), "batch edit preserves second precision");
                // Exercise hover overlay positioning without sending OS pointer input.
                games.GetType().GetMethod("OnMouseMove", Flags).Invoke(games, new object[] { new MouseEventArgs(MouseButtons.None, 0, 15, 15, 0) });
                Check(games.Controls.OfType<Button>().Count(x => x.Visible) == 2, "playlist and trash icons appear on hovered row");
                Render(form, "library-hover.png");
                games.GetType().GetMethod("HideActions").Invoke(games, null);
                Check(games.Controls.OfType<Button>().All(x => !x.Visible), "row icons hide when hover ends");
                string export = Path.Combine(root, "export.tsv"); Call(form, "ExportGameList", export);
                Check(File.ReadAllLines(export).All(x => x.Split('\t').Length == 2) && !File.ReadAllText(export).Contains("Minutes"), "export includes only names and executable paths");
                string import = Path.Combine(root, "import.tsv"); File.WriteAllText(import, "No supported game\t\nNew game\tnew.exe\n"); Call(form, "ImportGameList", import);
                Check(games.Items.Count == 3 && games.Items.Cast<PlaylistItem>().All(x => x.Path.Length > 0), "imports cannot add unsupported library games");
                var remote = new[] { new DetectionCatalog.Entry { name = "Unsupported catalog title", executables = new DetectionCatalog.Executable[0] } };
                form.GetType().GetField("remoteCatalog", Flags).SetValue(form, remote);
                ((TextBox)Field(form, "catalogSearch")).Text = "Unsupported";
                var found = (ListBox)Field(form, "catalogResults"); found.SetSelected(0, true);
                Check(!((Button)Field(form, "addGamesButton")).Enabled, "unsupported-only selection disables add button");
                Call(form, "AddCatalogGames"); Check(games.Items.Count == 3, "unsupported catalog results cannot be added programmatically");
                // Manual timer preference stays off while playlist always counts down.
                Toggle(form, "backgroundMode", true); Call(form, "PlayNow", crab);
                Check(Until(() => Field(form, "active") != null && GameWindow((Process)Field(form, "active"), false), 3000), "background launch keeps game window visible to Windows but off every screen");
                var worker = (Process)Field(form, "active");
                Toggle(form, "backgroundMode", false); Check(Until(() => GameWindow(worker, true), 1500), "background switch can reveal running game window");
                Toggle(form, "backgroundMode", true); Check(Until(() => GameWindow(worker, false), 1500), "background switch moves running window off-screen without hiding it");
                Call(form, "StopAll", "Stopped"); string crabTarget = PlaceholderPaths.Resolve(root, crab.Path);
                Check(File.Exists(crabTarget), "generated executable retained after Stop within session");
                DateTime stamp = File.GetLastWriteTimeUtc(crabTarget); Call(form, "PlayNow", crab); Call(form, "StopAll", "Stopped");
                Check(File.GetLastWriteTimeUtc(crabTarget) == stamp, "restarting same game reuses cached executable");
                Toggle(form, "randomPlay", true); Range(form, "playMin", "1s"); Range(form, "playMax", "2s");
                Toggle(form, "randomPause", true); Range(form, "pauseMin", "1s"); Range(form, "pauseMax", "1s");
                Call(form, "ShowPage", 1); Render(form, "playlist-ranges.png"); Call(form, "StartPlaylist");
                decimal sampled = (decimal)Field(form, "activeMinutes"); Check(sampled >= 1m / 60m && sampled <= 2m / 60m, "random playtime lies inside requested range");
                Toggle(form, "timerEnabled", true); Toggle(form, "timerEnabled", false);
                Check(Until(() => (bool)Field(form, "inPause"), 3500), "playlist timer advances into random pause with Library timer off");
                Check(Field(form, "active") == null && ((Label)Field(form, "remaining")).Text.Contains("pause"), "pause has no game process and shows countdown");
                Render(form, "playlist-pause.png");
                Check(Until(() => !(bool)Field(form, "inPause") && Field(form, "active") != null, 2500), "next game launches after pause");
                Check(((PlaylistRun)Field(form, "run")).Index == 1, "pause resumes correct next entry");
                Check(Until(() => Field(form, "run") == null, 3500), "playlist completes after last game without an extra pause");
                Check(Directory.Exists(Path.Combine(root, "SpoofedGames")), "SpoofedGames folder retained after playlist completion");
                Toggle(form, "repeat", true); Call(form, "StartPlaylist");
                Check(Until(() => (bool)Field(form, "inPause"), 3500), "repeat run enters pause"); Call(form, "Advance");
                Check(!(bool)Field(form, "inPause") && Field(form, "active") != null, "Next skips active pause");
                Check(Until(() => (bool)Field(form, "inPause") && ((PlaylistRun)Field(form, "run")).Index == 0, 3500), "repeat pauses between final and first game");
                Call(form, "StopAll", "Stopped");
                Range(form, "playMin", "3s"); Range(form, "playMax", "1s"); Call(form, "StartPlaylist");
                Check(Field(form, "active") == null && ((Label)Field(form, "status")).Text.Contains("minimum"), "invalid random range fails inline");
                Range(form, "playMin", "1s"); Range(form, "playMax", "2s"); Call(form, "SavePlaylist");
                var before = File.ReadAllText(Path.Combine(root, "games.tsv"));
                File.WriteAllText(import, "Safe\tsafe.exe\nBad\t../escape.exe\n"); bool rejected = false; try { Call(form, "ImportGameList", import); } catch (TargetInvocationException) { rejected = true; }
                Check(rejected && File.ReadAllText(Path.Combine(root, "games.tsv")) == before, "invalid import does not change library");
                games.ClearSelected(); games.SetSelected(0, true); Confirm(form, "RemoveLibraryGames", false); Check(games.Items.Count == 3, "cancel removal keeps library");
                form.ClientSize = new Size(700, 600); form.Close();
            }
            string cachedCrab = PlaceholderPaths.Resolve(root, crab.Path);
            Check(File.Exists(cachedCrab) && Directory.Exists(Path.Combine(root, "SpoofedGames")), "closing app preserves generated executables in SpoofedGames");
            DateTime cachedStamp = File.GetLastWriteTimeUtc(cachedCrab);
            using (var form = Create(root)) {
                form.Show(); Pump(50);
                Check(form.ClientSize == new Size(700, 600), "saved resized dimensions restore");
                Call(form, "PlayNow", crab);
                Check(Field(form, "active") != null && File.GetLastWriteTimeUtc(cachedCrab) == cachedStamp, "reopening app reuses existing executable without rewriting it");
                Call(form, "StopAll", "Stopped");
                Check(((CheckBox)Field(form, "randomPlay")).Checked && ((CheckBox)Field(form, "randomPause")).Checked && ((ListBox)Field(form, "queue")).Items.Count == 2, "timing options and unique playlist persist");
                // Import an older playlist with duplicate entries.
                var old = new SavedPlaylist { Items = new System.Collections.Generic.List<PlaylistItem> { crab, crab.Copy(), unturned } };
                string xml = Path.Combine(root, "legacy.xml"); using (var writer = File.Create(xml)) new System.Xml.Serialization.XmlSerializer(typeof(SavedPlaylist)).Serialize(writer, old);
                Call(form, "ImportGameList", xml); Check(((ListBox)Field(form, "queue")).Items.Count == 2, "playlist import removes duplicate games");
                string beforeInvalidImport = File.ReadAllText(Path.Combine(root, "playlist.xml"));
                old.PlayMin = -1; old.RandomPlay = false;
                using (var writer = File.Create(xml)) new System.Xml.Serialization.XmlSerializer(typeof(SavedPlaylist)).Serialize(writer, old);
                bool invalidTimingRejected = false;
                try { Call(form, "ImportGameList", xml); } catch (TargetInvocationException e) { invalidTimingRejected = e.InnerException is ArgumentException; }
                Check(invalidTimingRejected && File.ReadAllText(Path.Combine(root, "playlist.xml")) == beforeInvalidImport && ((ListBox)Field(form, "queue")).Items.Count == 2, "invalid disabled timing range is rejected before import changes saved playlist");
                var finalQueue = (ListBox)Field(form, "queue");
                finalQueue.GetType().GetMethod("OnKeyDown", Flags).Invoke(finalQueue, new object[] { new KeyEventArgs(Keys.Control | Keys.A) });
                finalQueue.GetType().GetMethod("OnKeyDown", Flags).Invoke(finalQueue, new object[] { new KeyEventArgs(Keys.Delete) });
                Check(finalQueue.Items.Count == 0, "Ctrl+A followed by Delete removes all selected playlist games");
                form.Close();
            }
            Console.WriteLine("All checks passed."); return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
