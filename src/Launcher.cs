using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Xml.Serialization;

sealed class Launcher : Form
{
    readonly string root;
    readonly List<PlaylistItem> catalog = new List<PlaylistItem>();
    readonly List<PlaylistItem> localLibrary = new List<PlaylistItem>();
    readonly CatalogLibrary libraryStore;
    readonly Button refreshButton;
    readonly Label lastUpdated = new Label();
    readonly List<PlaylistItem> playlist = new List<PlaylistItem>();
    readonly ListBox games = new DarkList(), queue = new DarkList();
    readonly TextBox search = new TextBox(), catalogSearch = new TextBox();
    readonly ListBox catalogResults = new DarkList();
    readonly Label catalogInfo = new Label();
    readonly Button addGamesButton;
    readonly Button addAllPlaylistButton;
    DetectionCatalog.Entry[] remoteCatalog;
    readonly MinutesSelector minutes = new MinutesSelector(), editMinutes = new MinutesSelector();
    readonly CheckBox repeat = new RepeatSwitch();
    readonly CheckBox timerEnabled = new RepeatSwitch(), backgroundMode = new RepeatSwitch();
    readonly CheckBox randomPlay = new RepeatSwitch(), randomPause = new RepeatSwitch();
    readonly MinutesSelector playMin = new MinutesSelector(), playMax = new MinutesSelector(), pauseMin = new MinutesSelector(), pauseMax = new MinutesSelector();
    readonly Random random = new Random();
    readonly HashSet<string> verifiedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    byte[] sourceHash;
    SavedPlaylist runTiming;
    bool inPause;
    decimal pauseMinutes;
    readonly ToolTip hints = new DarkToolTip();
    static readonly Size DefaultClientSize = new Size(560, 661);
    Size normalClientSize = DefaultClientSize;
    bool lastMaximized;
    readonly Label status = new Label(), activity = new Label(), remaining = new Label(), queueInfo = new Label();
    readonly Button stopButton, nextButton;
    readonly Button removePlaylistButton;
    readonly Button shuffleButton;
    readonly List<string> originalOrder = new List<string>();
    bool shuffled;
    readonly Timer timer = new Timer();
    readonly Stopwatch clock = new Stopwatch();
    readonly Color muted = Theme.Muted, surface = Theme.Panel, accent = Theme.Accent;
    readonly List<Panel> pages = new List<Panel>();
    readonly List<ActionButton> navigation = new List<ActionButton>();
    readonly Panel content = new Panel();
    readonly Label libraryCount = new Label();
    Process active;
    PlaylistRun run;
    decimal activeMinutes;
    bool loading;

