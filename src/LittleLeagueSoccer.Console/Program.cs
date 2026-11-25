using LittleLeagueSoccer.ConsoleApp;
using Spectre.Console;

try
{
    var options = Options.Parse(args, Environment.GetEnvironmentVariable("SOCCER_API_URL"));
    if (options.Help) { Console.WriteLine(Options.Usage); return 0; }
    if (!options.Snapshot && (Console.IsInputRedirected || Console.IsOutputRedirected))
        throw new ArgumentException("Interactive mode requires a terminal. Use --snapshot for redirected output.");

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    using var handler = new HttpClientHandler { AllowAutoRedirect = false };
    using var http = new HttpClient(handler) { BaseAddress = options.BaseUrl, Timeout = TimeSpan.FromSeconds(15) };
    await new TerminalApp(AnsiConsole.Console, new SoccerClient(http), options).RunAsync(cancellation.Token);
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Request cancelled or timed out.");
    return 130;
}
catch (Exception ex) when (ex is ArgumentException || TerminalApp.IsConnectionError(ex))
{
    Console.Error.WriteLine($"Error: {Display.Safe(ex.Message)}");
    return 1;
}
