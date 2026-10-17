using System.Text.Json;
using System.Text.RegularExpressions;

namespace DIPlayer.Core;

public sealed record UpdateRelease(string Tag, string Url);

public static class ReleaseUpdates
{
    static (Version Number, string? Pre)? Parse(string value)
    {
        var m = Regex.Match(value, @"^v?(\d+\.\d+\.\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$");
        return m.Success && Version.TryParse(m.Groups[1].Value, out var number)
            ? (number, m.Groups[2].Success ? m.Groups[2].Value : null) : null;
    }
    public static int Compare(string left, string right)
    {
        var a = Parse(left) ?? throw new ArgumentException("Invalid release version.");
        var b = Parse(right) ?? throw new ArgumentException("Invalid installed version.");
        int result = a.Number.CompareTo(b.Number);
        if (result != 0) return result;
        if (a.Pre == null || b.Pre == null) return a.Pre == b.Pre ? 0 : a.Pre == null ? 1 : -1;
        var aa = a.Pre.Split('.'); var bb = b.Pre.Split('.');
        for (int i = 0; i < Math.Min(aa.Length, bb.Length); i++)
        {
            bool an = ulong.TryParse(aa[i], out var av), bn = ulong.TryParse(bb[i], out var bv);
            result = an && bn ? av.CompareTo(bv) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(aa[i], bb[i]);
            if (result != 0) return result;
        }
        return aa.Length.CompareTo(bb.Length);
    }
    public static UpdateRelease? Find(JsonElement releases, string installed)
    {
        if (Parse(installed) == null || releases.ValueKind != JsonValueKind.Array) return null;
        string? best = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False) continue;
            if (!release.TryGetProperty("tag_name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            string? tag = name.GetString();
            if (tag == null || Parse(tag) == null || Compare(tag, installed) <= 0) continue;
            if (best == null || Compare(tag, best) > 0) best = tag;
        }
        return best == null ? null : new(best, "https://github.com/GeekyKrank/DIPlayer/releases/tag/" + Uri.EscapeDataString(best));
    }
}
