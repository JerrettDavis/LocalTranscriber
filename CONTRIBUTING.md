# Contributing

Thanks for contributing to LocalTranscriber.

## Development Setup

1. Install .NET SDK `10.0.103` (or compatible patch).
2. Restore dependencies:
   - `dotnet restore LocalTranscriber.sln`
3. Build:
   - `dotnet build LocalTranscriber.sln -c Debug`
4. Run:
   - CLI: `dotnet run --project src/LocalTranscriber.Cli/LocalTranscriber.Cli.csproj -- help`
   - Web: `dotnet run --project src/LocalTranscriber.Web/LocalTranscriber.Web.csproj`

## Branching And Pull Requests

1. Create a feature branch from `main`.
2. Keep changes scoped and focused.
3. Add or update docs for behavior/flag/UI changes.
4. Run local validation before opening a PR:
   - `dotnet restore LocalTranscriber.sln`
   - `dotnet build LocalTranscriber.sln -c Release`
   - `dotnet test LocalTranscriber.sln -c Release --no-build` (if tests exist)
5. Open PR using the template and include:
   - What changed
   - Why it changed
   - Validation evidence (build/test/manual)

## Coding Guidelines

1. Keep code and comments concise.
2. Favor explicit naming and predictable behavior.
3. Maintain backward compatibility for CLI flags when practical.
4. For UI changes, include screenshots or short clips when possible.
5. Do not commit generated files, model artifacts, or local output assets.

## Commit Message Guidance

Use clear, imperative messages, e.g.:

- `feat(web): add checksum cache hit stage`
- `fix(cli): preserve transcript section when formatting fails`
- `docs: add browser mode support matrix`

## Security

If you discover a security issue, do not open a public issue. Follow `SECURITY.md`.
