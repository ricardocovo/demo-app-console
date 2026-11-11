using System.Net;
using System.Text;
using SoccerCli;

var routes = new (string Command, string Path)[]
{
    ("championships list", "championships"),
    ("championships get 1", "championships/1"),
    ("championships teams 1", "championships/1/teams"),
    ("teams list", "teams"),
    ("teams list --championship-id 2", "teams?championshipId=2"),
    ("teams get 1", "teams/1"),
    ("teams players 1", "teams/1/players"),
    ("teams games 1", "teams/1/games"),
    ("players list --team-id 2", "players?teamId=2"),
    ("players get 1", "players/1"),
    ("players totals 1 --championship-id 2", "players/1/totals?championshipId=2"),
    ("games list --team-id 2", "games?teamId=2"),
    ("games get 1", "games/1"),
    ("games stats 1", "games/1/stats"),
    ("player-stats list --player-id 3 --game-id 2", "player-stats?gameId=2&playerId=3"),
    ("player-stats get 1", "player-stats/1")
};

foreach (var (command, path) in routes)
{
    using var handler = new FakeHandler(HttpStatusCode.OK, """{"futureField":"preserved","items":[1]}""");
    using var client = new HttpClient(handler);
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await Cli.RunAsync(command.Split(' '), client, output, error, "https://example.test/api");
    Check(code == 0 && error.ToString() == "", command);
    Check(handler.Method == HttpMethod.Get && handler.Uri?.AbsoluteUri == $"https://example.test/api/{path}", command);
    Check(output.ToString().Contains("\"futureField\": \"preserved\""), "full JSON output");
}

foreach (var command in new[]
{
    "", "teams", "teams delete 1", "teams list 1", "teams get", "teams get -1",
    "teams get 2147483648", "games list --team-id 0", "teams list --game-id 1",
    "teams list --team-id 1 --team-id 2", "teams list --base-url ftp://host",
    "teams list --base-url https://user:pass@host", "teams list --base-url https://host/?x=1",
    "teams list --base-url https://host/#x", "teams list --base-url"
})
{
    using var handler = new FakeHandler(HttpStatusCode.OK, "[]");
    using var client = new HttpClient(handler);
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await Cli.RunAsync(command.Split(' ', StringSplitOptions.RemoveEmptyEntries), client, output, error);
    Check(code == 2 && handler.Method is null && error.ToString().Length > 0, $"reject {command}");
}

{
    using var handler = new FakeHandler(HttpStatusCode.OK, "[]");
    using var client = new HttpClient(handler);
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await Cli.RunAsync(["--base-url", "https://override.test/prefix/", "teams", "list"],
        client, output, error, "https://environment.test");
    Check(code == 0 && handler.Uri?.AbsoluteUri == "https://override.test/prefix/teams", "base URL precedence");
}

foreach (var (status, body, expected) in new[]
{
    (HttpStatusCode.NotFound, "Not found", "404"),
    (HttpStatusCode.Redirect, "", "302"),
    (HttpStatusCode.OK, "not json", "invalid JSON")
})
{
    using var handler = new FakeHandler(status, body);
    using var client = new HttpClient(handler);
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await Cli.RunAsync(["teams", "get", "1"], client, output, error);
    Check(code == 1 && output.ToString() == "" && error.ToString().Contains(expected), $"error {status}");
}

{
    using var handler = new FailingHandler();
    using var client = new HttpClient(handler);
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await Cli.RunAsync(["teams", "list"], client, output, error);
    Check(code == 1 && output.ToString() == "" && error.ToString().Contains("Could not reach the API"),
        "connection failure");
}

{
    using var handler = new FakeHandler(HttpStatusCode.OK, "[]");
    using var client = new HttpClient(handler);
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await Cli.RunAsync(["--help"], client, output, error);
    Check(code == 0 && handler.Method is null && output.ToString().Contains("player-stats"), "help");
}

Console.WriteLine($"Passed {routes.Length} route cases and input, configuration, help, and error checks.");

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception($"Failed: {description}");
}

sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public HttpMethod? Method { get; private set; }
    public Uri? Uri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Method = request.Method;
        Uri = request.RequestUri;
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

}

sealed class FailingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("Connection refused");
}
