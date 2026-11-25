using System.Net;
using System.Text.Json;

namespace LittleLeagueSoccer.ConsoleApp;

public sealed class SoccerClient(HttpClient http)
{
    public async Task<JsonElement> GetAsync(ApiView view, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(view.Path, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ApiException(response.StatusCode == HttpStatusCode.NotFound
                ? $"GET /{view.Path}: record not found. It may have been removed; refresh the list."
                : $"GET /{view.Path}: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Check the API URL and server logs.");

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var expected = view.IsList ? JsonValueKind.Array : JsonValueKind.Object;
        if (document.RootElement.ValueKind != expected ||
            (view.IsList && document.RootElement.EnumerateArray().Any(row => row.ValueKind != JsonValueKind.Object)))
            throw new ApiException($"GET /{view.Path}: expected a JSON {(view.IsList ? "array of objects" : "object")}. Check the API URL.");
        return document.RootElement.Clone();
    }
}

public sealed class ApiException(string message) : Exception(message);
