using System.IO;
using System.Xml.Serialization;

public sealed class AppSettings
{
    public bool PlayInBackground { get; set; }
    public bool CountdownEnabled { get; set; }

    internal static AppSettings Load(string root)
    {
        string path = Path.Combine(root, "app-settings.xml");
        if (!File.Exists(path)) return new AppSettings();
        using (var stream = File.OpenRead(path))
            return (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(stream);
    }

    internal void Save(string root)
    {
        AtomicFile.Write(Path.Combine(root, "app-settings.xml"), stream => new XmlSerializer(typeof(AppSettings)).Serialize(stream, this));
    }
}
