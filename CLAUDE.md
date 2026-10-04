# RDW Tools

.NET 10 / C# solution (`RdwTools.slnx`) for RDW open data (dataset `m9d7-ebf2`, "Gekentekende voertuigen", Socrata/SODA API).

- `src/Rdw.Core`: all RDW logic (class library)
- `src/Rdw.Cli`: command-line tool, input/output only
- `tests/Rdw.Core.Tests`: xUnit tests
- Planned: REST API on top of Core, maybe a web UI, more datasets (APK, fuel, recalls), caching, Docker.

## Architecture rules

- All RDW logic lives in `Rdw.Core`. Front-ends (CLI, REST API, web) contain no RDW logic; they only call `IRdwClient`.
- Decisions are recorded in `docs/adr/`. Read them before changing behavior. Key one: ADR-002 "Annotate, don't block". Reject only input that can never be a plate (fail fast, no RDW call); per ADR-003 that is anything not exactly six characters after normalization. Never decide ourselves whether a plate is "valid"; the RDW data is the source of truth. A lookup returns a `VehicleLookupResult` with a `LookupStatus` (Found, NotFound, InvalidInput, ServiceUnavailable), never a bare null.
- Expected situations (bad user input, not found, RDW unreachable) are results. Programmer errors (null arguments, record without plate) throw.
- The RDW returns every value as text. Parse in Core (`RdwValueParser`); a missing or unparseable value becomes null.
- A significant design decision needs a new ADR (next number). Propose it before building.

## Commands

- Build: `dotnet build`
- Test: `dotnet test` (all tests must pass)
- Run the CLI: `dotnet run --project src/Rdw.Cli -- kenteken X998ZG`

## Workflow

- Never commit to `main`. One issue per task, then a branch (`feat/`, `fix/`, `docs/`, `test/`, `chore/`, `ci/`), then a PR with `Closes #n`. Use `gh` for issues and PRs.
- Start every task from an up-to-date `main` (`git switch main`, `git pull`) before creating a branch.
- After the owner merges a PR: switch to `main`, pull, and delete the merged local branch (`git branch -d <branch>`).
- Conventional Commits in English (`feat:`, `fix:`, `docs:`, `test:`, `chore:`, `ci:`). Commit messages are permanent: make them accurate.
- Test-first for Core logic. Tests never call the real RDW; use the `FakeHttpMessageHandler` pattern in `tests/Rdw.Core.Tests`.
- Before opening a PR: `dotnet build` and `dotnet test` pass, and `git diff` shows no unintended deletions.
- A PR is done when the CI check (`build-and-test`) is green. Show the evidence (test output, check result).
- Never commit secrets (tokens, keys, passwords). Use environment variables or configuration.
- Keep the README and ADRs in step with the code.

## Stop and ask first

- Any change to `.github/`, repository settings or this file.
- Deleting, skipping or weakening a test to get to green.
- A new design decision that needs an ADR.
- Anything destructive: force-push, rewriting history, deleting branches that are not yours.
- Merging. For now the owner merges every PR.

## Working with the owner

- The owner is not a programmer and is learning to work effectively with Claude. Reply in Dutch. Code, commits, PR text and documentation are in English.
- Work autonomously within the rules above. If a requirement is ambiguous and a wrong guess would be costly, ask. Otherwise choose a sensible default and say which one.
- Keep reports short: what changed, why, evidence, open questions. Do not explain the code line by line unless asked.

## Open questions (verify against real RDW data, do not guess)

- Plate shape recognition (sidecodes) as a neutral hint, designed after more real data; see ADR-002.

## Answered questions

Details, queries and counts: `docs/research/rdw-open-questions.md` (verified 2026-10-04).

- Plate length: every plate in `m9d7-ebf2` is exactly six characters. Shorter or longer input is rejected (ADR-003).
- `export_indicator`: always `Ja` or `Nee`, never missing. Exported vehicles stay in the dataset, so export never causes `NotFound`.
