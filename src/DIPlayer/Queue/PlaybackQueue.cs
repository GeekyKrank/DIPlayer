using DIPlayer.Core;
namespace DIPlayer.Queue;

public sealed class PlaybackQueue
{
    readonly List<int> shuffled = [], history = [];
    string[] order = [];
    int historyIndex = -1;
    public void ResetNavigation() { order = []; shuffled.Clear(); history.Clear(); historyIndex = -1; }
    public Track? Navigate(int direction, bool repeatAll, bool shuffle)
    {
        if (Tracks.Count == 0) return null;
        if (!shuffle)
        {
            int target = Index + direction;
            if (repeatAll) target = (target % Tracks.Count + Tracks.Count) % Tracks.Count;
            if (target < 0 || target >= Tracks.Count) return null;
            Index = target;
            return Current;
        }
        if (!order.SequenceEqual(Tracks.Select(t => t.Id)) || historyIndex < 0 || history[historyIndex] != Index)
        {
            ResetNavigation(); order = Tracks.Select(t => t.Id).ToArray();
            if (Current != null) { history.Add(Index); historyIndex = 0; }
            FillShuffle();
        }
        if (direction < 0)
        {
            if (historyIndex <= 0) return null;
            Index = history[--historyIndex]; return Current;
        }
        if (historyIndex + 1 < history.Count) { Index = history[++historyIndex]; return Current; }
        if (shuffled.Count == 0)
        {
            if (!repeatAll) return null;
            FillShuffle();
            if (shuffled.Count == 0) return Current;
        }
        Index = shuffled[^1]; shuffled.RemoveAt(shuffled.Count - 1);
        history.Add(Index); historyIndex = history.Count - 1;
        if (history.Count > 1000) { history.RemoveAt(0); historyIndex--; }
        return Current;
    }
    void FillShuffle()
    {
        shuffled.Clear();
        for (int i = 0; i < Tracks.Count; i++) if (i != Index) shuffled.Add(i);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
    }
    public List<Track> Tracks { get; } = [];
    public int Index { get; set; } = -1;
    public Track? Current => Index >= 0 && Index < Tracks.Count ? Tracks[Index] : null;
    public void Add(Track t)
    {
        if (Tracks.Count >= 1000)
            throw new CatalogException("The queue is limited to 1,000 tracks.");
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