    public Launcher() : this(AppDomain.CurrentDomain.BaseDirectory) { Shown += delegate { if (libraryStore.NeedsRefresh) RefreshCatalog(); }; }
    internal Launcher(string directory)
    {
        root = directory; libraryStore = new CatalogLibrary(directory);
        Text = "GameSpoof"; StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = DefaultClientSize; MinimumSize = new Size(576, 539);
        Font = new Font("Segoe UI", 9);
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Icon = Theme.AppIcon(); DoubleBuffered = true;
        var shell = Grid(1, 5); shell.Padding = new Padding(14);
        Rows(shell, 52, 64, 40, -1, 34); Controls.Add(shell);
        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(32,32), Location = new Point(0,1) });
        header.Controls.Add(new Label { Text = "GameSpoof", AutoSize = true, Location = new Point(42, 0), Font = new Font("Segoe UI", 16, FontStyle.Bold) });
        header.Controls.Add(new Label { Text = "Game activity & playlists", AutoSize = true, Location = new Point(44,30), ForeColor = muted, Font = new Font("Segoe UI", 8) });
        backgroundMode.Text = "Play in background"; backgroundMode.Size = new Size(178, 32); backgroundMode.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Resize += delegate { backgroundMode.Location = new Point(header.Width - backgroundMode.Width, 4); };
        header.Controls.Add(backgroundMode);
        backgroundMode.CheckedChanged += delegate { SaveAppSettings(); ActivityMessages.SetBackground(active, backgroundMode.Checked); };
        shell.Controls.Add(header);
        var nowCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8), Margin = Padding.Empty };
        var now = Grid(2, 1);
        now.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); now.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        var nowText = Grid(1, 2); Rows(nowText, 24, -1);
        activity.Text = "Ready to play"; activity.Font = new Font(Font, FontStyle.Bold); activity.Dock = DockStyle.Fill; activity.AutoEllipsis = true;
        remaining.Dock = DockStyle.Fill; remaining.ForeColor = muted; remaining.Text = "Choose a game or start your playlist.";
        nowText.Controls.Add(activity); nowText.Controls.Add(remaining); now.Controls.Add(nowText);
        timerEnabled.Text = ""; timerEnabled.AccessibleName = "Enable countdown timer"; hints.SetToolTip(timerEnabled, "Enable or pause the countdown timer"); timerEnabled.Dock = DockStyle.Fill; timerEnabled.Checked = false;
        timerEnabled.CheckedChanged += delegate {
            SaveAppSettings();
            if (active == null || run != null) return;
            if (timerEnabled.Checked) clock.Start(); else clock.Stop();
            UpdateCountdown();
        };
        stopButton = Button("Stop", delegate { StopAll("Stopped."); }, false); stopButton.Enabled = false; now.Controls.Add(stopButton);
        nowCard.Controls.Add(now); shell.Controls.Add(nowCard);
        var nav = Grid(3, 1); nav.Margin = new Padding(0, 5, 0, 4);
        for (int i = 0; i < 3; i++) {
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f)); int index = i;
            var button = (ActionButton)Button(new[] { "Library", "Playlist", "Add games" }[i], delegate { ShowPage(index); }, false);
            button.Margin = new Padding(i == 0 ? 0 : 5, 0, 0, 0); navigation.Add(button); nav.Controls.Add(button);
        }
        shell.Controls.Add(nav);
        content.Dock = DockStyle.Fill; shell.Controls.Add(content);
        status.Dock = DockStyle.Fill; status.ForeColor = muted; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true;
        shell.Controls.Add(status);
        var library = Page("Library"); var lib = Grid(1, 4); Rows(lib, 24, 36, -1, 40); library.Controls.Add(lib);
        libraryCount.Dock = DockStyle.Fill; libraryCount.ForeColor = Theme.Text; libraryCount.Font = new Font(Font, FontStyle.Bold);
        lib.Controls.Add(libraryCount); lib.Controls.Add(InputSurface(search)); search.AccessibleName = "Search games";
        search.HandleCreated += delegate { Theme.SearchHint(search); };
        StyleList(games, false); games.SelectionMode = SelectionMode.MultiExtended; lib.Controls.Add(new BufferedListPanel(games));
        ((DarkList)games).RowActions = true;
        ((DarkList)games).RowAction += delegate(int index, bool remove) {
            if (index < 0 || index >= games.Items.Count) return;
            if (!games.GetSelected(index)) { games.ClearSelected(); games.SetSelected(index, true); }
            if (remove) RemoveLibraryGames(); else AddSelectedGames();
        };
        var libActions = Grid(3, 1);
        libActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42)); libActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); libActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        libActions.Controls.Add(timerEnabled); Number(minutes); libActions.Controls.Add(minutes);
        libActions.Controls.Add(Button("Play now", delegate { PlaySelected(); }, true));
        minutes.ValueCommitted += delegate {
            if (active != null && run == null) { activeMinutes = minutes.Value; UpdateCountdown(); }
            Notice("Duration set to " + minutes.InputText + ".");
        };
        lib.Controls.Add(libActions);
        games.DoubleClick += delegate { if (games.SelectedItem != null) PlaySelected(); };
        games.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.A) { ((DarkList)games).SelectAllItems(); e.SuppressKeyPress = true; } if (e.KeyCode == Keys.Enter && games.SelectedItem != null) { PlaySelected(); e.SuppressKeyPress = true; } };
        search.TextChanged += delegate { Filter(); };

        var playlistPage = Page("Playlist"); var pl = Grid(1, 5); Rows(pl, 26, -1, 38, 38, 38); playlistPage.Controls.Add(pl);
        queueInfo.Dock = DockStyle.Fill; queueInfo.ForeColor = muted; pl.Controls.Add(queueInfo);
        hints.SetToolTip(queueInfo, "Ctrl+A selects all playlist games. Delete removes the selected games.");
        shuffleButton = Button("", ShufflePlaylist, false); ((ActionButton)shuffleButton).ShuffleIcon = true;
        shuffleButton.AccessibleName = "Shuffle playlist";
        hints.SetToolTip(shuffleButton, "Toggle shuffle. Turn off to restore the previous order. Changes apply on the next start.");
        StyleList(queue, true); queue.SelectionMode = SelectionMode.MultiExtended; pl.Controls.Add(new BufferedListPanel(queue));
        queue.KeyDown += delegate(object sender, KeyEventArgs e) {
            if (e.Control && e.KeyCode == Keys.A) { ((DarkList)queue).SelectAllItems(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Delete) { RemoveItem(); e.SuppressKeyPress = true; }
        };
        var ranges = Grid(8, 1);
        foreach (int width in new[] { 94, 72, 18, 72, 86, 62, 18 }) ranges.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        ranges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        randomPlay.Text = "Random"; randomPause.Text = "Pause"; randomPlay.Dock = randomPause.Dock = DockStyle.Fill;
        ranges.Controls.Add(randomPlay); Number(playMin); playMin.Value = 5; ranges.Controls.Add(playMin); ranges.Controls.Add(Label("–")); Number(playMax); playMax.Value = 15; ranges.Controls.Add(playMax);
        ranges.Controls.Add(randomPause); Number(pauseMin); pauseMin.Minimum = 0; pauseMin.Value = 10m / 60m; ranges.Controls.Add(pauseMin); ranges.Controls.Add(Label("–")); Number(pauseMax); pauseMax.Minimum = 0; pauseMax.Value = 30m / 60m; ranges.Controls.Add(pauseMax);
        hints.SetToolTip(randomPlay, "Random playtime between the two durations, instead of the entry's duration"); hints.SetToolTip(randomPause, "Random pause between games; no game process runs during the pause");
        hints.SetToolTip(playMin, "Minimum playtime (s / m / h)"); hints.SetToolTip(playMax, "Maximum playtime (s / m / h)"); hints.SetToolTip(pauseMin, "Minimum pause (s / m / h)"); hints.SetToolTip(pauseMax, "Maximum pause (s / m / h)");
        EventHandler commitTiming = delegate {
            if (loading) return;
            if (SavePlaylist()) Notice("Playlist timing saved. Changes apply on the next start.");
        };
        foreach (var input in new[] { playMin, playMax, pauseMin, pauseMax }) {
            input.ValueChanged += delegate {
                if (loading) return;
                // Intermediate edits can temporarily invert a range (e.g. 1 -> 15).
                try { ValidateTiming(CurrentTiming()); } catch (ArgumentException) { return; }
                SavePlaylist();
            };
            input.ValueCommitted += commitTiming;
            input.Leave += commitTiming;
        }
        randomPlay.CheckedChanged += delegate { if (!loading) SavePlaylist(); }; randomPause.CheckedChanged += delegate { if (!loading) SavePlaylist(); };
        pl.Controls.Add(ranges);
        var edit = Grid(7, 1);
        foreach (int width in new[] { 112, 62, 43, 50 }) edit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        edit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        edit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); edit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        Number(editMinutes); edit.Controls.Add(editMinutes);
        editMinutes.ValueCommitted += delegate { try { UpdateDuration(); } catch (Exception e) { Error(e.Message); } };
        edit.Controls.Add(Button("Apply", delegate { UpdateDuration(); }, false));
        edit.Controls.Add(Button("Up", delegate { MoveItem(-1); }, false));
        edit.Controls.Add(Button("Down", delegate { MoveItem(1); }, false));
        edit.Controls.Add(shuffleButton);
        edit.Controls.Add(new Panel { Dock = DockStyle.Fill });
        removePlaylistButton = Button("Remove", delegate { RemoveItem(); }, false); ((ActionButton)removePlaylistButton).Danger = true; edit.Controls.Add(removePlaylistButton); pl.Controls.Add(edit);
        var playback = Grid(3, 1);
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36)); playback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22)); playback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        repeat.Text = "Repeat"; repeat.Dock = DockStyle.Fill; repeat.CheckedChanged += delegate { if (!loading) SavePlaylist(); }; playback.Controls.Add(repeat);
        nextButton = Button("Next", delegate { Advance(); }, false); nextButton.Enabled = false; playback.Controls.Add(nextButton);
        playback.Controls.Add(Button("Start playlist", delegate { StartPlaylist(); }, true)); pl.Controls.Add(playback);
        queue.SelectedIndexChanged += delegate { var item = queue.SelectedItem as PlaylistItem; if (item != null) editMinutes.Value = item.Minutes; removePlaylistButton.Enabled = queue.SelectedIndices.Count > 0; };

        var addPage = Page("Add games"); var fields = Grid(1, 5); Rows(fields, 36, 24, -1, 36, 36); addPage.Controls.Add(fields);
        var findRow = Grid(2, 1);
        findRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); findRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        catalogSearch.AccessibleName = "Search Discord game catalog";
        catalogSearch.HandleCreated += delegate { Theme.SearchHint(catalogSearch); };
        findRow.Controls.Add(InputSurface(catalogSearch));
        refreshButton = Button("Refresh Game Catalogue", delegate { RefreshCatalog(); }, false); findRow.Controls.Add(refreshButton); fields.Controls.Add(findRow);
        var catalogHeading = Grid(2, 1); catalogHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); catalogHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        catalogInfo.Text = "Type a game name to search."; catalogInfo.Dock = DockStyle.Fill; catalogInfo.ForeColor = muted; catalogInfo.AutoEllipsis = true; catalogHeading.Controls.Add(catalogInfo);
        lastUpdated.ForeColor = muted; lastUpdated.Font = new Font("Segoe UI", 7.5f); lastUpdated.TextAlign = ContentAlignment.MiddleRight; lastUpdated.AutoEllipsis = true; lastUpdated.Dock = DockStyle.Fill; catalogHeading.Controls.Add(lastUpdated); fields.Controls.Add(catalogHeading);
        StyleList(catalogResults, false); catalogResults.SelectionMode = SelectionMode.MultiExtended; fields.Controls.Add(new BufferedListPanel(catalogResults));
        ((DarkList)catalogResults).HasRowInfo = index => string.IsNullOrEmpty(((PlaylistItem)catalogResults.Items[index]).Path);
        ((DarkList)catalogResults).RowInfo += delegate(int index) {
            using (var dialog = new DarkInformationDialog(((PlaylistItem)catalogResults.Items[index]).Name)) dialog.ShowDialog(this);
        };
        var addActions = Grid(2, 1); addActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); addActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        addGamesButton = Button("Add selected games", delegate { AddCatalogGames(); }, true); addGamesButton.Enabled = false; addActions.Controls.Add(addGamesButton);
        addAllPlaylistButton = Button("Add EVERYTHING to playlist", AddAllCatalogToPlaylist, false); addActions.Controls.Add(addAllPlaylistButton); fields.Controls.Add(addActions);
        hints.SetToolTip(addAllPlaylistButton, "Add the entire supported catalogue directly to Playlist, with 5s per new game. Skips duplicates and games without executable paths. Library stays unchanged.");
        catalogResults.SelectedIndexChanged += delegate { addGamesButton.Enabled = catalogResults.SelectedItems.Cast<PlaylistItem>().Any(x => !string.IsNullOrEmpty(x.Path)); };
        catalogResults.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.A) { ((DarkList)catalogResults).SelectAllItems(); e.SuppressKeyPress = true; } };
        catalogSearch.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { if (remoteCatalog != null) FilterCatalogGames(); e.SuppressKeyPress = true; } };
        catalogSearch.TextChanged += delegate { if (remoteCatalog != null) FilterCatalogGames(); else catalogInfo.Text = "Refresh the catalogue to search."; };
        var transfer = Grid(2, 1);
        transfer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); transfer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        transfer.Controls.Add(Button("Import game list", delegate { ImportListDialog(); }, false));
        transfer.Controls.Add(Button("Export game list", delegate { ExportListDialog(); }, false)); fields.Controls.Add(transfer);
        LoadCatalog(); LoadPlaylist(); LoadAppSettings(); Filter(); RefreshQueue(-1); ShowPage(0);
        timer.Interval = 250; timer.Tick += delegate { TickPlayback(); }; timer.Start();
        RestoreWindowSize();
        Resize += delegate {
            if (WindowState == FormWindowState.Normal) { normalClientSize = ClientSize; lastMaximized = false; }
            else if (WindowState == FormWindowState.Maximized) lastMaximized = true;
        };
        FormClosing += delegate { SaveWindowSize(); };
        FormClosed += delegate { StopAll("Stopped."); };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); hints.Dispose(); if (active != null) StopProcess(); }
        base.Dispose(disposing);
    }

    void LoadAppSettings()
    {
        loading = true;
        try {
            var settings = AppSettings.Load(root);
            backgroundMode.Checked = settings.PlayInBackground;
            timerEnabled.Checked = settings.CountdownEnabled;
        } catch (Exception e) { Error("Could not load app settings: " + e.Message); }
        finally { loading = false; }
    }

    void SaveAppSettings()
    {
        if (loading) return;
        try {
            new AppSettings { PlayInBackground = backgroundMode.Checked, CountdownEnabled = timerEnabled.Checked }.Save(root);
        } catch (Exception e) { Error("Could not save app settings: " + e.Message); }
    }

    void RestoreWindowSize()
    {
        try {
            string path = Path.Combine(root, "window-size.txt");
            if (!File.Exists(path)) return;
            string[] values = File.ReadAllLines(path); int width, height; bool maximized;
            if (values.Length != 3 || !int.TryParse(values[0], out width) || !int.TryParse(values[1], out height) || !bool.TryParse(values[2], out maximized)) return;
            var area = Screen.FromControl(this).WorkingArea;
            ClientSize = new Size(Math.Max(560, Math.Min(width, area.Width - (Width - ClientSize.Width))), Math.Max(500, Math.Min(height, area.Height - (Height - ClientSize.Height))));
            normalClientSize = ClientSize; lastMaximized = maximized;
            if (maximized) Shown += delegate { WindowState = FormWindowState.Maximized; };
        } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    void SaveWindowSize()
    {
        try {
            AtomicFile.Write(Path.Combine(root, "window-size.txt"), delegate(Stream stream) {
                using (var writer = new StreamWriter(stream)) { writer.WriteLine(normalClientSize.Width); writer.WriteLine(normalClientSize.Height); writer.WriteLine(lastMaximized); }
            });
        } catch (Exception e) { Error("Could not save window size: " + e.Message); }
    }

    TableLayoutPanel Grid(int columns, int rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty };
        if (rows == 1) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return grid;
    }
    void Rows(TableLayoutPanel grid, params int[] heights)
    { foreach (int height in heights) grid.RowStyles.Add(new RowStyle(height < 0 ? SizeType.Percent : SizeType.Absolute, height < 0 ? 100 : height)); }
    Panel Page(string text)
    {
        var page = new Card { Dock = DockStyle.Fill, Padding = new Padding(12), Visible = false };
        page.AccessibleName = text; pages.Add(page); content.Controls.Add(page); return page;
    }
    internal void ShowPage(int index)
    {
        for (int i = 0; i < pages.Count; i++) { pages[i].Visible = i == index; navigation[i].Selected = i == index; navigation[i].Invalidate(); }
        pages[index].BringToFront();
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.Window(this); }

    Label Label(string text)
    { return new Label { Text = text, AutoEllipsis = true, Margin = Padding.Empty, Dock = DockStyle.Fill, ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft }; }
    Button Button(string text, Action action, bool primary)
    {
        var button = new ActionButton { Primary = primary, Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = primary ? accent : surface, ForeColor = ForeColor, Cursor = Cursors.Hand, Margin = new Padding(3, 3, 0, 3) };
        button.FlatAppearance.BorderSize = 0; button.Click += delegate { try { action(); } catch (Exception e) { Error(e.Message); } }; return button;
    }
    Control InputSurface(TextBox box)
    {
        var panel = new Card { Dock = DockStyle.Fill, BackColor = Theme.Input, Padding = new Padding(9, 6, 9, 5), Margin = new Padding(0, 0, 0, 4) };
        box.Dock = DockStyle.Fill; box.BackColor = Theme.Input; box.ForeColor = ForeColor; box.BorderStyle = BorderStyle.None;
        panel.Controls.Add(box); return panel;
    }
    void Number(MinutesSelector box)
    { box.Minimum = 1m / 60m; box.Maximum = 10080; box.Value = 10; box.Dock = DockStyle.Fill; box.ForeColor = ForeColor; box.Margin = new Padding(0, 4, 3, 3); box.AccessibleName = "Duration (s, m, h; default minutes)"; }
    void StyleList(ListBox list, bool durations)
    {
        list.Dock = DockStyle.Fill; list.BackColor = surface; list.ForeColor = ForeColor; list.BorderStyle = BorderStyle.None;
        list.IntegralHeight = false; list.DrawMode = DrawMode.OwnerDrawFixed; list.ItemHeight = durations ? 28 : 44; list.Margin = new Padding(0, 3, 0, 3);
        list.DrawItem += delegate(object sender, DrawItemEventArgs e) {
            if (e.Index < 0) return;
            Color rowColor = ((DarkList)list).RowBackColor(e.Index, (e.State & DrawItemState.Selected) != 0);
            using (var brush = new SolidBrush(rowColor)) e.Graphics.FillRectangle(brush, e.Bounds);
            var item = (PlaylistItem)list.Items[e.Index];
            int inset = durations ? 104 : list == games ? 92 : list == catalogResults && string.IsNullOrEmpty(item.Path) ? 52 : 16;
            TextRenderer.DrawText(e.Graphics, (durations ? (e.Index + 1) + ".  " : "") + item.Name, Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - inset, durations ? e.Bounds.Height : 23), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (!durations) TextRenderer.DrawText(e.Graphics, string.IsNullOrEmpty(item.Path) ? "No supported Windows executable" : list == catalogResults ? (catalog.Any(x => x.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(x.Path)) ? "Already in library  /  " : "Ready to add  /  ") + item.Path : "Added " + item.DateAdded + "  /  " + item.Path, Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 23, e.Bounds.Width - inset, 19), muted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (durations) TextRenderer.DrawText(e.Graphics, MinutesSelector.Format(item.Minutes), Font, new Rectangle(e.Bounds.Right - 90, e.Bounds.Y, 80, e.Bounds.Height), muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        };
    }
    void Notice(string message) { status.ForeColor = muted; status.Text = message; }
    void Error(string message) { status.ForeColor = Color.FromArgb(255, 159, 163); status.Text = message; }
    void Validate(PlaylistItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Name) || item.Name.Contains('"') || item.Name.Any(char.IsControl)) throw new ArgumentException("Enter a valid game name.");
        if (string.IsNullOrWhiteSpace(item.Path)) throw new ArgumentException("This game has no executable match yet. Import a verified path when available.");
        PlaceholderPaths.Resolve(root, item.Path);
        if (item.Minutes < 1m / 60m || item.Minutes > 10080m) throw new ArgumentException("Duration must be one second to seven days.");
    }
    void LoadCatalog()
    {
        try {
            string path = Path.Combine(root, "games.tsv");
            if (File.Exists(path)) localLibrary.AddRange(ReadLibrary(path));
            foreach (var item in localLibrary) if (string.IsNullOrEmpty(item.DateAdded)) item.DateAdded = DateTime.Today.ToString("yyyy-MM-dd");
            RebuildLibrary();
        } catch (Exception e) { Error("Could not load library: " + e.Message); }
        remoteCatalog = libraryStore.HasCache ? libraryStore.Entries : null;
        UpdateCatalogDate();
        if (libraryStore.HasCache) catalogInfo.Text = remoteCatalog.Length.ToString("N0") + " games saved offline.";
        if (libraryStore.LoadError != null) Error(libraryStore.LoadError);
    }
    void RebuildLibrary() { catalog.Clear(); catalog.AddRange(libraryStore.Build(localLibrary)); }
    void UpdateCatalogDate()
    {
        addAllPlaylistButton.Enabled = remoteCatalog != null && remoteCatalog.Length > 0;
        lastUpdated.Text = libraryStore.UpdatedUtc.HasValue ? "Updated " + libraryStore.UpdatedUtc.Value.ToLocalTime().ToString("dd MMM, HH:mm") : "Not refreshed yet";
    }
    async void RefreshCatalog()
    {
        if (!refreshButton.Enabled) return;
        refreshButton.Enabled = false; refreshButton.Text = "Refreshing";
        Notice("Downloading the game catalog...");
        try {
            await System.Threading.Tasks.Task.Run(() => libraryStore.Refresh());
            if (IsDisposed || Disposing) return;
            remoteCatalog = libraryStore.Entries; RebuildLibrary(); Filter(); FilterCatalogGames(); UpdateCatalogDate();
            Notice("Catalog refreshed: " + remoteCatalog.Length.ToString("N0") + " games available offline in Add games.");
        } catch (Exception e) { if (!IsDisposed && !Disposing) Error("Refresh failed; saved games remain available. " + e.Message); }
        finally { if (!refreshButton.IsDisposed) { refreshButton.Enabled = true; refreshButton.Text = "Refresh Game Catalogue"; } }
    }

    List<PlaylistItem> ReadLibrary(string path)
    {
        var items = new List<PlaylistItem>(); int line = 0;
        foreach (string row in File.ReadAllLines(path)) {
            line++; if (row.StartsWith("#") || string.IsNullOrWhiteSpace(row)) continue;
            string[] parts = row.Split('\t'); if (parts.Length < 2 || parts.Length > 3) throw new FormatException("Line " + line + ": expected game name, tab, executable path.");
            var item = new PlaylistItem { Name = parts[0].Trim(), Path = parts[1].Trim(), Minutes = 10 };
            if (parts.Length == 3 && parts[2].Length > 0) {
                DateTime date; if (!DateTime.TryParseExact(parts[2], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date)) throw new FormatException("Line " + line + ": invalid date added.");
                item.DateAdded = parts[2];
            }
            if (item.Path.Length > 0) Validate(item);
            else if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Contains('"') || item.Name.Any(char.IsControl)) throw new FormatException("Invalid game name.");
            if (!string.IsNullOrEmpty(item.Path)) items.Add(item);
        }
        return items;
    }
    SavedPlaylist ReadPlaylist(string path)
    {
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
        SavedPlaylist saved;
        using (var reader = System.Xml.XmlReader.Create(path, settings)) saved = (SavedPlaylist)new XmlSerializer(typeof(SavedPlaylist)).Deserialize(reader);
        if (saved == null || saved.Items == null) throw new FormatException("The playlist is missing its game list.");
        foreach (var item in saved.Items) Validate(item);
        saved.Items = UniqueGames(saved.Items).ToList();
        ValidateTiming(saved);
        return saved;
    }
    static void WriteLibrary(string path, IEnumerable<PlaylistItem> items)
    {
        AtomicFile.Write(path, delegate(Stream stream) {
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) {
                writer.WriteLine("# GameSpoof library: game name<TAB>relative executable path<TAB>date added (yyyy-MM-dd)");
                foreach (var item in items) writer.WriteLine(item.Name + "\t" + item.Path + "\t" + item.DateAdded);
            }
        });
    }
    static void WritePlaylist(string path, SavedPlaylist saved)
    {
        AtomicFile.Write(path, delegate(Stream stream) { new XmlSerializer(typeof(SavedPlaylist)).Serialize(stream, saved); });
    }
    void ImportListDialog()
    {
        using (var dialog = new OpenFileDialog { Title = "Import game library or playlist", Filter = "Game lists (*.tsv;*.xml)|*.tsv;*.xml|Game library (*.tsv)|*.tsv|Playlist (*.xml)|*.xml", CheckFileExists = true }) {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { ImportGameList(dialog.FileName); } catch (Exception e) { Error("Import failed: " + e.Message); }
        }
    }
    void ExportListDialog()
    {
        using (var dialog = new SaveFileDialog { Title = "Export game library", Filter = "Game library (*.tsv)|*.tsv", FileName = "games.tsv", AddExtension = true, OverwritePrompt = true }) {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { ExportGameList(dialog.FileName); } catch (Exception e) { Error("Export failed: " + e.Message); }
        }
    }
    internal void ImportGameList(string path)
    {
        if (Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase)) {
            var saved = ReadPlaylist(path);
            WritePlaylist(Path.Combine(root, "playlist.xml"), saved);
            playlist.Clear(); playlist.AddRange(saved.Items);
            ApplyTiming(saved);
            RefreshQueue(); Notice("Imported " + playlist.Count + " playlist entries. Start playlist to play them.");
        } else {
            var incoming = ReadLibrary(path);
            if (incoming.Count == 0) throw new FormatException("The game list contains no games.");
            var merged = localLibrary.Select(x => x.Copy()).ToList();
            int added = 0, updated = 0, skipped = 0;
            var existingTitles = catalog.GroupBy(x => DetectionCatalog.TitleKey(x.Name)).ToDictionary(x => x.Key, x => x.First());
            foreach (var item in incoming) {
                PlaylistItem existing; existingTitles.TryGetValue(DetectionCatalog.TitleKey(item.Name), out existing);
                if (existing != null && existing.Path.Replace('\\', '/').Equals(item.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                int index = merged.FindIndex(x => x.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(item.DateAdded)) item.DateAdded = index < 0 ? DateTime.Today.ToString("yyyy-MM-dd") : merged[index].DateAdded;
                if (index < 0) { if (existing != null) { item.DateAdded = existing.DateAdded; updated++; } else added++; merged.Add(item); }
                else if (string.IsNullOrEmpty(item.Path) || item.Path.Replace('\\', '/').Equals(merged[index].Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) { skipped++; }
                else { item.Name = merged[index].Name; merged[index] = item; updated++; }
            }
            merged.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            if (added > 0 || updated > 0) {
                WriteLibrary(Path.Combine(root, "games.tsv"), merged);
                localLibrary.Clear(); localLibrary.AddRange(merged);
            }
            libraryStore.SetHidden(incoming, false); RebuildLibrary(); Filter();
            Notice("Added " + added + " / Updated " + updated + " / Skipped " + skipped + " duplicates or empty updates.");
        }
    }
    internal void ExportGameList(string path)
    {
        AtomicFile.Write(path, delegate(Stream stream) { using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) foreach (var item in catalog.Where(x => !string.IsNullOrEmpty(x.Path))) writer.WriteLine(item.Name + "\t" + item.Path); });
        Notice("Game names and executable paths exported.");
    }
    void Filter()
    {
        var match = libraryStore.Matcher(search.Text);
        var visible = catalog.Where(match).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        ((DarkList)games).ResetHover(); games.BeginUpdate();
        try { games.Items.Clear(); games.Items.AddRange(visible); } finally { games.EndUpdate(); }
        libraryCount.Text = "Game library" + " / " + visible.Length.ToString("N0") + (search.Text.Length > 0 ? " matches" : " games");
    }
    void RefreshQueue(params int[] selections)
    {
        ((DarkList)queue).ResetHover(); queue.BeginUpdate();
        queue.Items.Clear(); foreach (var item in playlist) queue.Items.Add(item);
        foreach (int selection in selections) if (selection >= 0 && selection < playlist.Count) queue.SetSelected(selection, true);
        queue.EndUpdate();
        navigation[1].Text = "Playlist (" + playlist.Count + ")";
        removePlaylistButton.Enabled = queue.SelectedIndices.Count > 0;
        if (playlist.Count == 0) { shuffled = false; originalOrder.Clear(); }
        if (shuffled) {
            var currentPaths = new HashSet<string>(playlist.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
            originalOrder.RemoveAll(path => !currentPaths.Contains(path));
            var knownPaths = new HashSet<string>(originalOrder, StringComparer.OrdinalIgnoreCase);
            foreach (var item in playlist) if (knownPaths.Add(item.Path)) originalOrder.Add(item.Path);
        }
        ((ActionButton)shuffleButton).Selected = shuffled; shuffleButton.Invalidate();
        shuffleButton.Enabled = shuffled || playlist.Count > 1;
        shuffleButton.AccessibleDescription = shuffled ? "Shuffle enabled. Activate to restore the previous order." : "Shuffle disabled. Activate to randomize playlist order.";
        queueInfo.Text = playlist.Count == 0 ? "Add games from Library." : playlist.Count.ToString("N0") + " games / " + PlaylistDuration(playlist.Sum(x => x.Minutes)) + " total";
    }
    static string PlaylistDuration(decimal minutes)
    {
        // Round once after summing so fractional-minute seconds do not lose time.
        long seconds = (long)decimal.Round(minutes * 60m, 0, MidpointRounding.AwayFromZero);
        long days = seconds / 86400, hours = seconds / 3600 % 24, mins = seconds / 60 % 60;
        var parts = new List<string>();
        if (days > 0) parts.Add(days + "d");
        if (days > 0 || hours > 0) parts.Add(hours + "h");
        if (days > 0 || hours > 0 || mins > 0) parts.Add(mins + "m");
        parts.Add((seconds % 60) + "s");
        return string.Join(" ", parts);
    }
    static bool SameGame(PlaylistItem a, PlaylistItem b)
    { return (!string.IsNullOrEmpty(a.CatalogId) && !string.IsNullOrEmpty(b.CatalogId) && a.CatalogId.Equals(b.CatalogId, StringComparison.Ordinal)) || a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase) || a.Path.Replace('\\', '/').Equals(b.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase); }
    static IEnumerable<PlaylistItem> UniqueGames(IEnumerable<PlaylistItem> items)
    {
        var unique = new List<PlaylistItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items) {
            string path = item.Path.Replace('\\', '/');
            if ((!string.IsNullOrEmpty(item.CatalogId) && ids.Contains(item.CatalogId)) || names.Contains(item.Name) || paths.Contains(path)) continue;
            if (!string.IsNullOrEmpty(item.CatalogId)) ids.Add(item.CatalogId);
            names.Add(item.Name); paths.Add(path); unique.Add(item);
        }
        return unique;
    }
    void AddAllCatalogToPlaylist()
    {
        if (remoteCatalog == null || remoteCatalog.Length == 0) { Error("Refresh the catalogue first."); return; }
        var playable = DetectionCatalog.AllPlayable(remoteCatalog);
        foreach (var item in playable) item.Minutes = 5m / 60m;
        var combined = UniqueGames(playlist.Concat(playable)).ToList();
        int added = combined.Count - playlist.Count;
        var selected = queue.SelectedIndices.Cast<int>().ToArray();
        playlist.Clear(); playlist.AddRange(combined); RefreshQueue(selected);
        if (SavePlaylist()) Notice("Added " + added.ToString("N0") + " catalogue games to Playlist (5s each). Skipped " + (playable.Length - added).ToString("N0") + " duplicates. Library unchanged.");
    }
    internal void AddItem(PlaylistItem item, decimal duration)
    {
        try {
            var copy = item.Copy(); copy.Minutes = duration; Validate(copy);
            if (playlist.Any(x => SameGame(x, copy))) { Notice("Already in playlist."); return; }
            playlist.Add(copy); RefreshQueue(playlist.Count - 1); if (SavePlaylist()) Notice("Added to playlist.");
        } catch (Exception e) { Error(e.Message); }
    }
    void AddSelectedGames()
    {
        var selected = games.SelectedItems.Cast<PlaylistItem>().ToArray();
        if (selected.Length == 0) { Error("Select games first."); return; }
        try {
            decimal duration = minutes.Value; foreach (var item in selected) Validate(item);
            int added = 0;
            foreach (var item in selected) if (!playlist.Any(x => SameGame(x, item))) { var copy = item.Copy(); copy.Minutes = duration; playlist.Add(copy); added++; }
            RefreshQueue(); if (SavePlaylist()) Notice("Added " + added + " / Already in playlist " + (selected.Length - added));
        } catch (Exception e) { Error(e.Message); }
    }
    void RemoveLibraryGames()
    {
        var selected = games.SelectedItems.Cast<PlaylistItem>().ToArray();
        if (selected.Length == 0) { Error("Select games to remove with Ctrl or Shift."); return; }
        using (var dialog = new DarkConfirmationDialog("Remove games?", "Remove " + selected.Length + " selected games from the library?\n\nSaved playlist entries will remain available.", "Remove"))
            if (dialog.ShowDialog(this) != DialogResult.Yes) return;
        try {
            libraryStore.SetHidden(selected, true); RebuildLibrary(); Filter(); Notice("Removed " + selected.Length + " games from the library.");
        } catch (Exception e) { Error("Could not remove games: " + e.Message); }
    }
    void FilterCatalogGames()
    {
        if (remoteCatalog == null) return;
        var results = DetectionCatalog.Search(remoteCatalog, catalogSearch.Text);
        ((DarkList)catalogResults).ResetHover(); catalogResults.BeginUpdate(); catalogResults.Items.Clear();
        foreach (var item in results.Take(200)) catalogResults.Items.Add(item);
        catalogResults.EndUpdate(); addGamesButton.Enabled = false;
        catalogInfo.Text = results.Length == 0 ? (string.IsNullOrWhiteSpace(catalogSearch.Text) ? "Type a game name to search." : "No matches. Try the full title or fewer words.") : results.Length > 200 ? "Showing 200 of " + results.Length + " matches. Narrow your search." : results.Length + " matches / Ctrl or Shift to select several";
    }
    void AddCatalogGames()
    {
        var selected = catalogResults.SelectedItems.Cast<PlaylistItem>().Where(x => !string.IsNullOrEmpty(x.Path)).ToArray();
        if (selected.Length == 0) { Error("Select supported games first."); return; }
        try {
            int restored = 0, added = 0;
            var merged = localLibrary.Select(x => x.Copy()).ToList();
            foreach (var item in selected) {
                Validate(item);
                if (!catalog.Any(x => SameGame(x, item)) && !merged.Any(x => SameGame(libraryStore.ResolveIdentity(x), item))) { var copy = item.Copy(); copy.DateAdded = DateTime.Today.ToString("yyyy-MM-dd"); merged.Add(copy); added++; }
                else if (libraryStore.IsHidden(item)) restored++;
            }
            if (added > 0) { WriteLibrary(Path.Combine(root, "games.tsv"), merged); localLibrary.Clear(); localLibrary.AddRange(merged); }
            libraryStore.SetHidden(selected, false); RebuildLibrary(); Filter(); catalogResults.Invalidate();
            Notice("Added " + added + " / Restored " + restored + " / Already in library " + (selected.Length - added - restored));
        } catch (Exception e) { Error("Could not add games: " + e.Message); }
    }
    void UpdateDuration()
    {
        int[] selected = queue.SelectedIndices.Cast<int>().ToArray();
        if (selected.Length == 0) { Error("Select one or more playlist entries."); return; }
        decimal duration = editMinutes.Value;
        foreach (int index in selected) playlist[index].Minutes = duration;
        RefreshQueue(selected); if (SavePlaylist()) Notice("Updated " + selected.Length + " entries. Changes apply on the next start.");
    }
    void ShufflePlaylist()
    {
        if (!shuffled && playlist.Count < 2) return;
        var selected = new HashSet<PlaylistItem>(queue.SelectedItems.Cast<PlaylistItem>());
        if (shuffled) {
            var byPath = playlist.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
            var restored = originalOrder.Where(byPath.ContainsKey).Select(path => byPath[path]).ToList();
            var restoredItems = new HashSet<PlaylistItem>(restored);
            restored.AddRange(playlist.Where(item => !restoredItems.Contains(item)));
            playlist.Clear(); playlist.AddRange(restored); originalOrder.Clear(); shuffled = false;
        } else {
            originalOrder.Clear(); originalOrder.AddRange(playlist.Select(item => item.Path)); shuffled = true;
            for (int i = playlist.Count - 1; i > 0; i--) {
                int j = random.Next(i + 1);
                var item = playlist[i]; playlist[i] = playlist[j]; playlist[j] = item;
            }
        }
        RefreshQueue(Enumerable.Range(0, playlist.Count).Where(index => selected.Contains(playlist[index])).ToArray());
        if (SavePlaylist()) Notice((shuffled ? "Shuffle enabled." : "Previous playlist order restored.") + (run != null ? " Changes apply on the next start." : ""));
    }
    void MoveItem(int direction)
    {
        var selected = new HashSet<int>(queue.SelectedIndices.Cast<int>());
        var order = direction < 0 ? selected.OrderBy(x => x).ToArray() : selected.OrderByDescending(x => x).ToArray();
        foreach (int index in order) {
            int target = index + direction;
            if (target < 0 || target >= playlist.Count || selected.Contains(target)) continue;
            var item = playlist[index]; playlist[index] = playlist[target]; playlist[target] = item;
            selected.Remove(index); selected.Add(target);
        }
        RefreshQueue(selected.OrderBy(x => x).ToArray()); SavePlaylist();
    }
    void RemoveItem()
    {
        int[] selected = queue.SelectedIndices.Cast<int>().OrderByDescending(x => x).ToArray(); if (selected.Length == 0) return;
        foreach (int index in selected) playlist.RemoveAt(index);
        RefreshQueue(Math.Min(selected.Min(), playlist.Count - 1)); if (SavePlaylist()) Notice("Removed " + selected.Length + " entries. Changes apply on the next start.");
    }
    void LoadPlaylist()
    {
        string path = Path.Combine(root, "playlist.xml"); if (!File.Exists(path)) return;
        try {
            var saved = ReadPlaylist(path);
            playlist.AddRange(saved.Items); ApplyTiming(saved);
        } catch (Exception e) { Error("Could not load playlist: " + e.Message); }
        finally { loading = false; }
    }
    SavedPlaylist CurrentTiming()
    {
        return new SavedPlaylist { Items = playlist, Shuffle = shuffled, OriginalOrder = new List<string>(originalOrder), Repeat = repeat.Checked, RandomPlay = randomPlay.Checked, RandomPause = randomPause.Checked, PlayMin = playMin.Value, PlayMax = playMax.Value, PauseMin = pauseMin.Value, PauseMax = pauseMax.Value };
    }
    static void ValidateTiming(SavedPlaylist timing)
    {
        if (timing.PlayMin < 1m / 60m || timing.PlayMin > 10080 || timing.PlayMax < 1m / 60m || timing.PlayMax > 10080 ||
            timing.PauseMin < 0 || timing.PauseMin > 10080 || timing.PauseMax < 0 || timing.PauseMax > 10080)
            throw new ArgumentException("Play durations must be one second to seven days; pauses must be zero to seven days.");
        if (timing.RandomPlay && timing.PlayMin > timing.PlayMax) throw new ArgumentException("Random playtime: minimum must be no greater than maximum.");
        if (timing.RandomPause && timing.PauseMin > timing.PauseMax) throw new ArgumentException("Random pause: minimum must be no greater than maximum.");
    }
    void ApplyTiming(SavedPlaylist saved)
    {
        loading = true;
        try { shuffled = saved.Shuffle; originalOrder.Clear(); if (saved.OriginalOrder != null) originalOrder.AddRange(saved.OriginalOrder.Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.OrdinalIgnoreCase)); repeat.Checked = saved.Repeat; playMin.Value = saved.PlayMin; playMax.Value = saved.PlayMax; pauseMin.Value = saved.PauseMin; pauseMax.Value = saved.PauseMax; randomPlay.Checked = saved.RandomPlay; randomPause.Checked = saved.RandomPause; }
        finally { loading = false; }
    }
    decimal Sample(decimal min, decimal max) { return min + (max - min) * (decimal)random.NextDouble(); }
    bool SavePlaylist()
    {
        try {
            var saved = CurrentTiming(); ValidateTiming(saved); WritePlaylist(Path.Combine(root, "playlist.xml"), saved);
            return true;
        } catch (Exception e) { Error("Playlist could not be saved: " + e.Message); return false; }
    }
    bool StopProcess()
    {
        if (active == null) { clock.Reset(); return true; }
        try {
            if (!active.HasExited) { active.Kill(); if (!active.WaitForExit(3000)) throw new Exception("The previous process has not stopped yet."); }
            active.Dispose(); active = null; clock.Reset(); return true;
        } catch (Exception e) { Error("Could not stop: " + e.Message); return false; }
    }
    void EnsureGameExecutable(string target)
    {
        if (verifiedTargets.Contains(target) && File.Exists(target)) return;
        string source = System.Reflection.Assembly.GetExecutingAssembly().Location;
        bool current = false;
        using (var hash = SHA256.Create()) {
            if (sourceHash == null) using (var stream = File.OpenRead(source)) sourceHash = hash.ComputeHash(stream);
            if (File.Exists(target)) using (var stream = File.OpenRead(target)) current = sourceHash.SequenceEqual(hash.ComputeHash(stream));
        }
        if (!current) {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, true);
        }
        verifiedTargets.Add(target);
    }
    bool Launch(PlaylistItem item)
    {
        try {
            Validate(item);
            string target = PlaceholderPaths.Resolve(root, item.Path);
            if (!StopProcess()) return false;
            EnsureGameExecutable(target);
            using (var self = Process.GetCurrentProcess()) active = Process.Start(new ProcessStartInfo(target, "--placeholder " + self.Id + " " + self.StartTime.ToUniversalTime().Ticks + " \"" + item.Name + "\"" + (backgroundMode.Checked ? " background" : "")) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) });
            activeMinutes = run != null && runTiming.RandomPlay ? Sample(runTiming.PlayMin, runTiming.PlayMax) : item.Minutes; clock.Restart(); if (!CountdownEnabled) clock.Stop();
            activity.Text = item.Name; stopButton.Enabled = true; return true;
        } catch (Exception e) { Error("Could not start: " + e.Message); return false; }
    }
    internal void PlayNow(PlaylistItem item)
    {
        run = null; inPause = false; nextButton.Enabled = false;
        if (!Launch(item)) { PlaybackFailed(); return; }
        Notice("Playing now. Playlist playback is off."); UpdateCountdown();
    }
    void PlaySelected()
    {
        var selected = games.SelectedItem as PlaylistItem;
        if (selected == null) { Error("Select a game first."); return; }
        try { var item = selected.Copy(); item.Minutes = minutes.Value; PlayNow(item); }
        catch (ArgumentException e) { Error(e.Message); }
    }
    internal void StartPlaylist()
    {
        try {
            runTiming = CurrentTiming(); ValidateTiming(runTiming); inPause = false;
            var candidate = new PlaylistRun(playlist); foreach (var item in candidate.Items) Validate(item);
            run = candidate;
            if (!Launch(run.Current)) { PlaybackFailed(); return; }
            nextButton.Enabled = true; Notice("Playlist running. Edits apply on the next start."); UpdateCountdown();
        } catch (Exception e) { Error(e.Message); }
    }
    internal void Advance() { MoveNext(false); }
    void MoveNext(bool withPause)
    {
        if (run == null) return;
        if (inPause) { inPause = false; if (!Launch(run.Current)) PlaybackFailed(); UpdateCountdown(); return; }
        if (!run.Advance(repeat.Checked)) { StopAll("Playlist finished."); return; }
        if (withPause && runTiming.RandomPause) {
            if (!StopProcess()) { PlaybackFailed(); return; }
            pauseMinutes = Sample(runTiming.PauseMin, runTiming.PauseMax);
            if (pauseMinutes > 0) { inPause = true; clock.Restart(); activity.Text = "Playlist pause"; stopButton.Enabled = true; UpdateCountdown(); return; }
        }
        if (!Launch(run.Current)) { PlaybackFailed(); return; }
        UpdateCountdown();
    }
    void PlaybackFailed()
    {
        run = null; inPause = false; nextButton.Enabled = false;
        if (active == null) { activity.Text = "No game running"; remaining.Text = "Playback stopped. See the message below."; stopButton.Enabled = false; }
    }
    internal void StopAll(string message)
    {
        run = null; inPause = false; nextButton.Enabled = false;
        if (!StopProcess()) return;
        activity.Text = "No game running"; remaining.Text = "Choose a game or start your playlist."; stopButton.Enabled = false; Notice(message);
    }
    bool CountdownEnabled { get { return run != null || timerEnabled.Checked; } }

    internal void TickPlayback()
    {
        if (inPause) {
            if (clock.Elapsed.TotalMinutes >= (double)pauseMinutes) { inPause = false; if (!Launch(run.Current)) PlaybackFailed(); }
            UpdateCountdown(); return;
        }
        if (active == null) return;
        if (active.HasExited) { StopAll("Game process exited. Playlist stopped."); return; }
        if (CountdownEnabled && clock.Elapsed.TotalMinutes >= (double)activeMinutes) {
            if (run != null) MoveNext(true); else StopAll("Timer finished.");
        }
        UpdateCountdown();
    }
    static string Duration(TimeSpan time)
    { return ((int)time.TotalHours).ToString("00") + time.ToString(@"\:mm\:ss"); }
    void UpdateCountdown()
    {
        if (inPause) {
            TimeSpan pauseLeft = TimeSpan.FromMinutes((double)pauseMinutes) - clock.Elapsed;
            remaining.Text = Duration(TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(pauseLeft.TotalSeconds)))) + " pause / Next: " + run.Current.Name; return;
        }
        if (active == null) return;
        string prefix = run == null ? "" : (run.Index + 1) + "/" + run.Items.Count + "  /  ";
        string popup;
        if (!CountdownEnabled) {
            remaining.Text = "Timer off / Play until stopped";
            popup = "Timer off  /  Close this window to stop.";
        } else {
            TimeSpan left = TimeSpan.FromMinutes((double)activeMinutes) - clock.Elapsed;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            string countdown = Duration(TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds)));
            remaining.Text = prefix + countdown + " left" + (run != null && repeat.Checked ? "  /  Repeat" : "");
            popup = "Time remaining  " + countdown;
        }
        ActivityMessages.Send(active, Handle, popup);
    }
}
