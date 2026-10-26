using System;
using System.Collections.Generic;
using System.Linq;

public class PlaylistItem
{
    public string Name { get; set; }
    public string CatalogId { get; set; }
    public string Path { get; set; }
    public decimal Minutes { get; set; }
    public string DateAdded { get; set; }
    public override string ToString() { return Name; }
    public PlaylistItem Copy() { return new PlaylistItem { Name = Name, CatalogId = CatalogId, Path = Path, Minutes = Minutes, DateAdded = DateAdded }; }
}

public class SavedPlaylist
{
    public bool Repeat { get; set; }
    public bool RandomPlay { get; set; }
    public bool RandomPause { get; set; }
    public bool Shuffle { get; set; }
    public List<string> OriginalOrder { get; set; }
    public decimal PlayMin { get; set; }
    public decimal PlayMax { get; set; }
    public decimal PauseMin { get; set; }
    public decimal PauseMax { get; set; }
    public List<PlaylistItem> Items { get; set; }
    public SavedPlaylist() { Items = new List<PlaylistItem>(); OriginalOrder = new List<string>(); PlayMin = 5; PlayMax = 15; PauseMin = 10m / 60m; PauseMax = 30m / 60m; }
}

// A run is a snapshot: editing the saved playlist never changes an active timer.
public class PlaylistRun
{
    public readonly List<PlaylistItem> Items;
    public int Index { get; private set; }
    public PlaylistRun(IEnumerable<PlaylistItem> items)
    {
        Items = items.Select(x => x.Copy()).ToList();
        if (Items.Count == 0 || Items.Any(x => x.Minutes < 1m / 60m || x.Minutes > 10080m))
            throw new ArgumentException("Add a game with a duration between one second and 10080 minutes.");
    }
    public PlaylistItem Current { get { return Items[Index]; } }
    public bool Advance(bool repeat)
    {
        if (Index + 1 < Items.Count) { Index++; return true; }
        if (repeat) { Index = 0; return true; }
        return false;
    }
}
