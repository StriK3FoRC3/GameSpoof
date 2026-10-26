using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

public static class DetectionCatalog
{
    public const string Url = "https://discord.com/api/v9/applications/detectable";
    public class Entry
    {
        public string id { get; set; }
        public string added_on { get; set; }
        public string name { get; set; }
        public string[] aliases { get; set; }
        public Executable[] executables { get; set; }
    }
    public class Executable
    {
        public string name { get; set; }
        public string os { get; set; }
        public bool is_launcher { get; set; }
    }
    sealed class CatalogClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            request.Timeout = 20000;
            var http = request as HttpWebRequest;
            if (http != null) http.ReadWriteTimeout = 20000;
            return request;
        }
    }
    static readonly ConcurrentDictionary<string, string[]> titleWords = new ConcurrentDictionary<string, string[]>();
    static string Normalize(string text)
    {
        string normalized = Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\bgta(?=iii\b|ii\b|iv\b|vi\b|v\b)", "gta ");
        normalized = Regex.Replace(normalized, @"\b(iii|ii|iv|vi|v)\b", m => m.Value == "iii" ? "3" : m.Value == "ii" ? "2" : m.Value == "iv" ? "4" : m.Value == "vi" ? "6" : "5");
        normalized = Regex.Replace(normalized, @"(?<=\p{L})(?=\d)|(?<=\d)(?=\p{L})", " ");
        return Regex.Replace(Regex.Replace(normalized, @"\bgta\b", "grand theft auto"), @"\s+", " ").Trim();
    }
    public static string TitleKey(string title) { return Normalize(title); }
    public static Entry[] Fetch()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using (var client = new CatalogClient { Encoding = Encoding.UTF8 }) {
            return Parse(client.DownloadString(Url));
        }
    }
    public static Entry[] Parse(string json)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
        var entries = serializer.Deserialize<Entry[]>(json);
        if (entries == null || entries.Length == 0) throw new FormatException("Discord returned an empty catalog.");
        return entries;
    }
    public static PlaylistItem[] Search(Entry[] entries, string query)
    {
        var matches = (query ?? "").Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).Where(x => x.Length > 0).Distinct().Select(CreateMatcher).ToArray();
        if (matches.Length == 0) return new PlaylistItem[0];
        return entries.Where(HasValidName)
            .Where(x => matches.Any(match => match(x.name) || (x.aliases ?? new string[0]).Any(match)))
            .Select(ToPlaylistItem)
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderBy(x => string.IsNullOrEmpty(x.Path)).First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static PlaylistItem[] AllPlayable(Entry[] entries)
    {
        return entries.Where(HasValidName).Select(ToPlaylistItem)
            .Where(x => !string.IsNullOrEmpty(x.Path)).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static bool HasValidName(Entry entry)
    { return entry != null && !string.IsNullOrWhiteSpace(entry.name) && !entry.name.Contains('"') && !entry.name.Any(char.IsControl); }
    static PlaylistItem ToPlaylistItem(Entry entry)
    { return new PlaylistItem { Name = entry.name, CatalogId = entry.id, Path = WindowsPath(entry), Minutes = 10, DateAdded = entry.added_on }; }
    public static Func<string, bool> TitleMatcher(string query) { return CreateMatcher(Normalize(query)); }
    static Func<string, bool> CreateMatcher(string normalized)
    {
        string[] terms = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        bool[] numeric = terms.Select(term => term.All(char.IsDigit)).ToArray();
        return title => {
            if (terms.Length == 0) return true;
            var words = titleWords.GetOrAdd(title ?? "", x => Normalize(x).Split(' '));
            for (int i = 0; i < terms.Length; i++)
                if (!words.Any(word => numeric[i] ? word == terms[i] : word.StartsWith(terms[i], StringComparison.Ordinal))) return false;
            return true;
        };
    }
    public static string WindowsPath(Entry entry)
    {
        foreach (var executable in entry.executables ?? new Executable[0]) {
            if (executable == null || executable.os != "win32" || executable.is_launcher || string.IsNullOrWhiteSpace(executable.name)) continue;
            try { PlaceholderPaths.Resolve(Environment.CurrentDirectory, executable.name); return executable.name; }
            catch (ArgumentException) { }
            catch (System.IO.IOException) { }
            catch (NotSupportedException) { }
        }
        return "";
    }
}
