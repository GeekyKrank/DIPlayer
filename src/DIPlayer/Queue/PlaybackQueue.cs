using DIPlayer.Core;
namespace DIPlayer.Queue;

public sealed class PlaybackQueue
{
    public List<Track> Tracks { get; } = [];
    public int Index { get; set; } = -1;
    public Track? Current => Index >= 0 && Index < Tracks.Count ? Tracks[Index] : null;
    public void Add(Track t)
    {
        if (Tracks.Count >= 1000)
            throw new CatalogException("Очередь ограничена 1000 треками.");
        Tracks.Add(t);
    }
    public Track? Next(int direction)
    {
        int target = Index + direction;
        if (target < 0 || target >= Tracks.Count)
            return null;
        Index = target;
        return Current;
    }
    public void Remove(int index)
    {
        Tracks.RemoveAt(index);
        if (index < Index)
            Index--;
        else if (index == Index)
            Index = Math.Min(Index - 1, Tracks.Count - 1);
    }
    public void Move(int index, int direction)
    {
        int to = index + direction;
        if (to < 0 || to >= Tracks.Count)
            return;
        (Tracks[to], Tracks[index]) = (Tracks[index], Tracks[to]);
        if (Index == index)
            Index = to;
        else if (Index == to)
            Index = index;
    }
}
