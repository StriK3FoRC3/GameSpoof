using System;
using System.IO;

// Commit a complete file only after writing succeeds; retain the previous file on failure.
static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        path = Path.GetFullPath(path);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = File.Create(temp)) write(stream);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
