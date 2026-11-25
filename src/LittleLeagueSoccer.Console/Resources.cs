using System.Globalization;
using System.Text.Json;

namespace LittleLeagueSoccer.ConsoleApp;

public sealed record Resource(string Path, string Name, string Accent, string[] Columns)
{
    public static readonly Resource Championships = new("championships", "Championships", "gold1", ["id", "name", "year"]);
    public static readonly Resource Teams = new("teams", "Teams", "springgreen2", ["id", "name", "championshipId"]);
    public static readonly Resource Players = new("players", "Players", "deepskyblue1", ["id", "name", "age", "teamId"]);
    public static readonly Resource Games = new("games", "Games", "orchid", ["id", "date", "teamAId", "teamBId"]);
    public static readonly Resource Stats = new("player-stats", "Player stats", "lightsalmon1",
        ["id", "gameId", "playerId", "position", "minutesPlayed", "starter", "goals", "assists"]);
    public static IReadOnlyList<Resource> All { get; } = [Championships, Teams, Players, Games, Stats];

    public static Resource Parse(string value) =>
        All.FirstOrDefault(r => r.Path == value.ToLowerInvariant())
        ?? throw new ArgumentException($"Unknown resource '{value}'. Use: {string.Join(", ", All.Select(r => r.Path))}.");
}

public sealed record ApiView(string Path, string Title, Resource Resource, bool IsList, bool IsEntity = false)
{
    public static ApiView List(Resource resource) => new(resource.Path, resource.Name, resource, true);
    public static ApiView Detail(Resource resource, int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        return new($"{resource.Path}/{id}", $"{resource.Name} / #{id}", resource, false, true);
    }

    public IReadOnlyList<ApiView> Links(JsonElement entity)
    {
        if (!IsEntity) return [];
        var id = entity.GetProperty("id").GetInt32();
        if (Resource == Resource.Championships)
            return [new($"championships/{id}/teams", "Championship teams", Resource.Teams, true)];
        if (Resource == Resource.Teams)
            return [Detail(Resource.Championships, entity.GetProperty("championshipId").GetInt32()),
                new($"teams/{id}/players", "Team roster", Resource.Players, true),
                new($"teams/{id}/games", "Team fixtures", Resource.Games, true)];
        if (Resource == Resource.Players)
            return [Detail(Resource.Teams, entity.GetProperty("teamId").GetInt32()),
                new($"players/{id}/totals", "Career totals", Resource.Players, false),
                new($"player-stats?playerId={id}", "Player match stats", Resource.Stats, true)];
        if (Resource == Resource.Games)
            return [Detail(Resource.Teams, entity.GetProperty("teamAId").GetInt32()),
                Detail(Resource.Teams, entity.GetProperty("teamBId").GetInt32()),
                new($"games/{id}/stats", "Match stat lines", Resource.Stats, true)];
        return [Detail(Resource.Games, entity.GetProperty("gameId").GetInt32()),
            Detail(Resource.Players, entity.GetProperty("playerId").GetInt32())];
    }
}

public static class Display
{
    public static string Safe(string value) =>
        new(value.Where(c => !char.IsControl(c) && c is not '\u2028' and not '\u2029'
            && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray());

    public static string Value(JsonElement value) => Safe(value.ValueKind == JsonValueKind.String
        ? value.GetString()! : value.ToString());

    public static string Label(string field) => string.Concat(field.Select((c, i) =>
        i > 0 && char.IsUpper(c) ? " " + c : c.ToString())).ToUpperInvariant();

    public static List<JsonElement> Filter(JsonElement data, string search) =>
        data.EnumerateArray().Where(row => row.EnumerateObject()
            .Any(p => Value(p.Value).Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
}
