# thediscussion

Application for structured discussions. Product requirements live in
[`docs/core-requirements.md`](docs/core-requirements.md).

## Layout

| Path | What it is |
| --- | --- |
| `src/TheDiscussion.Core` | Domain model, argument status engine and storage interfaces. No storage technology. |
| `src/TheDiscussion.Api` | ASP.NET Core REST API (application tier). The storage provider is chosen in its composition root (`Composition/`) from configuration. |
| `tests/` | xUnit test projects (architecture, API integration). |
| `web/` | React + TypeScript + Vite front end. Talks to the API only through REST. |
| `.github/workflows/ci.yml` | GitHub Actions CI: .NET build and tests, web lint, type-check, tests and build. |

## Prerequisites

- .NET SDK 8 (pinned by `global.json`, any 8.0 feature band ≥ 8.0.100)
- Node.js 22 and npm

## Run the API

```sh
dotnet run --project src/TheDiscussion.Api --launch-profile InMemory
```

The API listens on <http://localhost:5080>. `GET /health` reports health. Logs are written to the
console as one JSON object per line.

## Run the front end

```sh
cd web
npm ci
npm run dev
```

Open <http://localhost:5173>. The dev server proxies `/api` and `/health` to the API at
`http://localhost:5080` (override with the `API_PROXY_TARGET` environment variable).

## Tests

.NET (from the repository root):

```sh
dotnet test
```

Web (from `web/`):

```sh
npm ci && npm run lint && npm test && npm run build
```

`npm run typecheck` runs the TypeScript compiler on its own (`npm run build` also type-checks).
