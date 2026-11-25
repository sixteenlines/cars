using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace CarTracker.Api.Services;

/// <summary>Raw outcome of one Responses API call: the JSON text the model produced, or an error, plus usage.</summary>
public record AiCallResult(string? Text, string? Error, string? Model, int InputTokens, int OutputTokens, decimal EstimatedCostUsd, bool AiCalled);

/// <summary>Shared plumbing for the OpenAI Responses API with a strict JSON schema (bill import, fuel scan).</summary>
public static class OpenAiResponses
{
    public static async Task<AiCallResult> CallAsync(HttpClient http, AiOptions options, ILogger log,
        string instructions, JsonObject filePart, string prompt, string schemaName, JsonObject schema, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return new(null, Loc.T("No OpenAI API key configured (Ai:ApiKey).", "Kein OpenAI-Schlüssel eingerichtet (Ai:ApiKey)."), null, 0, 0, 0, false);

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["instructions"] = instructions,
            ["store"] = false,
            ["input"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(filePart, new JsonObject { ["type"] = "input_text", ["text"] = prompt }),
            }),
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject { ["type"] = "json_schema", ["name"] = schemaName, ["strict"] = true, ["schema"] = schema },
            },
        };
        if (!string.IsNullOrWhiteSpace(options.ReasoningEffort))
            body["reasoning"] = new JsonObject { ["effort"] = options.ReasoningEffort };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "OpenAI request failed");
            return new(null, Loc.T("Couldn't reach OpenAI. Try again later.", "OpenAI ist gerade nicht erreichbar. Versuch es später nochmal."), options.Model, 0, 0, 0, true);
        }

        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            log.LogWarning("OpenAI returned {Status}: {Body}", (int)res.StatusCode, json.Length > 2000 ? json[..2000] : json);
            var msg = TryGetErrorMessage(json) ?? res.ReasonPhrase;
            return new(null, Loc.T($"OpenAI error ({(int)res.StatusCode}): {msg}", $"OpenAI-Fehler ({(int)res.StatusCode}): {msg}"), options.Model, 0, 0, 0, true);
        }
        return Parse(json, options);
    }

    /// <summary>Pulls the output text (or refusal) and token usage out of a Responses API result.</summary>
    public static AiCallResult Parse(string responseJson, AiOptions options)
    {
        var root = JsonNode.Parse(responseJson)!;
        var model = root["model"]?.GetValue<string>() ?? options.Model;
        var inTok = root["usage"]?["input_tokens"]?.GetValue<int>() ?? 0;
        var outTok = root["usage"]?["output_tokens"]?.GetValue<int>() ?? 0;
        var cost = Math.Round((inTok * options.InputPricePerMTok + outTok * options.OutputPricePerMTok) / 1_000_000m, 5);

        string? text = null, refusal = null;
        foreach (var item in root["output"]?.AsArray() ?? [])
        {
            if (item?["type"]?.GetValue<string>() != "message") continue;
            foreach (var part in item["content"]?.AsArray() ?? [])
            {
                var type = part?["type"]?.GetValue<string>();
                if (type == "output_text") text = part!["text"]?.GetValue<string>();
                if (type == "refusal") refusal = part!["refusal"]?.GetValue<string>();
            }
        }

        if (text is null)
            return new(null, refusal is null ? Loc.T("The AI returned no result.", "Die KI hat kein Ergebnis geliefert.") : Loc.T($"The AI refused: {refusal}", $"Die KI hat abgelehnt: {refusal}"), model, inTok, outTok, cost, true);
        return new(text, null, model, inTok, outTok, cost, true);
    }

    public static JsonObject Str(bool nullable = true) => new() { ["type"] = nullable ? new JsonArray("string", "null") : "string" };
    public static JsonObject Num(bool nullable = true) => new() { ["type"] = nullable ? new JsonArray("number", "null") : "number" };

    private static string? TryGetErrorMessage(string json)
    {
        try { return JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>(); }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
