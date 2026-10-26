using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

// The downloaded catalog and local preferences never replace the user's custom TSV.
public sealed class CatalogLibrary
{
    public sealed class Snapshot
    {
        public int Version { get; set; }
        public string UpdatedUtc { get; set; }
        public DetectionCatalog.Entry[] Entries { get; set; }
    }
    public sealed class Preferences
    {
        public List<string> Hidden { get; set; }
        public Preferences() { Hidden = new List<string>(); }
    }
    readonly string root;
    Snapshot snapshot;
    Dictionary<string, DetectionCatalog.Entry> byId = new Dictionary<string, DetectionCatalog.Entry>(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, DetectionCatalog.Entry> byTitle = new Dictionary<string, DetectionCatalog.Entry>(StringComparer.Ordinal);
    Dictionary<string, DetectionCatalog.Entry> byName = new Dictionary<string, DetectionCatalog.Entry>(StringComparer.OrdinalIgnoreCase);
    HashSet<string> hidden;
    public string LoadError { get; private set; }
    public bool HasCache { get { return snapshot != null; } }
    public bool NeedsRefresh { get { return snapshot == null || snapshot.Version < 2; } }
    public DetectionCatalog.Entry[] Entries { get { return snapshot == null ? new DetectionCatalog.Entry[0] : snapshot.Entries; } }
    public DateTime? UpdatedUtc
    {
        get { DateTime date; return snapshot != null && DateTime.TryParse(snapshot.UpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date) ? (DateTime?)date.ToUniversalTime() : null; }
    }
    public CatalogLibrary(string directory)
    {
        root = directory;
        var preferences = new Preferences();
        string cache = Path.Combine(root, "catalog-cache.json");
        try {
            if (File.Exists(cache)) {
                var loaded = Serializer().Deserialize<Snapshot>(File.ReadAllText(cache));
                if (loaded == null || loaded.Entries == null || loaded.Entries.Length == 0) throw new FormatException("Invalid cached catalog.");
                snapshot = loaded; Reindex();
            }
        } catch (Exception e) { snapshot = null; LoadError = "Could not read cached catalog: " + e.Message; }
        try {
            string path = Path.Combine(root, "library-preferences.json");
            if (File.Exists(path)) {
                string json = File.ReadAllText(path);
                preferences = Serializer().Deserialize<Preferences>(json) ?? new Preferences();
                var old = Serializer().Deserialize<Dictionary<string, object>>(json);
                if (old != null && (old.ContainsKey("Favorites") || old.ContainsKey("FavoritesOnly"))) WriteJson(path, preferences);
            }
        } catch (Exception e) { LoadError = "Could not read library preferences: " + e.Message; }
        hidden = new HashSet<string>(preferences.Hidden ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
    }
    static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }; }
    static string Key(PlaylistItem item) { return string.IsNullOrEmpty(item.CatalogId) ? NameKey(item.Name) : "id:" + item.CatalogId; }
    static string NameKey(string name) { return "name:" + name; }
    public bool IsHidden(PlaylistItem item) { return hidden.Contains(Key(item)) || hidden.Contains(NameKey(item.Name)); }
    void SavePreferences(HashSet<string> newHidden)
    {
        var next = new Preferences { Hidden = newHidden.OrderBy(x => x).ToList() };
        WriteJson(Path.Combine(root, "library-preferences.json"), next);
        hidden = newHidden;
    }
    public PlaylistItem ResolveIdentity(PlaylistItem item)
    {
        if (!string.IsNullOrEmpty(item.CatalogId)) return item;
        DetectionCatalog.Entry entry;
        if (byName.TryGetValue(item.Name, out entry) || byTitle.TryGetValue(DetectionCatalog.TitleKey(item.Name), out entry)) { var copy = item.Copy(); copy.CatalogId = entry.id; return copy; }
        return item;
    }
    public void SetHidden(IEnumerable<PlaylistItem> items, bool value)
    {
        var next = new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase);
        foreach (var original in items) {
            var item = ResolveIdentity(original);
            if (value) { next.Add(Key(item)); next.Add(NameKey(item.Name)); }
            else {
                next.Remove(Key(item)); next.Remove(NameKey(item.Name));
                foreach (string key in next.Where(x => x.StartsWith("name:", StringComparison.OrdinalIgnoreCase)).ToArray()) {
                    var previous = ResolveIdentity(new PlaylistItem { Name = key.Substring(5) });
                    if (Key(previous).Equals(Key(item), StringComparison.OrdinalIgnoreCase) || DetectionCatalog.TitleKey(previous.Name) == DetectionCatalog.TitleKey(item.Name)) next.Remove(key);
                }
            }
        }
        SavePreferences(next);
    }
    public void Refresh() { ApplySnapshot(DetectionCatalog.Fetch(), DateTime.UtcNow); }
    public void ApplySnapshot(DetectionCatalog.Entry[] downloaded, DateTime updatedUtc)
    {
        var valid = new List<DetectionCatalog.Entry>();
        foreach (var entry in downloaded ?? new DetectionCatalog.Entry[0]) {
            if (entry == null || string.IsNullOrWhiteSpace(entry.name) || entry.name.Contains('"') || entry.name.Any(char.IsControl)) continue;
            string path = DetectionCatalog.WindowsPath(entry);
            DetectionCatalog.Entry old; string added = updatedUtc.ToLocalTime().ToString("yyyy-MM-dd");
            if ((!string.IsNullOrEmpty(entry.id) && byId.TryGetValue(entry.id, out old)) || byName.TryGetValue(entry.name, out old)) added = old.added_on ?? added;
            valid.Add(new DetectionCatalog.Entry { id = entry.id, name = entry.name, aliases = entry.aliases, added_on = added, executables = path.Length == 0 ? new DetectionCatalog.Executable[0] : new[] { new DetectionCatalog.Executable { os = "win32", name = path } } });
        }
        if (!valid.Any(x => x.executables.Length > 0)) throw new FormatException("The downloaded catalog contains no supported Windows games. Saved catalog retained.");
        var next = new Snapshot { Version = 2, Entries = valid.ToArray(), UpdatedUtc = updatedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) };
        // Only publish a new snapshot after its complete file has been saved successfully.
        WriteJson(Path.Combine(root, "catalog-cache.json"), next);
        snapshot = next; Reindex();
    }
    void Reindex()
    {
        var ids = new Dictionary<string, DetectionCatalog.Entry>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, DetectionCatalog.Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries.Where(x => x != null && !string.IsNullOrEmpty(x.name))) {
            if (!string.IsNullOrEmpty(entry.id)) ids[entry.id] = entry;
            names[entry.name] = entry;
        }
        // Canonical titles win over aliases shared by different editions.
        foreach (var entry in Entries.Where(x => x != null)) foreach (string alias in entry.aliases ?? new string[0]) if (!string.IsNullOrWhiteSpace(alias) && !names.ContainsKey(alias)) names.Add(alias, entry);
        var titles = new Dictionary<string, DetectionCatalog.Entry>(StringComparer.Ordinal);
        foreach (var entry in Entries.Where(x => x != null && !string.IsNullOrEmpty(x.name))) titles[DetectionCatalog.TitleKey(entry.name)] = entry;
        foreach (var pair in names) { string key = DetectionCatalog.TitleKey(pair.Key); if (!titles.ContainsKey(key)) titles.Add(key, pair.Value); }
        byId = ids; byName = names; byTitle = titles;
    }
    public List<PlaylistItem> Build(IEnumerable<PlaylistItem> local)
    {
        // The cache is a lookup source. Only explicitly added games belong in Library.
        var result = new Dictionary<string, PlaylistItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var original in local) {
            if (string.IsNullOrWhiteSpace(original.Path)) continue;
            var copy = ResolveIdentity(original.Copy());
            result[copy.Name] = copy;
        }
        return result.Values.Where(x => !IsHidden(x)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public Func<PlaylistItem, bool> Matcher(string query)
    {
        var match = DetectionCatalog.TitleMatcher(query);
        return item => {
            if (match(item.Name)) return true;
            DetectionCatalog.Entry entry;
            return !string.IsNullOrEmpty(item.CatalogId) && byId.TryGetValue(item.CatalogId, out entry) && (match(entry.name) || (entry.aliases ?? new string[0]).Any(match));
        };
    }
    static void WriteJson(string path, object value)
    {
        AtomicFile.Write(path, delegate(Stream stream) {
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(Serializer().Serialize(value));
        });
    }
}
