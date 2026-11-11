# Little League Soccer CLI

A read-only command-line client for [demo-app-api](https://github.com/ricardocovo/demo-app-api). It sends only HTTP GET requests and prints the complete response as formatted JSON.

## Requirements and configuration

Install the .NET 9 SDK and start the API. Its default HTTP launch profile listens at `http://localhost:5249`.

```powershell
dotnet run --project src\SoccerCli -- championships list
dotnet run --project src\SoccerCli -- teams list --championship-id 1
dotnet run --project src\SoccerCli -- players totals 2 --championship-id 1
```

Set `SOCCER_API_BASE_URL` to use another API location, or pass `--base-url URL` on any command (the option takes precedence). The URL must be an absolute HTTP(S) URL, optionally with a path prefix, and cannot contain credentials, a query, or a fragment.

The current API does not require authentication or support pagination. Its list endpoints return all matching records. The CLI does not follow redirects, so an incorrectly configured URL produces an error rather than sending a request to another location.

## Commands

Run `dotnet run --project src\SoccerCli -- --help` for a summary. IDs and filter values must be positive integers.

| Command | API operation |
|---|---|
| `championships list` | List championships |
| `championships get ID` | Get a championship |
| `championships teams ID` | List teams in a championship |
| `teams list [--championship-id ID]` | List or filter teams |
| `teams get ID` | Get a team |
| `teams players ID` | List a team's players |
| `teams games ID` | List a team's games |
| `players list [--team-id ID]` | List or filter players |
| `players get ID` | Get a player |
| `players totals ID [--championship-id ID]` | Get player totals |
| `games list [--team-id ID]` | List or filter games |
| `games get ID` | Get a game |
| `games stats ID` | List a game's player stats |
| `player-stats list [--game-id ID] [--player-id ID]` | List or filter player stats |
| `player-stats get ID` | Get a player stats record |

JSON goes to standard output. Usage, connection, HTTP, and invalid-response errors go to standard error and return a nonzero exit code.

## Validation

```powershell
dotnet run --project tests\SoccerCli.Tests
```
