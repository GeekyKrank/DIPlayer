using DIPlayer;

static class SearchSuggestionsChecks
{
    public static void Run(Action<bool,string> check)
    {
        var history=SearchSuggestionsModel.Remember(Enumerable.Range(0,20).Select(i=>"Old query "+i),"  Daft Punk  ");
        check(history.Length==12&&history[0]=="Daft Punk","search history is bounded and newest query is first");
        var repeated=SearchSuggestionsModel.Remember(history,"daft punk");
        check(repeated.Length==12&&repeated.Count(value=>value.Equals("Daft Punk",StringComparison.OrdinalIgnoreCase))==1,"search history deduplicates case variants");
        SearchCandidate[] tracks=[new("Get Lucky","Daft Punk"),new("Digital Love","Daft Punk"),new("Awake","Tycho"),new("Daft Punk","",true)];
        var result=SearchSuggestionsModel.Build(true,"daft",["Daft Punk live","Tycho"],tracks);
        check(result.Any(item=>item.Group=="Recent search")&&result.Any(item=>item.Label=="Get Lucky"&&item.Group=="Track")&&result.Any(item=>item.Label=="Daft Punk"&&item.Group=="Artist"),"search suggestions combine recent requests, real tracks and related artists");
        check(result.All(item=>item.YouTube)&&result.All(item=>!item.Label.Contains("Tycho")),"suggestions retain their service and filter unrelated cached tracks");
        var related=SearchSuggestionsModel.Build(false,"music for work",[],[new("Weightless","Marconi Union")],true);
        check(related.Any(item=>item.Query=="Marconi Union")&&related.All(item=>!item.YouTube),"completed search results supply related artists without another network query");
        var empty=SearchSuggestionsModel.Build(false,"",["Only recent"],tracks);
        check(empty.Length==1&&empty[0].Group=="Recent search","empty search shows recent requests without inventing recommendations");
        var bounded=SearchSuggestionsModel.Build(true,"query",Enumerable.Range(0,40).Select(i=>"query "+i),Enumerable.Range(0,80).Select(i=>new SearchCandidate("query "+i,"Artist "+i)));
        check(bounded.Length<=8&&bounded.Select(item=>item.Query).Distinct(StringComparer.OrdinalIgnoreCase).Count()==bounded.Length,"search menu stays bounded and has no duplicate actions");
    }
}
