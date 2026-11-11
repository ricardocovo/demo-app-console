using System.Globalization;
using System.Text.Json;

namespace SoccerCli;

public static class Cli
{
    private const string DefaultBaseUrl = "http://localhost:5249";
    private const string Help = """
        Usage: soccer-cli [--base-url URL] RESOURCE COMMAND [ID] [OPTIONS]

        championships list | get ID | teams ID
        teams         list [--championship-id ID] | get ID | players ID | games ID
        players       list [--team-id ID] | get ID | totals ID [--championship-id ID]
        games         list [--team-id ID] | get ID | stats ID
        player-stats  list [--game-id ID] [--player-id ID] | get ID

        Set SOCCER_API_BASE_URL or use --base-url to select the API.
        """;

    public static async Task<int> RunAsync(string[] args, HttpClient client, TextWriter output,
        TextWriter error, string? environmentBaseUrl = null)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            await output.WriteLineAsync(Help);
            return 0;
        }

        try
        {
            var (url, path) = Parse(args, environmentBaseUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, path));
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                await error.WriteLineAsync($"API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
                if (!string.IsNullOrWhiteSpace(body))
                    await error.WriteLineAsync(body);
                return 1;
            }

            using var document = JsonDocument.Parse(body);
            await output.WriteLineAsync(JsonSerializer.Serialize(document.RootElement,
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (ArgumentException ex)
        {
            await error.WriteLineAsync($"{ex.Message}\nRun with --help for usage.");
            return 2;
        }
        catch (HttpRequestException ex)
        {
            await error.WriteLineAsync($"Could not reach the API: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("The API request timed out or was canceled.");
            return 1;
        }
        catch (JsonException)
        {
            await error.WriteLineAsync("The API returned invalid JSON.");
            return 1;
        }
    }

    private static (Uri BaseUrl, string Path) Parse(string[] args, string? environmentBaseUrl)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Option {arg} requires a value.");
            if (!options.TryAdd(arg, args[++i]))
                throw new ArgumentException($"Option {arg} was supplied more than once.");
        }

        var baseUrl = options.Remove("--base-url", out var suppliedBaseUrl)
            ? suppliedBaseUrl : environmentBaseUrl ?? DefaultBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Base URL must be an absolute HTTP(S) URL without credentials, query, or fragment.");
        var normalizedBase = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");

        if (positional.Count < 2)
            throw new ArgumentException("A resource and command are required.");
        var resource = positional[0];
        var command = positional[1];
        var (needsId, suffix, filters) = (resource, command) switch
        {
            ("championships", "list") => (false, "", Array.Empty<string>()),
            ("championships", "get") => (true, "", Array.Empty<string>()),
            ("championships", "teams") => (true, "/teams", Array.Empty<string>()),
            ("teams", "list") => (false, "", new[] { "--championship-id" }),
            ("teams", "get") => (true, "", Array.Empty<string>()),
            ("teams", "players") => (true, "/players", Array.Empty<string>()),
            ("teams", "games") => (true, "/games", Array.Empty<string>()),
            ("players", "list") => (false, "", new[] { "--team-id" }),
            ("players", "get") => (true, "", Array.Empty<string>()),
            ("players", "totals") => (true, "/totals", new[] { "--championship-id" }),
            ("games", "list") => (false, "", new[] { "--team-id" }),
            ("games", "get") => (true, "", Array.Empty<string>()),
            ("games", "stats") => (true, "/stats", Array.Empty<string>()),
            ("player-stats", "list") => (false, "", new[] { "--game-id", "--player-id" }),
            ("player-stats", "get") => (true, "", Array.Empty<string>()),
            _ => throw new ArgumentException("Unknown resource or command.")
        };
        if (positional.Count != (needsId ? 3 : 2))
            throw new ArgumentException(needsId ? "This command requires exactly one ID." : "This command does not accept an ID.");

        var path = resource;
        if (needsId)
            path += "/" + ParseId(positional[2], "ID");
        path += suffix;

        var query = new List<string>();
        foreach (var filter in filters)
        {
            if (!options.Remove(filter, out var value)) continue;
            var key = filter switch
            {
                "--championship-id" => "championshipId",
                "--team-id" => "teamId",
                "--game-id" => "gameId",
                "--player-id" => "playerId",
                _ => throw new InvalidOperationException("Unsupported filter.")
            };
            query.Add($"{key}={ParseId(value, filter)}");
        }
        if (options.Count != 0)
            throw new ArgumentException($"Unknown or unsupported option: {options.Keys.First()}.");
        if (query.Count != 0)
            path += "?" + string.Join("&", query);
        return (normalizedBase, path);
    }

    private static string ParseId(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ArgumentException($"{name} must be a positive integer.");
        return id.ToString(CultureInfo.InvariantCulture);
    }
}
