---
name: askcosmosemu
description: "Use this to verify and query a local Azure Cosmos DB emulator. Confirms databases, containers, and documents exist after writes. Read-only — never creates or modifies data."
---

# askcosmosemu — Cosmos Emulator Verification Skill

## Overview

Read-only tool for verifying work against a local Azure Cosmos DB emulator. Use after writing data to confirm databases, containers, and documents exist and are correct.

## Finding the Script

The skill uses a .NET 10 single-file script bundled with this plugin. Locate it with:

```bash
find "$USERPROFILE/.claude" -name "askcosmosemu.cs" 2>/dev/null | head -1
```

On Linux/Mac:
```bash
find ~/.claude -name "askcosmosemu.cs" 2>/dev/null | head -1
```

Store this path as `SCRIPT_PATH`. All commands below use `dotnet run "$SCRIPT_PATH"`.

## Connection Setup (First Run)

Before running any command, resolve the connection info from the project. The script reads `.env` and `.env.local` automatically from the **current working directory** — run all commands from the project root.

**Priority 1:** Look for `COSMOS_CONNECTION_STRING` in `.env`, `.env.local`, or environment.
Format: `AccountEndpoint=https://<host>:8081/;AccountKey=<key>`

**Priority 2:** Look for `COSMOS_ENDPOINT` + `COSMOS_KEY_FILE` (path to key file) or `COSMOS_KEY` (inline key value).

**If no connection info is found:** Stop and tell the user:
> "No Cosmos connection info found. Please set `COSMOS_CONNECTION_STRING` or `COSMOS_ENDPOINT` + `COSMOS_KEY_FILE` in your `.env` or `.env.local` file."

Do not guess values. Do not attempt to connect without confirmed credentials.

## Commands

### Verify emulator is reachable

```bash
dotnet run "$SCRIPT_PATH" verify
```

**Success:** `{"status":"ok","endpoint":"...","latency_ms":N}`
**Failure:** `{"status":"error","message":"..."}`

### Verify database exists

```bash
dotnet run "$SCRIPT_PATH" verify --db <database-name>
```

**Success:** `{"status":"ok","endpoint":"...","database":"...","latency_ms":N}`

### Verify database and container exist

```bash
dotnet run "$SCRIPT_PATH" verify --db <database-name> --cont <container-name>
```

**Success:** `{"status":"ok","endpoint":"...","database":"...","container":"...","latency_ms":N}`

### Query documents

```bash
dotnet run "$SCRIPT_PATH" query --db <database-name> --cont <container-name> --sql "<sql>" [--limit N]
```

- Default `--limit` is **5** if not specified
- Returns a JSON array for multiple results, a JSON object for a single result
- You can also control result size in the SQL itself (`SELECT TOP 1 ...`)

**Examples:**
```bash
# Check document count
dotnet run "$SCRIPT_PATH" query --db mydb --cont users --sql "SELECT VALUE COUNT(1) FROM c"

# Get first 3 documents matching a condition
dotnet run "$SCRIPT_PATH" query --db mydb --cont users --sql "SELECT * FROM c WHERE c.active = true" --limit 3

# Get a specific document by id
dotnet run "$SCRIPT_PATH" query --db mydb --cont users --sql "SELECT * FROM c WHERE c.id = 'user-123'" --limit 1
```

## Interpreting Results

- **Exit code 0** = success — parse stdout as JSON and report to the user
- **Exit code non-zero** = failure — read the `message` field and report clearly
- **Never retry on failure** — report what failed and stop
- **Never attempt to create** missing databases or containers — that is out of scope

## Output Format

Results are always JSON on stdout. No logs, warnings, or diagnostics are mixed in.

- Single document → plain JSON object
- Multiple documents → JSON array
- Error → `{"status":"error","message":"<description>"}`

## Prerequisites

- .NET 10 SDK installed (`dotnet --version` should show `10.x`)
- Azure Cosmos DB Emulator running and accessible
- Connection info set in `.env`, `.env.local`, or environment variables
