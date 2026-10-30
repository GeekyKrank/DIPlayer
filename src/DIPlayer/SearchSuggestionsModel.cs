namespace DIPlayer;

public sealed record SearchCandidate(string Title,string Artist,bool ArtistOnly=false);
public sealed record SearchSuggestion(string Query,string Label,string Detail,string Group,bool YouTube)
{
    public string Icon=>Group=="Recent search"?UI.Symbols.Clock:Group=="Artist"?UI.Symbols.User:UI.Symbols.Search;
}

public static class SearchSuggestionsModel
{
    public static string[] Remember(IEnumerable<string>? history,string query)
    {
        query=query.Trim();
        if(query.Length is 0 or >200)return (history??[]).Where(Valid).Select(value=>value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
        return new[]{query}.Concat(history??[]).Where(Valid).Select(value=>value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
    }
    static bool Valid(string? value)=>!string.IsNullOrWhiteSpace(value)&&value.Trim().Length<=200;

    public static SearchSuggestion[] Build(bool youtube,string query,IEnumerable<string>? history,IEnumerable<SearchCandidate> catalog,bool currentResults=false)
    {
        query=query.Trim();
        if(query.Length>200)return [];
        var result=new List<SearchSuggestion>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string value,string label,string detail,string group)
        {
            value=value.Trim();
            if(Valid(value)&&seen.Add(value))result.Add(new(value,label,detail,group,youtube));
        }
        string service=youtube?"YouTube Music":"SoundCloud";
        foreach(string recent in (history??[]).Where(Valid).Select(value=>value.Trim()).Where(value=>query.Length==0||value.Contains(query,StringComparison.CurrentCultureIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Take(query.Length==0?8:3))
            Add(recent,recent,service,"Recent search");
        if(query.Length==0)return result.ToArray();
        var words=query.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var candidates=catalog.Where(item=>Valid(item.Title))
            .Where(item=>currentResults||words.All(word=>(item.Title+" "+item.Artist).Contains(word,StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(item=>item.Title.StartsWith(query,StringComparison.CurrentCultureIgnoreCase))
            .Take(64).ToArray();
        foreach(var track in candidates.Where(item=>!item.ArtistOnly).Take(4))
            Add((track.Title+" "+track.Artist).Trim(),track.Title,track.Artist,"Track");
        foreach(string artist in candidates.Select(item=>item.ArtistOnly?item.Title:item.Artist).Where(Valid).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
            Add(artist,artist,"Search this artist","Artist");
        return result.Take(8).ToArray();
    }
}
