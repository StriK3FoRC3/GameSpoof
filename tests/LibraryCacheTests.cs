using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

static class LibraryCacheTests
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool valid, string name) { if (!valid) throw new Exception(name); Console.WriteLine("PASS: " + name); }
    static object Field(Form form, string name) { return form.GetType().GetField(name, Flags).GetValue(form); }
    static object Call(Form form, string name, params object[] args) { return form.GetType().GetMethod(name, Flags).Invoke(form, args); }
    static Form Create(string root) { return (Form)typeof(PlaylistRun).Assembly.GetType("Launcher").GetConstructor(Flags, null, new[] { typeof(string) }, null).Invoke(new object[] { root }); }
    static DetectionCatalog.Entry Game(string id, string name, string path, params string[] aliases)
    { return new DetectionCatalog.Entry { id = id, name = name, aliases = aliases, executables = path == null ? new DetectionCatalog.Executable[0] : new[] { new DetectionCatalog.Executable { os = "win32", name = path } } }; }
    [STAThread] static int Main(string[] args)
    {
        try {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var library = new CatalogLibrary(root);
            var first = new[] { Game("1", "Grand Theft Auto V Legacy", "gta5.exe", "Grand Theft Auto V"), Game("2", "Atomic Heart", "win64/atomic.exe"), Game("3", "Unavailable", null), Game("4", "Unsafe", "../escape.exe"), Game("wd", "WARDOGS", null) };
            library.ApplySnapshot(first, new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc));
            Check(library.NewCount == 0, "first catalogue download establishes a baseline without marking everything new");
            Check(library.Entries.Length == 5 && library.Entries.Count(x => DetectionCatalog.WindowsPath(x).Length > 0) == 2, "cache keeps unavailable titles but only retains safe Windows executable paths");
            Check(!library.NeedsRefresh && DetectionCatalog.Search(library.Entries, "WARDOGS").Single().Path == "", "WARDOGS with empty executable list remains searchable in complete cache");
            var custom = new[] { new PlaylistItem { Name = "Grand Theft Auto V", Path = "custom/gta5.exe", Minutes = 10, DateAdded = "2026-10-24" }, new PlaylistItem { Name = "EA SPORTS FC 27", Path = "EA SPORTS FC 27/FC27.exe", Minutes = 10, DateAdded = "2026-10-25" } };
            var games = library.Build(custom);
            Check(games.Count == 2 && games.Single(x => x.CatalogId == "1").Path == "custom/gta5.exe", "Library contains only manually added games and preserves custom paths");
            Check(library.Build(new PlaylistItem[0]).Count == 0 && DetectionCatalog.Search(library.Entries, "Atomic Heart").Length == 1, "downloaded games stay searchable without entering Library");
            var atomic = DetectionCatalog.Search(library.Entries, "Atomic Heart").Single();
            var manual = custom.Concat(new[] { atomic }).ToArray();
            var offline = new CatalogLibrary(root);
            Check(offline.HasCache && offline.Build(manual).Count == 3, "manual selections and catalog remain usable offline");
            library.SetHidden(new[] { atomic }, true);
            Check(library.Build(manual).Count == 2 && DetectionCatalog.Search(library.Entries, "Atomic").Length == 1, "removed Library games remain in offline catalog");
            var next = new[] { Game("1", "Grand Theft Auto V Legacy", "updated/gta5.exe", "Grand Theft Auto V"), Game("2", "Atomic Heart Renamed", "win64/atomic-new.exe", "Atomic Heart"), Game("5", "New Game", "newgame.exe"), Game("wd", "WARDOGS", null) };
            library.ApplySnapshot(next, new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc));
            Check(library.NewCount == 1 && library.IsNew(DetectionCatalog.Search(library.Entries, "New Game").Single()), "refresh identifies new games but not renamed titles or executable updates");
            Check(library.Entries.Single(x => x.id == "1").added_on == "2026-10-25" && library.Entries.Single(x => x.id == "5").added_on == "2026-10-26", "first seen dates survive refresh and new games receive the new date");
            var refreshed = library.Build(manual);
            Check(refreshed.Count == 2 && refreshed.All(x => x.CatalogId != "5" && x.CatalogId != "2") && refreshed.Single(x => x.CatalogId == "1").Path == "custom/gta5.exe", "refresh updates catalog without auto-adding games or changing custom paths");
            Check(DetectionCatalog.Search(library.Entries, "New Game").Single().Path == "newgame.exe", "newly refreshed entries are available to add");
            library.SetHidden(new[] { new PlaylistItem { Name = "Atomic Heart", Path = "win64/atomic-new.exe" } }, false);
            Check(library.Build(manual).Count == 3, "explicit re-add restores a removed game");
            string cache = File.ReadAllText(Path.Combine(root, "catalog-cache.json")); var date = library.UpdatedUtc;
            bool rejected = false; try { library.ApplySnapshot(new DetectionCatalog.Entry[0], DateTime.UtcNow); } catch (FormatException) { rejected = true; }
            Check(rejected && cache == File.ReadAllText(Path.Combine(root, "catalog-cache.json")) && library.UpdatedUtc == date, "failed refresh preserves offline catalog and timestamp");
            var reopened = new CatalogLibrary(root);
            Check(reopened.NewCount == 1 && reopened.IsNew(DetectionCatalog.Search(reopened.Entries, "New Game").Single()), "new-game markers survive restart and a failed refresh");
            Check(reopened.UpdatedUtc == date && DetectionCatalog.Search(reopened.Entries, "New Game").Length == 1 && reopened.Build(custom).Count == 2, "offline reopening retains catalog without flooding Library");
            File.WriteAllText(Path.Combine(root, "library-preferences.json"), "{\"Favorites\":[\"id:2\"],\"FavoritesOnly\":true,\"Hidden\":[\"id:5\"]}");
            var migrated = new CatalogLibrary(root);
            Check(migrated.IsHidden(new PlaylistItem { CatalogId = "5", Name = "New Game" }) && !File.ReadAllText(Path.Combine(root, "library-preferences.json")).Contains("Favorites"), "obsolete favorite settings removed while preserving hidden games");
            File.WriteAllText(Path.Combine(root, "games.tsv"), "Grand Theft Auto V\tcustom/gta5.exe\t2026-10-24\nEA SPORTS FC 27\tEA SPORTS FC 27/FC27.exe\t2026-10-25\n");
            string originalTsv = File.ReadAllText(Path.Combine(root, "games.tsv"));
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            using (var form = Create(root)) {
                form.Show(); Application.DoEvents(); var list = (ListBox)Field(form, "games");
                Check(list.Items.Count == 2, "main Library displays only selected games");
                var librarySort = (Control)Field(form, "librarySort");
                Action<int> selectLibrarySort = index => librarySort.GetType().GetProperty("SelectedIndex").SetValue(librarySort, index, null);
                list.SelectedIndex = 0;
                string selectedLibraryGame = ((PlaylistItem)list.SelectedItem).Name;
                selectLibrarySort(1);
                Check(((PlaylistItem)list.Items[0]).DateAdded == "2026-10-24" && ((PlaylistItem)list.SelectedItem).Name == selectedLibraryGame, "library oldest-first sorting preserves the selected game");
                selectLibrarySort(0);
                Check(((PlaylistItem)list.Items[0]).DateAdded == "2026-10-25", "library newest-first sorting uses the date added to Library");
                selectLibrarySort(3);
                Check(((PlaylistItem)list.Items[0]).Name == "Grand Theft Auto V", "library name sorting supports descending order");
                selectLibrarySort(2);
                Call(form, "ShowPage", 2);
                var refresh = (Button)Field(form, "refreshButton");
                var catalogSearch = (TextBox)Field(form, "catalogSearch");
                Check(refresh.Text == "Refresh Game Catalogue" && refresh.Parent == catalogSearch.Parent.Parent && ((Label)Field(form, "lastUpdated")).Visible, "Refresh Game Catalogue and update date are in Add games beside search input");
                Check(refresh.Width >= TextRenderer.MeasureText(refresh.Text, refresh.Font).Width, "refresh button label fits without clipping");
                var browse = (Button)Field(form, "browseCatalogButton");
                var sort = (Control)Field(form, "catalogSort");
                Action<int> selectSort = index => sort.GetType().GetProperty("SelectedIndex").SetValue(sort, index, null);
                var newOnly = (CheckBox)Field(form, "newCatalogOnly");
                var catalogue = (ListBox)Field(form, "catalogResults");
                var showAll = (Button)Field(form, "showAllCatalogButton");
                Check(catalogue.Items.Count == 3 && ((CheckBox)Field(form, "playableCatalogOnly")).Checked && !showAll.Visible, "small playable catalogue loads without an unnecessary Show all button");
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save("add-games-playable.png"); }
                catalogSearch.Text = "Atomic"; Call(form, "ShowPage", 0); Call(form, "ShowPage", 2);
                Check(catalogue.Items.Count == 1 && catalogSearch.Text == "Atomic", "returning to Add games keeps the active search");
                catalogSearch.Clear();
                Check(catalogue.Items.Count == 3 && ((CheckBox)Field(form, "playableCatalogOnly")).Checked, "returning to Add games restores the playable-only default while preserving search");
                Check(browse.Parent == refresh.Parent && browse.Left > refresh.Left, "All games sits beside Refresh Game Catalogue");
                browse.PerformClick(); Application.DoEvents();
                Check(catalogue.Items.Count == 4 && ((PlaylistItem)catalogue.Items[0]).CatalogId == "5" && list.Items.Count == 2, "All games loads the complete catalogue newest first without adding to Library");
                var playableOnly = (CheckBox)Field(form, "playableCatalogOnly");
                playableOnly.Checked = true;
                Check(catalogue.Items.Count == 3 && catalogue.Items.Cast<PlaylistItem>().All(x => !string.IsNullOrEmpty(x.Path)), "Playable only hides unavailable catalogue entries");
                newOnly.Checked = true;
                Check(catalogue.Items.Count == 1, "Playable only combines with the new-games filter");
                newOnly.Checked = false; catalogSearch.Text = "WARDOGS";
                Check(catalogue.Items.Count == 0, "Playable only applies to search results");
                playableOnly.Checked = false;
                Check(catalogue.Items.Count == 1, "disabling Playable only restores unavailable search results");
                catalogSearch.Clear();
                selectSort(1);
                Check(((PlaylistItem)catalogue.Items[0]).DateAdded == "2026-10-25", "oldest first sorts by catalogue first-seen date");
                ((Button)sort).PerformClick(); Application.DoEvents();
                var sortMenu = (ContextMenuStrip)sort.GetType().GetField("menu", Flags).GetValue(sort);
                Check(sortMenu.Visible && sortMenu.Items.Count == 4 && ((ToolStripMenuItem)sortMenu.Items[1]).Checked, "dark sort menu opens with the current order checked");
                using (var bitmap = new Bitmap(sortMenu.Width, sortMenu.Height)) { sortMenu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, sortMenu.Size)); bitmap.Save("catalogue-sort-menu.png"); }
                ((ToolStripMenuItem)sortMenu.Items[3]).PerformClick(); sortMenu.Close();
                Check(((PlaylistItem)catalogue.Items[0]).Name == "WARDOGS", "clicking a sort menu option applies the selected order");
                selectSort(3);
                Check(((PlaylistItem)catalogue.Items[0]).Name == "WARDOGS", "name sorting includes unavailable games");
                newOnly.Checked = true;
                Check(catalogue.Items.Count == 1 && ((PlaylistItem)catalogue.Items[0]).CatalogId == "5", "new since last refresh filter shows only additions");
                catalogSearch.Text = "Atomic";
                Check(catalogue.Items.Count == 0, "new filter combines with title search");
                newOnly.Checked = false; catalogSearch.Clear(); selectSort(0);
                Check(form.GetType().GetField("favoritesButton", Flags) == null && form.GetType().GetField("findGamesButton", Flags) == null, "favorite feature and separate Search button removed");
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save("add-games-controls.png"); }
                Call(form, "ShowPage", 0);
                Check(File.ReadAllText(Path.Combine(root, "games.tsv")) == originalTsv, "opening and filtering never rewrites user game list");
                var search = (TextBox)Field(form, "search"); search.Text = "gta5";
                Check(list.Items.Count == 1 && ((PlaylistItem)list.Items[0]).Path == "custom/gta5.exe", "Library search keeps abbreviations and custom paths"); search.Clear();
                Call(form, "ShowPage", 2); ((TextBox)Field(form, "catalogSearch")).Text = "WARDOGS";
                ((CheckBox)Field(form, "playableCatalogOnly")).Checked = false;
                var unavailable = (ListBox)Field(form, "catalogResults");
                Check(unavailable.Items.Count == 1 && ((PlaylistItem)unavailable.Items[0]).Path == "", "unavailable WARDOGS is shown instead of no search results");
                unavailable.SetSelected(0, true);
                Check(!((Button)Field(form, "addGamesButton")).Enabled, "WARDOGS cannot be added without a supported executable");
                Call(form, "AddCatalogGames"); Check(list.Items.Count == 2, "unavailable result never enters Library");
                Call(form, "ShowPage", 2); ((TextBox)Field(form, "catalogSearch")).Text = "Atomic Heart";
                var results = (ListBox)Field(form, "catalogResults");
                Check(results.Items.Count == 1 && list.Items.Count == 2, "Add games searches saved catalog offline without adding results automatically");
                results.SetSelected(0, true); Call(form, "AddCatalogGames");
                Check(list.Items.Count == 3 && list.Items.Cast<PlaylistItem>().Any(x => x.CatalogId == "2"), "explicit Add selected games inserts chosen cached game");
                string addedTsv = File.ReadAllText(Path.Combine(root, "games.tsv")); Call(form, "AddCatalogGames");
                Check(list.Items.Count == 3 && addedTsv == File.ReadAllText(Path.Combine(root, "games.tsv")), "repeated manual add skips duplicates");
                ((TextBox)Field(form, "catalogSearch")).Text = "gta"; results.SetSelected(0, true); Call(form, "AddCatalogGames");
                Check(list.Items.Count == 3, "same game with canonical title and custom path is not duplicated");
                var uiStore = (CatalogLibrary)Field(form, "libraryStore");
                uiStore.ApplySnapshot(next.Concat(new[] { Game("6", "Another New Game", "another.exe") }).ToArray(), DateTime.UtcNow);
                Call(form, "RebuildLibrary"); Call(form, "Filter");
                Check(list.Items.Count == 3, "catalog refresh leaves selected Library unchanged");
                var large = Enumerable.Range(0, 1100).Select(i => Game("bulk-" + i, "Catalogue game " + i, "game" + i + ".exe")).ToArray();
                form.GetType().GetField("remoteCatalog", Flags).SetValue(form, large);
                browse.PerformClick();
                playableOnly.Checked = true;
                Check(catalogue.Items.OfType<PlaylistItem>().Count() == 10 && catalogue.Items.Contains(showAll) && showAll.Parent == catalogue && showAll.Text.Contains("1,090"), "preview contains 10 games followed by an inline Show all button");
                Check(!showAll.Visible, "Show all stays below the viewport until the list is scrolled");
                catalogue.GetType().GetMethod("SelectAllItems").Invoke(catalogue, null);
                Check(catalogue.SelectedItems.Count == 10 && catalogue.SelectedItems.Cast<object>().All(x => x is PlaylistItem), "Ctrl+A selects only games, not the Show all row");
                catalogue.TopIndex = catalogue.Items.Count - 1; Application.DoEvents();
                Check(showAll.Visible && catalogue.ClientRectangle.IntersectsWith(showAll.Bounds), "Show all scrolls into view after the tenth game");
                int previousTop = catalogue.TopIndex;
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save("catalogue-inline-show-all.png"); }
                showAll.PerformClick();
                Check(catalogue.Items.Count == 1100 && !showAll.Visible && playableOnly.Checked && list.Items.Count == 3, "Show all expands the matching list without disabling Playable only or changing Library");
                Check(catalogue.TopIndex == previousTop && catalogue.SelectedItems.Count == 10, "expanding the catalogue preserves scroll position and game selection");
                selectSort(3);
                Check(catalogue.Items.OfType<PlaylistItem>().Count() == 10 && catalogue.Items.Contains(showAll), "changing sort resets the preview to 10 rows");
                catalogue.TopIndex = catalogue.Items.Count - 1; Application.DoEvents();
                showAll.PerformClick(); catalogSearch.Text = "Catalogue";
                Check(catalogue.Items.OfType<PlaylistItem>().Count() == 10 && catalogue.Items.Contains(showAll), "changing search resets the preview to 10 rows");
                catalogue.TopIndex = catalogue.Items.Count - 1; Application.DoEvents();
                showAll.PerformClick(); Call(form, "ShowPage", 0); Call(form, "ShowPage", 2);
                Check(catalogue.Items.OfType<PlaylistItem>().Count() == 10 && playableOnly.Checked && catalogue.Items.Contains(showAll), "reopening Add games starts with a small playable preview again");
                Call(form, "ShowPage", 0);
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save("manual-library-cache.png"); }
                form.Close();
            }
            using (var form = Create(root)) {
                Check(((ListBox)Field(form, "games")).Items.Count == 3, "manually added cached games persist after restart");
                ((TextBox)Field(form, "catalogSearch")).Text = "Another New";
                Check(((ListBox)Field(form, "catalogResults")).Items.Count == 1, "fresh catalog updates remain searchable after offline restart");
            }
            var unchanged = new CatalogLibrary(root);
            var previousNew = DetectionCatalog.AllEntries(unchanged.Entries).Where(unchanged.IsNew).Select(x => x.CatalogId).ToArray();
            unchanged.ApplySnapshot(unchanged.Entries, DateTime.UtcNow);
            unchanged.ApplySnapshot(unchanged.Entries, DateTime.UtcNow.AddMinutes(1));
            var kept = new CatalogLibrary(root);
            Check(previousNew.Length > 0 && DetectionCatalog.AllEntries(kept.Entries).Where(kept.IsNew).Select(x => x.CatalogId).SequenceEqual(previousNew) && unchanged.LastRefreshNewCount == 0, "repeated unchanged refreshes keep the previous new-games batch across restart");
            kept.ApplySnapshot(kept.Entries.Concat(new[] { Game("7", "Latest discovery", "latest.exe") }).ToArray(), DateTime.UtcNow.AddMinutes(2));
            Check(kept.NewCount == 1 && kept.LastRefreshNewCount == 1 && kept.IsNew(DetectionCatalog.Search(kept.Entries, "Latest discovery").Single()) && DetectionCatalog.AllEntries(kept.Entries).Where(kept.IsNew).All(x => !previousNew.Contains(x.CatalogId)), "a refresh with new games replaces the previous batch with just the newly discovered games");
            kept.ApplySnapshot(kept.Entries, DateTime.UtcNow.AddMinutes(3));
            Check(new CatalogLibrary(root).IsNew(DetectionCatalog.Search(kept.Entries, "Latest discovery").Single()) && kept.LastRefreshNewCount == 0, "the replacement batch also survives another unchanged refresh");
            string legacyRoot = Path.Combine(root, "legacy-cache"); Directory.CreateDirectory(legacyRoot);
            string brokenLibraryRoot = Path.Combine(root, "broken-library"); Directory.CreateDirectory(brokenLibraryRoot);
            File.Copy(Path.Combine(root, "catalog-cache.json"), Path.Combine(brokenLibraryRoot, "catalog-cache.json"));
            File.WriteAllText(Path.Combine(brokenLibraryRoot, "games.tsv"), "invalid row without a path");
            using (var form = Create(brokenLibraryRoot)) {
                ((TextBox)Field(form, "catalogSearch")).Text = "Atomic Heart";
                Check(((ListBox)Field(form, "catalogResults")).Items.Count == 1 && ((Button)Field(form, "addAllPlaylistButton")).Enabled, "malformed library does not disable the independent offline catalogue");
            }
            File.WriteAllText(Path.Combine(legacyRoot, "catalog-cache.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new CatalogLibrary.Snapshot { Entries = first, UpdatedUtc = DateTime.UtcNow.ToString("o") }));
            var legacy = new CatalogLibrary(legacyRoot);
            Check(legacy.HasCache && legacy.NeedsRefresh, "older filtered cache is retained and marked for automatic upgrade");
            if (args.Length > 0) {
                var watch = Stopwatch.StartNew(); var live = new CatalogLibrary(args[0]);
                Check(live.Entries.Length > 1000 && live.Build(new PlaylistItem[0]).Count == 0, "full bundled cache does not populate an empty Library");
                var found = DetectionCatalog.Search(live.Entries, "gta"); Check(found.Length > 3, "bundled GTA catalog remains searchable offline");
                Console.WriteLine("Live cache load/search: " + watch.ElapsedMilliseconds + " ms / " + live.Entries.Length + " cached games");
            }
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
