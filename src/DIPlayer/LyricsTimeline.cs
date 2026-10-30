using System.Globalization;
using System.Text.RegularExpressions;
namespace DIPlayer;
public sealed record TimedLyric(double Start,string Text);
public static class LyricsTimeline
{
    public static TimedLyric[] Parse(string lrc)
    {
        double offset=0;
        var match=Regex.Match(lrc,@"\[offset:([+-]?\d+)\]",RegexOptions.IgnoreCase);
        if(match.Success&&double.TryParse(match.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var ms))offset=ms/1000;
        var result=new List<TimedLyric>();
        foreach(string line in lrc.Split('\n').Take(2000))
        {
            var stamps=Regex.Matches(line,@"\[(\d{1,3}):(\d{2}(?:[.,]\d{1,3})?)\]");
            string text=Regex.Replace(line,@"\[[^\]]+\]","").Trim();
            foreach(Match stamp in stamps)
            {
                double minutes=double.Parse(stamp.Groups[1].Value,CultureInfo.InvariantCulture);
                double seconds=double.Parse(stamp.Groups[2].Value.Replace(',','.'),CultureInfo.InvariantCulture);
                if(seconds<60)result.Add(new(Math.Max(0,minutes*60+seconds-offset),text));
            }
        }
        return result.OrderBy(l=>l.Start).Take(1000).ToArray();
    }
    public static int ActiveIndex(IReadOnlyList<TimedLyric> lines,double position)
    {
        if(!double.IsFinite(position))return -1;
        int low=0,high=lines.Count-1,found=-1;
        while(low<=high){int mid=low+(high-low)/2;if(lines[mid].Start<=position){found=mid;low=mid+1;}else high=mid-1;}
        return found;
    }
}
