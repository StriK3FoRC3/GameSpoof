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
            library.ApplySnapshot(next, new DateTime(2026, 10, 25, 10, 0, 0, DateTimeKind.Utc));
            var refreshed = library.Build(manual);
            Check(refreshed.Count == 2 && refreshed.All(x => x.CatalogId != "5" && x.CatalogId != "2") && refreshed.Single(x => x.CatalogId == "1").Path == "custom/gta5.exe", "refresh updates catalog without auto-adding games or changing custom paths");
            Check(DetectionCatalog.Search(library.Entries, "New Game").Single().Path == "newgame.exe", "newly refreshed entries are available to add");
            library.SetHidden(new[] { new PlaylistItem { Name = "Atomic Heart", Path = "win64/atomic-new.exe" } }, false);
            Check(library.Build(manual).Count == 3, "explicit re-add restores a removed game");
            string cache = File.ReadAllText(Path.Combine(root, "catalog-cache.json")); var date = library.UpdatedUtc;
            bool rejected = false; try { library.ApplySnapshot(new DetectionCatalog.Entry[0], DateTime.UtcNow); } catch (FormatException) { rejected = true; }
            Check(rejected && cache == File.ReadAllText(Path.Combine(root, "catalog-cache.json")) && library.UpdatedUtc == date, "failed refresh preserves offline catalog and timestamp");
            var reopened = new CatalogLibrary(root);
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
                Call(form, "ShowPage", 2);
                var refresh = (Button)Field(form, "refreshButton");
                var catalogSearch = (TextBox)Field(form, "catalogSearch");
                Check(refresh.Text == "Refresh Game Catalogue" && refresh.Parent == catalogSearch.Parent.Parent && ((Label)Field(form, "lastUpdated")).Visible, "Refresh Game Catalogue and update date are in Add games beside search input");
                Check(refresh.Width >= TextRenderer.MeasureText(refresh.Text, refresh.Font).Width, "refresh button label fits without clipping");
                Check(form.GetType().GetField("favoritesButton", Flags) == null && form.GetType().GetField("findGamesButton", Flags) == null, "favorite feature and separate Search button removed");
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save("add-games-controls.png"); }
                Call(form, "ShowPage", 0);
                Check(File.ReadAllText(Path.Combine(root, "games.tsv")) == originalTsv, "opening and filtering never rewrites user game list");
                var search = (TextBox)Field(form, "search"); search.Text = "gta5";
                Check(list.Items.Count == 1 && ((PlaylistItem)list.Items[0]).Path == "custom/gta5.exe", "Library search keeps abbreviations and custom paths"); search.Clear();
                Call(form, "ShowPage", 2); ((TextBox)Field(form, "catalogSearch")).Text = "WARDOGS";
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
                Call(form, "ShowPage", 0);
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save("manual-library-cache.png"); }
                form.Close();
            }
            using (var form = Create(root)) {
                Check(((ListBox)Field(form, "games")).Items.Count == 3, "manually added cached games persist after restart");
                ((TextBox)Field(form, "catalogSearch")).Text = "Another New";
                Check(((ListBox)Field(form, "catalogResults")).Items.Count == 1, "fresh catalog updates remain searchable after offline restart");
            }
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
