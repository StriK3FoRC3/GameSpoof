using System;
using System.Linq;

static class CatalogTests
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
    static int Main(string[] args)
    {
        try {
            string json = @"[
                {'name':'Call of Duty: Black Ops III','executables':[{'name':'blackops3.exe','os':'win32','is_launcher':false},{'name':'launcher.exe','os':'win32','is_launcher':true},{'name':'linux-only','os':'linux','is_launcher':false}]},
                {'name':'Call of Duty: Black Ops II','executables':[{'name':'t6sp.exe','os':'win32','is_launcher':false}]},
                {'name':'Call of Duty: Black Ops','executables':[{'name':'blackops.exe','os':'win32','is_launcher':false}]},
                {'name':'Atomic Heart','executables':[{'name':'win64/atomicheart-win64-shipping.exe','os':'win32','is_launcher':false}]},
                {'name':'Unavailable Game','executables':[]},
                {'name':'Unrelated title','executables':[]}
            ]".Replace('\'', '"');
            var entries = DetectionCatalog.Parse(json);
            var results = DetectionCatalog.Search(entries, "Atomic Heart, Black Ops 3\nBlack Ops 2");
            Check(results.Length == 3 && results.Any(x => x.Path == "blackops3.exe") && results.Any(x => x.Path == "t6sp.exe"), "Roman numerals and multiple queries find games");
            Check(results.Any(x => x.Path == "win64/atomicheart-win64-shipping.exe"), "complete relative paths are preserved");
            Check(!results.Any(x => x.Path == "launcher.exe" || x.Path == "linux-only" || x.Name == "Unrelated title"), "only relevant Windows game records are included");
            Check(DetectionCatalog.Search(entries, "Black Ops").Length == 3, "broad searches show sequels for user selection");
            Check(DetectionCatalog.Search(entries, "Unavailable Game").Single().Path == "", "unavailable catalog entries are marked");
            Check(DetectionCatalog.Search(entries, "Unknown Game").Length == 0, "unmatched search returns no invented entry");
            Check(DetectionCatalog.Search(entries, " ").Length == 0, "empty search stays empty");
            var unsafeEntries = DetectionCatalog.Parse("[{'name':'Unsafe Game','executables':[{'name':'../escape.exe','os':'win32'}]}]".Replace('\'', '"'));
            Check(DetectionCatalog.Search(unsafeEntries, "Unsafe").Single().Path == "", "unsafe executable paths cannot be added");
            bool rejected = false;
            try { DetectionCatalog.Parse("not JSON"); } catch { rejected = true; }
            Check(rejected, "invalid response fails");
            var gta = DetectionCatalog.Parse(@"[
                {'name':'Grand Theft Auto IV','aliases':['Grand Theft Auto IV: The Complete Edition'],'executables':[]},
                {'name':'Grand Theft Auto V Legacy','executables':[]},
                {'name':'Grand Theft Auto V Enhanced','executables':[]},
                {'name':'Grand Theft Auto: San Andreas','executables':[]}
            ]".Replace('\'', '"'));
            Check(DetectionCatalog.Search(gta, "gta").Length == 4, "GTA abbreviation finds full series titles");
            Check(DetectionCatalog.Search(gta, "gta5").Length == 2 && DetectionCatalog.Search(gta, "GTA V Enhanced").Single().Name.EndsWith("Enhanced"), "GTA numbers and editions match");
            Check(DetectionCatalog.Search(gta, "gta4").Single().Name == "Grand Theft Auto IV" && DetectionCatalog.Search(gta, "GTAIV").Length == 1, "GTA IV supports numeric and joined abbreviations");
            Check(DetectionCatalog.Search(gta, "Complete Edition").Length == 1, "Discord aliases are searchable");
            Check(DetectionCatalog.Search(gta, "grand thef aut").Length == 4, "partial title words are searchable");
            Check(DetectionCatalog.TitleMatcher("gta5")("Grand Theft Auto V Legacy"), "Library uses GTA search matching too");
            if (args.Contains("--live")) {
                var downloaded = DetectionCatalog.Fetch();
                var live = DetectionCatalog.Search(downloaded, "Atomic Heart, Black Ops");
                Check(live.Any(x => x.Path.Contains("atomicheart")) && live.Any(x => x.Name == "Call of Duty: Black Ops III" && x.Path.Length > 0) && live.Any(x => x.Name == "Call of Duty: Black Ops II" && x.Path.Length > 0) && live.Any(x => x.Name == "Call of Duty: Black Ops" && x.Path.Length > 0), "live catalog supplies all four requested games");
                var liveGta = DetectionCatalog.Search(downloaded, "gta");
                Check(liveGta.Any(x => x.Name.Contains("San Andreas")) && liveGta.Any(x => x.Name == "Grand Theft Auto IV") && DetectionCatalog.Search(downloaded, "gta5").Any(x => x.Path.Length > 0), "live GTA catalog searches return playable matches");
            }
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
