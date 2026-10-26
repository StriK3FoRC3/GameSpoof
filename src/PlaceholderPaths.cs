using System;
using System.IO;
using System.Linq;

public static class PlaceholderPaths
{
    static readonly string[] ReservedNames = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
    static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();
    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) throw new ArgumentException("Enter an executable path.");
        relative = relative.Replace('/', '\\');
        if (Path.IsPathRooted(relative) || relative.Length > 180 || !relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a relative executable path, such as win64/cs2.exe.");
        foreach (string part in relative.Split('\\'))
            if (string.IsNullOrWhiteSpace(part) || part == "." || part == ".." || part != part.TrimEnd(' ', '.') ||
                part.IndexOfAny(InvalidNameChars) >= 0 || ReservedNames.Contains(part.Split('.')[0].ToUpperInvariant()))
                throw new ArgumentException("The executable path contains an invalid folder or filename.");
        if (Path.GetFileName(relative).Length <= 4) throw new ArgumentException("Enter an executable filename.");
        string directory = Path.GetFullPath(Path.Combine(root, "SpoofedGames")) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(directory, relative));
        if (!target.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Path must stay inside SpoofedGames.");
        return target;
    }
}
