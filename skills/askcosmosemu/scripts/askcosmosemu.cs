#:package Microsoft.Azure.Cosmos@3.46.0
#:package Newtonsoft.Json@13.0.3
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

// ── Arg parsing ───────────────────────────────────────────────────────────────

var argList = args.ToList();

if (argList.Count == 0)
{
    OutputError("No command specified. Usage: verify | verify --db <name> [--cont <name>] | query --db <name> --cont <name> --sql <sql> [--limit N]");
    return 1;
}

var command = argList[0].ToLower();
var db       = GetArg(argList, "--db");
var cont     = GetArg(argList, "--cont");
var sql      = GetArg(argList, "--sql");
var limitStr = GetArg(argList, "--limit");
var limit    = limitStr != null ? int.Parse(limitStr) : 5;

// ── Connection resolution ─────────────────────────────────────────────────────

var (endpoint, key, connError) = ResolveConnection();
if (connError != null)
{
    OutputError(connError);
    return 1;
}

// ── Dispatch ──────────────────────────────────────────────────────────────────

var client = CreateClient(endpoint!, key!);
try
{
    return command switch
    {
        "verify" => await RunVerify(client, endpoint!, db, cont),
        "query"  => await RunQuery(client, db, cont, sql, limit),
        _        => BadCommand(command)
    };
}
catch (Exception ex)
{
    OutputError(ex.Message);
    return 1;
}

// ── Commands ──────────────────────────────────────────────────────────────────

static async Task<int> RunVerify(CosmosClient client, string endpoint, string? db, string? cont)
{
    var sw = Stopwatch.StartNew();
    try
    {
        await client.ReadAccountAsync();
        sw.Stop();

        if (db == null)
        {
            Output(new { status = "ok", endpoint, latency_ms = sw.ElapsedMilliseconds });
            return 0;
        }

        await client.GetDatabase(db).ReadAsync();

        if (cont == null)
        {
            Output(new { status = "ok", endpoint, database = db, latency_ms = sw.ElapsedMilliseconds });
            return 0;
        }

        await client.GetContainer(db, cont).ReadContainerAsync();
        Output(new { status = "ok", endpoint, database = db, container = cont, latency_ms = sw.ElapsedMilliseconds });
        return 0;
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        if (cont != null)
            OutputError($"Container '{cont}' not found in database '{db}'");
        else if (db != null)
            OutputError($"Database '{db}' not found");
        else
            OutputError($"Emulator not reachable at {endpoint}");
        return 1;
    }
}

static async Task<int> RunQuery(CosmosClient client, string? db, string? cont, string? sql, int limit)
{
    if (db == null || cont == null || sql == null)
    {
        OutputError("query requires --db, --cont, and --sql");
        return 1;
    }

    var container = client.GetContainer(db, cont);
    var results   = new List<JsonElement>();
    var opts      = new QueryRequestOptions { MaxItemCount = limit };

    // Use stream iterator to avoid JsonElement lifetime issues and handle SELECT VALUE scalars.
    // Cosmos wraps all results (including scalars) as {"Documents":[...], "_count":N} in the stream.
    try
    {
        using var iter = container.GetItemQueryStreamIterator(new QueryDefinition(sql), requestOptions: opts);
        while (iter.HasMoreResults && results.Count < limit)
        {
            using var response = await iter.ReadNextAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    OutputError($"Database '{db}' or container '{cont}' not found");
                    return 1;
                }
                OutputError($"Query failed with status {(int)response.StatusCode}: {response.ErrorMessage}");
                return 1;
            }
            using var doc = await JsonDocument.ParseAsync(response.Content);
            if (doc.RootElement.TryGetProperty("Documents", out var documents))
            {
                foreach (var item in documents.EnumerateArray())
                {
                    results.Add(item.Clone());
                    if (results.Count >= limit) break;
                }
            }
        }
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        OutputError($"Database '{db}' or container '{cont}' not found");
        return 1;
    }

    if (results.Count == 1)
        Output(results[0]);
    else
        Output(results);

    return 0;
}

static int BadCommand(string command)
{
    OutputError($"Unknown command '{command}'. Expected: verify, query");
    return 1;
}

// ── Connection helpers ────────────────────────────────────────────────────────

static (string? endpoint, string? key, string? error) ResolveConnection()
{
    // Priority 1: COSMOS_CONNECTION_STRING
    var connStr = ReadEnvVar("COSMOS_CONNECTION_STRING");
    if (connStr != null)
    {
        string? ep = null, k = null;
        foreach (var part in connStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("AccountEndpoint=", StringComparison.OrdinalIgnoreCase))
                ep = part["AccountEndpoint=".Length..].Trim();
            else if (part.StartsWith("AccountKey=", StringComparison.OrdinalIgnoreCase))
                k = part["AccountKey=".Length..].Trim();
        }
        if (ep != null && k != null)
            return (ep, k, null);
        return (null, null, "COSMOS_CONNECTION_STRING is set but could not parse AccountEndpoint and AccountKey");
    }

    // Priority 2: COSMOS_ENDPOINT + COSMOS_KEY_FILE or COSMOS_KEY
    var endpoint = ReadEnvVar("COSMOS_ENDPOINT");
    if (endpoint != null)
    {
        var keyFile = ReadEnvVar("COSMOS_KEY_FILE");
        if (keyFile != null)
        {
            if (!File.Exists(keyFile))
                return (null, null, $"COSMOS_KEY_FILE points to '{keyFile}' which does not exist");
            return (endpoint, File.ReadAllText(keyFile).Trim(), null);
        }

        var inlineKey = ReadEnvVar("COSMOS_KEY");
        if (inlineKey != null)
            return (endpoint, inlineKey, null);

        return (null, null, "COSMOS_ENDPOINT is set but neither COSMOS_KEY_FILE nor COSMOS_KEY is set");
    }

    return (null, null,
        "No Cosmos connection info found. Set one of:\n" +
        "  COSMOS_CONNECTION_STRING=AccountEndpoint=...;AccountKey=...\n" +
        "  COSMOS_ENDPOINT=https://... + COSMOS_KEY_FILE=<path> (or COSMOS_KEY=<value>)");
}

static string? ReadEnvVar(string name)
{
    var val = Environment.GetEnvironmentVariable(name);
    if (!string.IsNullOrWhiteSpace(val)) return val;

    // Check .env.local then .env in current working directory
    foreach (var file in new[] { ".env.local", ".env" })
    {
        if (!File.Exists(file)) continue;
        foreach (var line in File.ReadAllLines(file))
        {
            if (line.TrimStart().StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            if (line[..eq].Trim() != name) continue;
            var value = line[(eq + 1)..].Trim().Trim('"').Trim('\'');
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
    }
    return null;
}

// ── Arg helper ────────────────────────────────────────────────────────────────

static string? GetArg(List<string> argList, string name)
{
    var idx = argList.IndexOf(name);
    return idx >= 0 && idx + 1 < argList.Count ? argList[idx + 1] : null;
}

// ── Client factory ────────────────────────────────────────────────────────────

static CosmosClient CreateClient(string endpoint, string key) =>
    new(endpoint, key, new CosmosClientOptions
    {
        HttpClientFactory = () => new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        }),
        ConnectionMode = ConnectionMode.Gateway
    });

// ── Output helpers ────────────────────────────────────────────────────────────

static void Output(object value) =>
    Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    }));

static void OutputError(string message) =>
    Console.WriteLine(JsonSerializer.Serialize(
        new { status = "error", message },
        new JsonSerializerOptions { WriteIndented = false }));
