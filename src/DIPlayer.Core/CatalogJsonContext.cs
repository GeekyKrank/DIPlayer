using System.Text.Json.Serialization;
namespace DIPlayer.Core;
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,IgnoreReadOnlyProperties=true)]
[JsonSerializable(typeof(Track))]
[JsonSerializable(typeof(TrackPage))]
[JsonSerializable(typeof(PlaylistInfo))]
[JsonSerializable(typeof(List<Track>))]
[JsonSerializable(typeof(string))]
public partial class CatalogJsonContext : JsonSerializerContext;
