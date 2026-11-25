# Touchline | Little League Soccer

A read-only terminal match centre for [demo-app-api](https://github.com/ricardocovo/demo-app-api).
Browse every championship, team, player, game, and player-stat record in a colourful,
keyboard-driven Spectre.Console interface.

## Run

Requires the .NET 9 SDK (9.0.320 or a later 9.0 feature band) and a running soccer API.
The API is a separate application; this console does not start it or seed its database.

```powershell
dotnet run --project src\LittleLeagueSoccer.Console -- --url http://localhost:5000
```

Use the address printed by your API process. You can also configure it via an environment variable:

```powershell
$env:SOCCER_API_URL = "http://localhost:5000"
dotnet run --project src\LittleLeagueSoccer.Console
```

URL precedence: `--url`, then `SOCCER_API_URL`, then `http://localhost:5000/`.
HTTP and HTTPS are supported, including API path prefixes. Normal HTTPS certificate
validation remains enabled. Requests time out after 15 seconds and redirects are not followed.

## Explore

- **Dashboard:** live counts for all five entity collections.
- **Collections:** all API fields, up to 10 records per page (adapts to terminal height), case-insensitive search across
  every field, record selection, refresh, and explicit empty/error states.
- **Details:** every returned field plus links to related views.
- **Navigation:** arrow keys and Enter; type to filter actions. Back retraces
  related views; Dashboard returns home. Ctrl+C exits.

| Entity | Related views |
|---|---|
| Championships | Championship teams |
| Teams | Championship, roster, fixtures |
| Players | Team, career totals, per-game stats |
| Games | Both teams, match stat lines (including player name) |
| Player stats | Game, player |

Player positions are displayed as returned by the API (`Goalkeeper`, `Defender`,
`Midfielder`, `Forward`). Relationship IDs remain visible so records are unambiguous.
Search and paging are local to the loaded collection; Refresh fetches current data.
Back restores the previous view with its search/page reset.

**Read-only by design:** the client sends only GET requests. There are no create,
update, delete, data seeding, or local database operations.

## Direct views and terminal capture

```powershell
# Start in a collection or detail screen
dotnet run --project src\LittleLeagueSoccer.Console -- --resource players
dotnet run --project src\LittleLeagueSoccer.Console -- --resource players --id 1

# Non-interactive output for CI, logs, or terminal captures
dotnet run --project src\LittleLeagueSoccer.Console -- --snapshot
dotnet run --project src\LittleLeagueSoccer.Console -- --snapshot --resource games
dotnet run --project src\LittleLeagueSoccer.Console -- --snapshot --resource player-stats --id 1
```

Resources: `championships`, `teams`, `players`, `games`, `player-stats`.
`--snapshot` prints the dashboard or selected view once and exits; collection
snapshots include all records, not just the first page. Redirected input/output
requires snapshot mode. Exit codes: `0` success, `1` options/API error,
`130` cancellation or request timeout. Use `--help` for all options.

## Build and test

```powershell
dotnet build
dotnet test
```

Tests use an in-memory HTTP handler and terminal renderer; no running API is required.
They cover GET-only requests, entity and relationship routes, rendering, search,
configuration, empty data, malformed responses, HTTP errors, and terminal-text escaping.

## Structure

`src\LittleLeagueSoccer.Console` contains the CLI configuration, explicit resource
catalogue and relationship routes, GET-only HTTP client, and terminal UI.
`tests\LittleLeagueSoccer.Console.Tests` contains isolated regression tests.
There is no dependency on the API's implementation or database.