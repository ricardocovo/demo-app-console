using System.Net;
using System.Text;
using System.Text.Json;
using LittleLeagueSoccer.ConsoleApp;
using Spectre.Console.Testing;
using Xunit;

namespace LittleLeagueSoccer.Console.Tests;

public sealed class ConsoleTests
{
    [Theory]
    [InlineData("championships", """{"id":7,"name":"Cup","year":2026}""", "championships/7/teams")]
    [InlineData("teams", """{"id":7,"name":"Falcons","championshipId":2}""", "championships/2", "teams/7/players", "teams/7/games")]
    [InlineData("players", """{"id":7,"name":"Alex","age":11,"teamId":2}""", "teams/2", "players/7/totals", "player-stats?playerId=7")]
    [InlineData("games", """{"id":7,"date":"2026-06-01","teamAId":2,"teamBId":3}""", "teams/2", "teams/3", "games/7/stats")]
    [InlineData("player-stats", """{"id":7,"gameId":2,"playerId":3}""", "games/2", "players/3")]
    public async Task EveryEntitySupportsListDetailAndRelatedGetViews(string path, string entityJson, params string[] links)
    {
        var resource = Resource.Parse(path);
        using var document = JsonDocument.Parse(entityJson);
        var detail = ApiView.Detail(resource, 7);
        Assert.Equal(links, detail.Links(document.RootElement).Select(link => link.Path));
        using var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/7") ? entityJson : "[]");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") };
        var client = new SoccerClient(http);

        await client.GetAsync(ApiView.List(resource));
        await client.GetAsync(detail);
        foreach (var link in detail.Links(document.RootElement))
        {
            handler.Json = _ => link.IsList ? "[]" : entityJson;
            await client.GetAsync(link);
        }

        Assert.All(handler.Requests, request => Assert.Equal("GET", request.Method));
        Assert.Equal(new[] { $"/api/{path}", $"/api/{path}/7" }
            .Concat(links.Select(link => "/api/" + link)), handler.Requests.Select(request => request.Path));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "record not found")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    [InlineData(HttpStatusCode.Redirect, "HTTP 302")]
    [InlineData(HttpStatusCode.Unauthorized, "HTTP 401")]
    public async Task HttpErrorsAreExplicit(HttpStatusCode status, string message)
    {
        using var handler = new RecordingHandler(_ => "[]") { Status = status };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            new SoccerClient(http).GetAsync(ApiView.List(Resource.Teams)));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[1]")]
    public async Task UnexpectedListShapesAreRejected(string json)
    {
        using var handler = new RecordingHandler(_ => json);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await Assert.ThrowsAsync<ApiException>(() => new SoccerClient(http).GetAsync(ApiView.List(Resource.Teams)));
    }

    [Fact]
    public async Task InvalidJsonIsNotTreatedAsAnEmptyCollection()
    {
        using var handler = new RecordingHandler(_ => "<html>Not the API</html>");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await Assert.ThrowsAnyAsync<JsonException>(() => new SoccerClient(http).GetAsync(ApiView.List(Resource.Teams)));
    }

    [Fact]
    public async Task SnapshotShowsAllEntitiesAndUsesOnlyGet()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        using var handler = new RecordingHandler(_ => """[{"id":1,"name":"Falcons"}]""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await new TerminalApp(console, new SoccerClient(http),
            Options.Parse(["--snapshot"], null)).RunAsync(default);
        foreach (var resource in Resource.All) Assert.Contains(resource.Name, console.Output);
        Assert.Contains("READ ONLY", console.Output);
        Assert.Equal(Resource.All.Select(r => "/" + r.Path), handler.Requests.Select(r => r.Path));
        Assert.All(handler.Requests, r => Assert.Equal("GET", r.Method));
    }

    [Fact]
    public async Task SnapshotRendersUntrustedNamesLiterallyWithoutTerminalControls()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        using var handler = new RecordingHandler(_ =>
            """[{"id":1,"name":"[red]Falcons[/]\u001b[2J","championshipId":2}]""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await new TerminalApp(console, new SoccerClient(http),
            Options.Parse(["--snapshot", "--resource", "teams"], null)).RunAsync(default);
        Assert.Contains("[red]Falcons[/]", console.Output);
        Assert.DoesNotContain("\u001b[2J", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyCollectionsAndAdditionalStatLineFieldsAreVisible()
    {
        using var console = new TestConsole();
        console.Profile.Width = 180;
        using var handler = new RecordingHandler(_ => "[]");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var view = new ApiView("games/1/stats", "Match stat lines", Resource.Stats, true);
        var options = new Options(http.BaseAddress, true, view, false);
        var app = new TerminalApp(console, new SoccerClient(http), options);
        await app.RunAsync(default);
        Assert.Contains("No records yet", console.Output);
        handler.Json = _ => """[{"id":1,"playerId":2,"playerName":"Alex","teamId":3,"position":"Forward","minutesPlayed":60,"starter":true,"goals":2,"assists":1}]""";
        await app.RunAsync(default);
        Assert.Contains("PLAYER NAME", console.Output);
        Assert.Contains("Alex", console.Output);
        Assert.Contains("Forward", console.Output);
    }

    [Fact]
    public void SearchIncludesEveryFieldAndIgnoresCase()
    {
        using var document = JsonDocument.Parse("""[{"id":1,"name":"Falcons","age":11},{"id":2,"name":"Owls","age":12}]""");
        Assert.Single(Display.Filter(document.RootElement, "FAL"));
        Assert.Equal(2, Display.Filter(document.RootElement, "12").Single().GetProperty("id").GetInt32());
        Assert.Empty(Display.Filter(document.RootElement, "missing"));
    }

    [Fact]
    public async Task KeyboardNavigationPagesIntoDetailsAndRelatedRecords()
    {
        using var console = new TestConsole().Interactive().Width(120).Height(50);
        using var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/teams" => JsonSerializer.Serialize(Enumerable.Range(1, 12).Select(id => new { id, name = $"Team {id}", championshipId = 1 })),
            "/teams/11" => """{"id":11,"name":"Team 11","championshipId":1}""",
            "/teams/11/players" => """[{"id":101,"name":"Alex","age":11,"teamId":11}]""",
            "/players/101" => """{"id":101,"name":"Alex","age":11,"teamId":11}""",
            _ => "[]"
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Select(console, 11); // Next page after ten records and Search.
        Select(console, 0); // Team 11.
        Select(console, 1); // Roster.
        Select(console, 0); // Alex.
        Select(console, 5); // Dashboard.
        Select(console, 6); // Quit.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await new TerminalApp(console, new SoccerClient(http),
            Options.Parse(["--resource", "teams"], null)).RunAsync(timeout.Token);
        Assert.Contains("Page 2 of 2", console.Output);
        Assert.Contains("Alex", console.Output);
        Assert.Equal(new[] { "/teams", "/teams/11", "/teams/11/players", "/players/101" },
            handler.Requests.Take(4).Select(r => r.Path));
        Assert.All(handler.Requests, r => Assert.Equal("GET", r.Method));
    }

    [Fact]
    public async Task KeyboardSearchRefreshAndBackPreserveReadOnlyBrowsing()
    {
        using var console = new TestConsole().Interactive().Width(120).Height(50);
        using var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/teams" => """[{"id":1,"name":"Falcons","championshipId":1},{"id":2,"name":"Owls","championshipId":1}]""",
            "/teams/2" => """{"id":2,"name":"Owls","championshipId":1}""",
            _ => "[]"
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Select(console, 2); // Search.
        console.Input.PushTextWithEnter("owls");
        Select(console, 3); // Refresh filtered list.
        Select(console, 0); // Open matching team.
        Select(console, 4); // Back.
        Select(console, 5); // Dashboard.
        Select(console, 6); // Quit.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await new TerminalApp(console, new SoccerClient(http),
            Options.Parse(["--resource", "teams"], null)).RunAsync(timeout.Token);
        Assert.Contains("Search: owls", console.Output);
        Assert.Equal(new[] { "/teams", "/teams", "/teams/2", "/teams" },
            handler.Requests.Take(4).Select(r => r.Path));
    }

    private static void Select(TestConsole console, int index)
    {
        for (var i = 0; i < index; i++) console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
    }

    [Fact]
    public async Task MenuSupportsTypingToFilterActions()
    {
        using var console = new TestConsole().Interactive();
        using var handler = new RecordingHandler(_ => "[]");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var app = new TerminalApp(console, new SoccerClient(http), Options.Parse([], null));
        console.Input.PushTextWithEnter("Quit");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await app.RunAsync(timeout.Token);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public void UrlPrecedenceAndPathPrefixArePreserved()
    {
        var options = Options.Parse(["--url", "https://example.test/soccer", "--resource", "players", "--id", "3"], "http://ignored/");
        Assert.Equal("https://example.test/soccer/", options.BaseUrl.AbsoluteUri);
        Assert.Equal("players/3", options.View!.Path);
        Assert.Equal("http://environment/", Options.Parse([], "http://environment").BaseUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("--id", "0")]
    [InlineData("--id", "1")]
    [InlineData("--url", "file:///tmp/api")]
    [InlineData("--url", "https://example.test/?q=1")]
    [InlineData("--url", "https://user:password@example.test/")]
    [InlineData("--resource", "unknown")]
    [InlineData("--unknown", "value")]
    [InlineData("--url")]
    public void InvalidOptionsFailWithAnActionableError(params string[] args) =>
        Assert.Throws<ArgumentException>(() => Options.Parse(args, null));

    private sealed class RecordingHandler(Func<HttpRequestMessage, string> json) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, string> Json { get; set; } = json;
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public List<(string Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery));
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Json(request), Encoding.UTF8, "application/json")
            });
        }
    }
}
