# thediscussion

Application for structured discussions. Product requirements live in
[`docs/core-requirements.md`](docs/core-requirements.md).

## Layout

| Path | What it is |
| --- | --- |
| `src/TheDiscussion.Core` | Domain model, argument status engine and storage interfaces. No storage technology. |
| `src/TheDiscussion.Api` | ASP.NET Core REST API (application tier). The storage provider is chosen in its composition root (`Composition/StorageComposition.cs`) from configuration. |
| `src/TheDiscussion.Storage.InMemory` | In-memory storage provider (local default and tests). Data is lost when the API stops. |
| `src/TheDiscussion.Storage.Postgres` | PostgreSQL storage provider (EF Core + Npgsql) with its migrations. |
| `tests/` | xUnit test projects: core (status engine, validation), architecture rules, storage contract suite, API integration. |
| `web/` | React + TypeScript + Vite front end (discussion list and outline view). Talks to the API only through REST. Tests use Vitest, React Testing Library and axe. |
| `.github/workflows/ci.yml` | GitHub Actions CI: .NET build and tests (with a PostgreSQL service container), web lint, type-check, tests and build. |

## Prerequisites

- .NET SDK 8 (pinned by `global.json`, any 8.0 feature band ≥ 8.0.100)
- Node.js 22 and npm
- Optional: PostgreSQL 13 or later, to run the API or the storage tests against PostgreSQL

## Run the API

### In-memory storage (default)

```sh
dotnet run --project src/TheDiscussion.Api --launch-profile InMemory
```

### PostgreSQL storage

Point the API at a database it may create tables in. Pending migrations are applied at startup
(turn that off with `Storage__Postgres__MigrateOnStartup=false`).

```sh
# bash
export ConnectionStrings__TheDiscussion="Host=localhost;Port=5432;Database=thediscussion;Username=thediscussion;Password=<password>"
dotnet run --project src/TheDiscussion.Api --launch-profile Postgres
```

```powershell
# PowerShell
$env:ConnectionStrings__TheDiscussion = "Host=localhost;Port=5432;Database=thediscussion;Username=thediscussion;Password=<password>"
dotnet run --project src/TheDiscussion.Api --launch-profile Postgres
```

Configuration keys: `Storage:Provider` (`InMemory` or `Postgres`), `ConnectionStrings:TheDiscussion`,
`Storage:Postgres:MigrateOnStartup`. Do not commit connection strings; pass them through the
environment or user secrets.

The API listens on <http://localhost:5080>. `GET /health` returns 200 with storage reachability
(503 when storage cannot be reached). Logs are written to the console as one JSON object per line,
with one entry per request (method, route, status code, duration).

## REST API

Routes are under `/api/v1`. JSON is camelCase, ids are GUIDs, and errors are RFC 7807 problem
details (`application/problem+json`; validation errors list each offending field under `errors`).
Write endpoints are open (no accounts yet), so only run the API in local and test environments.

| Method and route | Body | Result |
| --- | --- | --- |
| `GET /api/v1/discussions` | | 200, `[{ id, title, topicTag, createdAt }]`, most recent first |
| `POST /api/v1/discussions` | `{ title, topicTag, thesis }` | 201 with the discussion, or 400 |
| `GET /api/v1/discussions/{id}` | | 200 with the full tree and computed status, or 404 |
| `POST /api/v1/discussions/{id}/statements` | `{ kind, parentId, text?, groupType? }` | 201 with the statement, 400, or 404 |
| `GET /health` | | 200 (`storage.reachable: true`) or 503 |

Limits: title 1–200 characters, statement text 1–5000 characters (leading and trailing whitespace
is trimmed), topic tag 1–5 dot-separated lowercase segments of `[a-z0-9-]` (for example
`work.remote`).

Statement kinds and what they may target (`parentId`):

| `kind` | Allowed parent | Notes |
| --- | --- | --- |
| `supportGroup` | the thesis | `groupType` is `allRequired` or `anySufficient`; no text |
| `supportingStatement` | a support group | |
| `illustration` | the thesis or a supporting statement | never affects status; cannot be countered |
| `counterArgument` | the thesis or a supporting statement | |
| `rebuttal` | a counter argument or a rebuttal | any depth |

`GET /api/v1/discussions/{id}` returns `status` (`unsupported`, `open`, `countered` or `tested`),
`thesisId` and `statements`, a flat list in creation order where each statement names its
`parentId`. Supporting statements carry `stands`, counter arguments and rebuttals carry `refuted`,
and non-empty support groups carry `holds`. Status is computed on every read by
`ArgumentStatusEngine` in the core project and is never stored:

- A counter argument or rebuttal is *refuted* when at least one rebuttal targeting it is not itself refuted.
- A supporting statement *stands* when no unrefuted counter argument targets it.
- An `allRequired` group holds when all its supporting statements stand; an `anySufficient` group
  holds when at least one does. Empty groups are ignored.
- Thesis: **unsupported** without supporting statements; otherwise **countered** when an unrefuted
  counter argument targets the thesis or a non-empty group does not hold; otherwise **tested** when
  any counter argument exists; otherwise **open**.

## Run the front end

```sh
cd web
npm ci
npm run dev
```

Open <http://localhost:5173>. The dev server proxies `/api` and `/health` to the API at
`http://localhost:5080` (override with the `API_PROXY_TARGET` environment variable), so start the
API first.

### End to end

1. Start the API (in-memory or PostgreSQL, above) in one terminal.
2. Start the front end in another terminal.
3. On the Discussions page, fill in *Start a discussion* (title, topic tag such as `work.remote`,
   thesis) and select **Create discussion**. The new discussion opens.
4. Use **+ Add support group**, **+ Add support**, **+ Add illustration**, **+ Add counter** and
   **+ Add rebuttal** to build the argument. After each add, the outline and statuses refresh.

The discussion view is an indented outline: the thesis with its status badge (UNSUPPORTED, OPEN,
COUNTERED or TESTED), support groups labelled ALL REQUIRED or ANY SUFFICIENT, and each statement
labelled with its kind and its marker (standing/countered for supporting statements,
refuted/unrefuted for counter arguments and rebuttals).

## Tests

.NET (from the repository root):

```sh
dotnet test
```

The storage contract suite runs against the in-memory provider every time. It runs against
PostgreSQL only when `THEDISCUSSION_TEST_POSTGRES` holds a connection string for a user that may
create databases; each run creates a throwaway database, applies the migrations and drops it.
Without the variable those tests are reported as skipped and the reason is printed. CI sets the
variable and `THEDISCUSSION_REQUIRE_POSTGRES_TESTS=true`, which turns a missing database into a
failure.

```sh
THEDISCUSSION_TEST_POSTGRES="Host=localhost;Username=postgres;Password=<password>" dotnet test
```

Web (from `web/`):

```sh
npm ci && npm run lint && npm test && npm run build
```

`npm run typecheck` runs the TypeScript compiler on its own (`npm run build` also type-checks).

## Database migrations

The schema is managed with EF Core migrations in `src/TheDiscussion.Storage.Postgres/Migrations`.
After changing `DiscussionDbContext`, add a migration with the pinned local tool:

```sh
dotnet tool restore
dotnet ef migrations add <Name> --project src/TheDiscussion.Storage.Postgres --output-dir Migrations
```

A test fails when the model and the latest migration snapshot disagree.
