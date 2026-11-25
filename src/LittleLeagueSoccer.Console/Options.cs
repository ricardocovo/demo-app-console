namespace LittleLeagueSoccer.ConsoleApp;

public sealed record Options(Uri BaseUrl, bool Snapshot, ApiView? View, bool Help)
{
    public const string Usage = """
        Little League Soccer | read-only match centre

        dotnet run --project src\LittleLeagueSoccer.Console -- [options]

        --url <http(s)://host:port/>  API URL (or SOCCER_API_URL; default http://localhost:5000/)
        --snapshot                  Print once without interactive prompts (CI / terminal capture)
        --resource <name>            Open championships, teams, players, games, or player-stats
        --id <positive integer>     Open a resource's detail (requires --resource)
        --help                      Show this help

        Interactive: arrow keys to choose, type to filter actions, Enter to open.
        Search, previous/next page, refresh, related records, back, and quit are menu actions.
        Ctrl+C exits. Only GET requests are sent. No data is written to the API.
        """;

    public static Options Parse(string[] args, string? environmentUrl)
    {
        var url = environmentUrl ?? "http://localhost:5000/";
        var snapshot = false;
        var help = false;
        Resource? resource = null;
        int? id = null;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {args[i - 1]}.");
            switch (args[i])
            {
                case "--help": case "-h": help = true; break;
                case "--snapshot": snapshot = true; break;
                case "--url": url = Next(); break;
                case "--resource": resource = Resource.Parse(Next()); break;
                case "--id":
                    if (!int.TryParse(Next(), out var parsed) || parsed <= 0)
                        throw new ArgumentException("--id must be a positive integer.");
                    id = parsed;
                    break;
                default: throw new ArgumentException($"Unknown option '{args[i]}'. Use --help.");
            }
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("API URL must be an absolute HTTP(S) URL without credentials, query, or fragment.");
        if (id.HasValue && resource is null) throw new ArgumentException("--id requires --resource.");
        uri = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        return new(uri, snapshot, resource is null ? null : id.HasValue
            ? ApiView.Detail(resource, id.Value) : ApiView.List(resource), help);
    }
}
