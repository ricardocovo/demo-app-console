using System.Text.Json;
using Spectre.Console;

namespace LittleLeagueSoccer.ConsoleApp;

public sealed class TerminalApp(IAnsiConsole console, SoccerClient client, Options options)
{
    private int PageSize => Math.Clamp((console.Profile.Height - 23) / 2, 1, 10);
    private static string Escape(string value) => Markup.Escape(Display.Safe(value));

    public async Task RunAsync(CancellationToken ct)
    {
        if (options.Snapshot)
        {
            Header();
            if (options.View is { } view) Render(view, await client.GetAsync(view, ct), "", 0, true);
            else await Dashboard(ct);
            return;
        }

        if (options.View is { } initial) await Browse(initial, ct);
        while (!ct.IsCancellationRequested)
        {
            console.Clear();
            Header();
            try { await Dashboard(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested && IsConnectionError(ex)) { Error(ex); }
            var choices = Resource.All.Select(r => new Choice(r.Name, ApiView.List(r))).ToList();
            choices.Add(new("Refresh dashboard"));
            choices.Add(new("Quit"));
            var choice = await Choose("MATCH CENTRE", choices, ct);
            if (choice.Label == "Quit") return;
            if (choice.View is { } next) await Browse(next, ct);
        }
    }

    private void Header()
    {
        if (console.Profile.Width >= 72 && console.Profile.Height >= 30)
            console.Write(new FigletText("TOUCHLINE").Color(Color.SpringGreen2));
        else
            console.Write(new Rule("[bold springgreen2]TOUCHLINE[/]"));
        console.MarkupLine("[bold white]LITTLE LEAGUE SOCCER[/]  [black on springgreen2] READ ONLY [/]");
        console.MarkupLine($"[grey]API {Escape(options.BaseUrl.AbsoluteUri)}[/]");
        console.WriteLine();
    }

    private async Task Dashboard(CancellationToken ct)
    {
        var data = await Task.WhenAll(Resource.All.Select(r => client.GetAsync(ApiView.List(r), ct)));
        var cards = Resource.All.Select((r, i) =>
            new Panel(new Markup($"[bold {r.Accent}]{data[i].GetArrayLength()}[/]\n[white]{r.Name}[/]"))
                .Border(BoxBorder.Rounded).BorderColor(Color.Grey).Expand()).ToArray();
        console.Write(new Columns(cards));
        console.WriteLine();
        console.Write(new Rule("[springgreen2]THE TOUCHLINE / LEAGUE OVERVIEW[/]").LeftJustified());
        console.MarkupLine("[grey]Explore competitions, squads, fixtures and every player's performance.[/]");
        console.MarkupLine("[grey]Live API data. No write actions. Select a collection below.[/]");
        console.WriteLine();
    }

    private async Task Browse(ApiView initial, CancellationToken ct)
    {
        var history = new Stack<ApiView>();
        var view = initial;
        var search = "";
        var page = 0;
        JsonElement? cached = null;
        while (!ct.IsCancellationRequested)
        {
            console.Clear();
            Header();
            JsonElement data;
            try { data = cached ?? await client.GetAsync(view, ct); cached = data; }
            catch (Exception ex) when (!ct.IsCancellationRequested && IsConnectionError(ex))
            {
                Error(ex);
                var action = await Choose("CONNECTION", [new("Retry"), new("Back to dashboard")], ct);
                if (action.Label == "Retry") continue;
                return;
            }

            var rows = view.IsList ? Display.Filter(data, search) : [];
            page = Math.Clamp(page, 0, Math.Max(0, (rows.Count - 1) / PageSize));
            Render(view, data, search, page, false);
            var choices = new List<Choice>();
            if (view.IsList)
            {
                foreach (var row in rows.Skip(page * PageSize).Take(PageSize))
                {
                    if (!row.TryGetProperty("id", out var id) || !id.TryGetInt32(out var number) || number <= 0) continue;
                    var name = row.TryGetProperty("name", out var n) ? Display.Value(n)
                        : row.TryGetProperty("date", out var d) ? Display.Value(d) : view.Resource.Name;
                    choices.Add(new($"Open #{number}  {name}", ApiView.Detail(view.Resource, number)));
                }
                choices.Add(new("Search"));
                if (search.Length > 0) choices.Add(new("Clear search"));
                if (page > 0) choices.Add(new("Previous page"));
                if ((page + 1) * PageSize < rows.Count) choices.Add(new("Next page"));
            }
            else choices.AddRange(view.Links(data).Select(link => new Choice(link.Title, link)));
            choices.Add(new("Refresh"));
            choices.Add(new("Back"));
            choices.Add(new("Dashboard"));
            var choice = await Choose("EXPLORE / SELECT AN ACTION", choices, ct);
            if (choice.View is { } next) { history.Push(view); view = next; }
            else switch (choice.Label)
            {
                case "Dashboard": return;
                case "Back":
                    if (!history.TryPop(out var previous)) return;
                    view = previous;
                    break;
                case "Search":
                    search = await new TextPrompt<string>("[springgreen2]Search all fields[/] (empty clears):")
                        .AllowEmpty().ShowAsync(console, ct);
                    page = 0;
                    continue;
                case "Clear search": search = ""; page = 0; continue;
                case "Next page": page++; continue;
                case "Previous page": page--; continue;
                case "Refresh": cached = null; continue;
            }
            cached = null;
            search = "";
            page = 0;
        }
    }

    private void Render(ApiView view, JsonElement data, string search, int page, bool snapshot)
    {
        console.Write(new Rule($"[{view.Resource.Accent}]{Escape(view.Title)}[/]").LeftJustified());
        console.MarkupLine($"[grey]GET /{Escape(view.Path)}[/]");
        if (!view.IsList)
        {
            var details = new Table().Border(TableBorder.Rounded).AddColumn("FIELD").AddColumn("VALUE");
            foreach (var property in data.EnumerateObject())
                details.AddRow($"[grey]{Escape(Display.Label(property.Name))}[/]", Escape(Display.Value(property.Value)));
            console.Write(details);
            return;
        }

        var rows = Display.Filter(data, search);
        if (rows.Count == 0)
        {
            console.Write(new Panel(search.Length == 0
                ? "No records yet. This collection is empty; use Refresh after data is added in the API."
                : "No matching records. Clear search to see the full collection.").Border(BoxBorder.Rounded));
            return;
        }
        var fields = rows[0].EnumerateObject().Select(p => p.Name).ToArray();
        // Keep contract column order while retaining extra fields from related views.
        fields = view.Resource.Columns.Where(fields.Contains).Concat(fields.Except(view.Resource.Columns)).ToArray();
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Expand();
        foreach (var field in fields) table.AddColumn($"[{view.Resource.Accent}]{Escape(Display.Label(field))}[/]");
        var visible = snapshot ? rows : rows.Skip(page * PageSize).Take(PageSize);
        foreach (var row in visible)
            table.AddRow(fields.Select(field => row.TryGetProperty(field, out var value)
                ? Escape(Display.Value(value)) : "-").ToArray());
        console.Write(table);
        console.MarkupLine($"[grey]{rows.Count} records" +
            (snapshot ? "" : $"  /  Page {page + 1} of {(rows.Count + PageSize - 1) / PageSize}") +
            (search.Length == 0 ? "" : $"  /  Search: {Escape(search)}") + "[/]");
    }

    private Task<Choice> Choose(string title, IEnumerable<Choice> choices, CancellationToken ct) =>
        new SelectionPrompt<Choice>().Title($"[bold springgreen2]{title}[/]")
            .PageSize(Math.Clamp(console.Profile.Height / 3, 3, 10))
            .EnableSearch()
            .HighlightStyle(new Style(Color.Black, Color.SpringGreen2))
            .UseConverter(choice => Escape(choice.Label)).AddChoices(choices).ShowAsync(console, ct);

    private void Error(Exception ex) =>
        console.Write(new Panel(new Markup($"[red]{Escape(ex.Message)}[/]\n[grey]Check that the API is running and --url is correct.[/]"))
            .Header("Unable to load API data").Border(BoxBorder.Rounded).BorderColor(Color.Red));

    public static bool IsConnectionError(Exception ex) =>
        ex is HttpRequestException or ApiException or JsonException or TaskCanceledException;

    private sealed record Choice(string Label, ApiView? View = null);
}
