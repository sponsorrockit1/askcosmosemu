# askcosmosemu

A Claude Code plugin that gives AI agents a read-only tool to verify and query a local [Azure Cosmos DB Emulator](https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-develop-emulator).

Use it to confirm databases, containers, and documents exist after agents write data — without risking any modifications.

## Install

```
/plugin install https://github.com/sponsorrockit1/askcosmosemu
```

## Prerequisites

- .NET 10 SDK — `dotnet --version` should show `10.x`
- Azure Cosmos DB Emulator running on your network

## Configuration

Set one of the following in your project's `.env` or `.env.local` (gitignored):

**Option A — Connection string:**
```env
COSMOS_CONNECTION_STRING=AccountEndpoint=https://<host>:8081/;AccountKey=<key>
```

**Option B — Endpoint + key file:**
```env
COSMOS_ENDPOINT=https://<host>:8081/
COSMOS_KEY_FILE=C:\path\to\emulator.key
```

**Option C — Endpoint + inline key:**
```env
COSMOS_ENDPOINT=https://<host>:8081/
COSMOS_KEY=<base64-encoded-key>
```

The script reads `.env` and `.env.local` from the current working directory automatically — no need to export env vars manually.

## Usage

Ask your agent to run any of these:

```
/askcosmosemu verify
/askcosmosemu verify db=mydb
/askcosmosemu verify db=mydb cont=mycontainer
/askcosmosemu query db=mydb cont=mycontainer sql="SELECT * FROM c" limit=5
```

Or directly in bash:

```bash
SCRIPT=$(find "$USERPROFILE/.claude" -name "askcosmosemu.cs" | head -1)

dotnet run "$SCRIPT" verify
dotnet run "$SCRIPT" verify --db mydb
dotnet run "$SCRIPT" verify --db mydb --cont mycontainer
dotnet run "$SCRIPT" query --db mydb --cont mycontainer --sql "SELECT * FROM c" --limit 5
```

## Output

All output is JSON on stdout. Exit code `0` = success, non-zero = failure.

```json
// verify success
{"status":"ok","endpoint":"https://localhost:8081/","latency_ms":42}

// verify db+container success
{"status":"ok","endpoint":"https://localhost:8081/","database":"mydb","container":"mycontainer","latency_ms":18}

// any failure
{"status":"error","message":"Database 'mydb' not found"}

// query results
[{"id":"1","name":"foo"},{"id":"2","name":"bar"}]
```

## What it does NOT do

- Create, update, or delete any data
- Support Azure-hosted Cosmos DB (emulator only)
- Modify emulator configuration
- Retry on failure

## Notes

- The emulator uses a self-signed TLS certificate — the script bypasses cert validation intentionally. Only use this tool for local development.
- NuGet packages are cached after first run — subsequent runs are fast.
- The `.env` parser handles `KEY=value`, `KEY="value"`, and `KEY='value'` but not multiline values or shell variable expansion.

## License

MIT
