using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DIPlayer.Core;
using Track = DIPlayer.Core.Track;

namespace DIPlayer;
public partial class MainWindow
{
    CancellationTokenSource? searchSuggestionsCancel;
    readonly DispatcherTimer searchHistorySaveTimer=new(){Interval=TimeSpan.FromMilliseconds(600)};
    SearchCandidate[] soundCloudSearchCandidates=[],youTubeSearchCandidates=[];
    string soundCloudCandidatesQuery="",youTubeCandidatesQuery="";
    bool suppressSearchTextChange,searchExplicitCommit;
    Popup? SuggestionsPopup=>FindName("SearchSuggestionsPopup") as Popup;
    ListBox? SuggestionsList=>FindName("SearchSuggestionsList") as ListBox;
    TextBox SearchInput=>browserMode?YouTubeSearch:SearchBox;

    void InitializeSearchExperience()
    {
        searchHistorySaveTimer.Tick+=(_,_)=>
        {
            searchHistorySaveTimer.Stop();
            if(DiagnosticLibraryDirectory()==null)_ = Guard(()=>{settings.Save();return Task.CompletedTask;});
        };
        Closed+=(_,_)=>{searchSuggestionsCancel?.Cancel();searchSuggestionsCancel?.Dispose();searchHistorySaveTimer.Stop();};
        Deactivated+=(_,_)=>CloseSearchSuggestions();
    }
    void CloseSearchSuggestions()
    {
        searchSuggestionsCancel?.Cancel();
        if(SuggestionsPopup is { } popup)popup.IsOpen=false;
    }
    void SearchInputFocused(object sender,KeyboardFocusChangedEventArgs e)
    {
        if(sender==SearchInput)QueueSearchSuggestions();
    }
    void YouTubeSearchChanged(object sender,TextChangedEventArgs e)
    {
        if(!suppressSearchTextChange&&browserMode)QueueSearchSuggestions();
    }
    void QueueSearchSuggestions()
    {
        searchSuggestionsCancel?.Cancel();searchSuggestionsCancel?.Dispose();searchSuggestionsCancel=new();
        var token=searchSuggestionsCancel.Token;
        bool youtube=browserMode;string text=SearchInput.Text;
        _ = Guard(async()=>
        {
            await Task.Delay(180,token);
            if(youtube!=browserMode||text!=SearchInput.Text||!IsVisible||!IsActive||!SearchInput.IsKeyboardFocusWithin)return;
            ShowSearchSuggestions();
        });
    }
    void ShowSearchSuggestions()
    {
        if(SuggestionsPopup is not { } popup||SuggestionsList is not { } list)return;
        string text=SearchInput.Text.Trim();
        var history=browserMode?settings.SearchHistoryYT:settings.SearchHistorySC;
        var catalog=browserMode?youTubeSearchCandidates:soundCloudSearchCandidates;
        string resultQuery=browserMode?youTubeCandidatesQuery:soundCloudCandidatesQuery;
        list.ItemsSource=SearchSuggestionsModel.Build(browserMode,text,history,catalog,text.Length>0&&text.Equals(resultQuery,StringComparison.CurrentCultureIgnoreCase));
        list.SelectedIndex=-1;
        popup.PlacementTarget=SearchInput;
        popup.Width=Math.Max(220,SearchInput.ActualWidth);
        popup.IsOpen=list.Items.Count>0&&SearchInput.IsVisible;
    }
    bool HandleSearchSuggestionKey(KeyEventArgs e)
    {
        if(e.Key==Key.Escape)
        {
            bool open=SuggestionsPopup?.IsOpen==true;CloseSearchSuggestions();
            if(open)e.Handled=true;return open;
        }
        if(e.Key==Key.Tab){CloseSearchSuggestions();return false;}
        if(e.Key is Key.Down or Key.Up)
        {
            if(SuggestionsPopup?.IsOpen!=true)ShowSearchSuggestions();
            if(SuggestionsList is not {Items.Count:>0} list||SuggestionsPopup?.IsOpen!=true)return false;
            int index=list.SelectedIndex;
            list.SelectedIndex=e.Key==Key.Down?(index+1)%list.Items.Count:index<=0?list.Items.Count-1:index-1;
            list.ScrollIntoView(list.SelectedItem);e.Handled=true;return true;
        }
        if(e.Key==Key.Enter&&SuggestionsPopup?.IsOpen==true&&SuggestionsList?.SelectedItem is SearchSuggestion choice)
        {
            e.Handled=true;_ = Guard(()=>SubmitSearchSuggestion(choice));return true;
        }
        return false;
    }
    void SearchSuggestionListKey(object sender,KeyEventArgs e)=>HandleSearchSuggestionKey(e);
    async void ChooseSearchSuggestion(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left||sender is not ListBox list||e.OriginalSource is not DependencyObject node)return;
        if(ItemsControl.ContainerFromElement(list,node) is not ListBoxItem {Content:SearchSuggestion choice})return;
        e.Handled=true;await Guard(()=>SubmitSearchSuggestion(choice));
    }
    async Task SubmitSearchSuggestion(SearchSuggestion choice)
    {
        if(choice.YouTube!=browserMode){CloseSearchSuggestions();return;}
        suppressSearchTextChange=true;
        try{SearchInput.Text=choice.Query;SearchInput.CaretIndex=SearchInput.Text.Length;}
        finally{suppressSearchTextChange=false;}
        if(browserMode)await CommitYouTubeSearch(choice.Query);
        else await CommitSoundCloudSearch(choice.Query);
    }
    void RememberSearch(bool youtube,string text)
    {
        if(youtube)settings.SearchHistoryYT=SearchSuggestionsModel.Remember(settings.SearchHistoryYT,text);
        else settings.SearchHistorySC=SearchSuggestionsModel.Remember(settings.SearchHistorySC,text);
        searchHistorySaveTimer.Stop();searchHistorySaveTimer.Start();
    }
    async Task CommitSoundCloudSearch(string text)
    {
        text=text.Trim();if(text.Length==0)return;
        CloseSearchSuggestions();searchExplicitCommit=true;RememberSearch(false,text);
        searchCancel?.Cancel();searchCancel=new();query=text;page=0;cursors.Clear();cursors.Add(null);next=null;
        SetView("search");
        await LoadSearch(searchCancel.Token,++searchGeneration);
    }
    async Task CommitYouTubeSearch(string text)
    {
        text=text.Trim();if(text.Length==0)return;
        CloseSearchSuggestions();RememberSearch(true,text);
        await NavigateYouTube("/search?q="+Uri.EscapeDataString(text),"Search results");
    }
    void RefreshSearchSuggestionsFromYouTube()
    {
        youTubeSearchCandidates=youtubeItems.Where(item=>item.Kind is "Track" or "Artist").Take(80).Select(item=>new SearchCandidate(item.Title,
            item.ArtistName??(item.Kind=="Artist"?item.Title:item.Subtitle.Split(['•','·'])[0].Trim()),item.Kind=="Artist")).ToArray();
        youTubeCandidatesQuery=new Uri(youtubeDisplayedUrl).AbsolutePath=="/search"?YouTubeSearch.Text.Trim():"";
        if(browserMode&&SuggestionsPopup?.IsOpen==true&&YouTubeSearch.IsKeyboardFocusWithin)QueueSearchSuggestions();
    }
    void RefreshSearchSuggestionsFromSoundCloud(string searched,IEnumerable<Track> tracks)
    {
        soundCloudSearchCandidates=tracks.Take(80).Select(track=>new SearchCandidate(track.Title,track.Artist)).ToArray();
        soundCloudCandidatesQuery=searched;
        if(!searchExplicitCommit&&SearchBox.IsKeyboardFocusWithin&&view=="search")QueueSearchSuggestions();
    }
}
