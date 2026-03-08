# AGENTS.md — FSharp.Data.SqlClient

Developer and AI-agent quick-reference for building, testing, and CI checks.

## Prerequisites

- **.NET 9 SDK** — required; pinned in `global.json`
- **A SQL Server instance** with the [AdventureWorks2012](https://github.com/Microsoft/sql-server-samples) database attached (used by all runtime tests)
- Local tools are declared in `.config/dotnet-tools.json`; restore once per clone:

```
dotnet tool restore
```

This installs **Paket 10** and **Fantomas 7**.

## Restoring packages

Paket manages all NuGet dependencies. After `dotnet tool restore`, run:

```bash
dotnet paket install   # re-resolves + updates paket.lock (after editing paket.dependencies)
dotnet paket restore   # fast restore from existing paket.lock (normal dev workflow)
```

The paket.lock is committed and should be kept up-to-date. Do not edit `packages/` by hand.

## Building

```bash
# Build the main library + design-time assembly
dotnet build SqlClient.sln -c Release

# Build the console sample (requires the library to be built first)
dotnet build Samples.sln -c Release

# Build the test projects
dotnet build Tests.sln -c Release
```

The FAKE-based build script in `build/` orchestrates everything in order:

```bash
cd build
dotnet run               # runs all: Clean → CheckFormat → AssemblyInfo → Build → …
dotnet run -- Build      # run a single target
```

## Code formatting (Fantomas)

All F# source in `src/`, `tests/`, and `build/` is formatted with Fantomas.

```bash
# Format in-place
dotnet fantomas src tests build

# Check only (what CI does — fails if any file needs reformatting)
dotnet fantomas src tests build --check
```

CI will fail if any file is not formatted. **Always run the formatter before committing.**

Formatting configuration is in [.fantomasrc](.fantomasrc); editor settings in [.editorconfig](.editorconfig).

## Running tests

Tests require a live SQL Server with AdventureWorks2012. The connection string is read from `tests/SqlClient.Tests/app.config` (key `AdventureWorks`) or the environment variable `GITHUB_ACTION_SQL_SERVER_CONNECTION_STRING`.

```bash
dotnet test tests/SqlClient.Tests/SqlClient.Tests.fsproj -f net9.0
dotnet test tests/SqlClient.DesignTime.Tests/SqlClient.DesignTime.Tests.fsproj -f net9.0
```

## CI readiness checklist

Before opening a PR, ensure:

1. `dotnet paket restore` succeeds (paket.lock is committed and up-to-date).
2. `dotnet build SqlClient.sln -c Release` — no errors (FS0044 deprecation warnings for `System.Data.SqlClient` are expected and suppressed).
3. `dotnet fantomas src tests build --recurse --check` — exits 0 (no unformatted files).
4. Tests pass against a SQL Server instance with AdventureWorks2012.

## Repository layout

| Path | Purpose |
|---|---|
| `src/SqlClient/` | Runtime library (`FSharp.Data.SqlClient.dll`), targets `netstandard2.0;net9.0` |
| `src/SqlClient.DesignTime/` | Design-time assembly, targets `net9.0` |
| `src/SqlClient.Samples/ConsoleSample/` | Runnable demo of `SqlCommandProvider`, `SqlProgrammabilityProvider`, `SqlEnumProvider` |
| `src/SqlClient.TestProjects/` | Integration test helpers (Lib, NetCoreApp) |
| `tests/SqlClient.Tests/` | Main test suite, `net9.0` |
| `tests/SqlClient.DesignTime.Tests/` | Design-time-specific tests, `net9.0` |
| `build/` | FAKE + Fun.Build pipeline (`dotnet run`) |
| `paket.dependencies` | Top-level package declarations (no .NET Framework groups) |
| `.config/dotnet-tools.json` | Local tool manifest (paket, fantomas) |

## Notes for agents

- **No .NET Framework / Mono support.** All target frameworks are `net9.0` or `netstandard2.0`. Do not add `net462`, `net471`, or similar TFMs.
- **Paket manages packages.** Do not use `dotnet add package`. Edit `paket.dependencies` and run `dotnet paket install` instead.
- `System.Data.SqlClient` (legacy) is used intentionally. Migration to `Microsoft.Data.SqlClient` is a future task; FS0044 warnings are suppressed.
- The design-time assembly output path is `bin/typeproviders/fsharp41/net9.0/` — this is the standard layout expected by the F# type-provider SDK.
