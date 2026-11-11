using SoccerCli;

using var handler = new HttpClientHandler { AllowAutoRedirect = false };
using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
return await Cli.RunAsync(args, client, Console.Out, Console.Error,
    Environment.GetEnvironmentVariable("SOCCER_API_BASE_URL"));
