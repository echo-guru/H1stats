# H1Stats

Operational intelligence platform for Hearts 1st.

## Ports (runs alongside GPH_patList_importer)

| Service | Port | Notes |
|---------|------|-------|
| Frontend (Vite) | **5180** | `http://localhost:5180` |
| Backend (ASP.NET) | **5002** | `http://localhost:5002` |
| GPH app (for reference) | 5173 / 3001 | Leave unchanged |

## Prerequisites

- **Node.js** 18+ (frontend)
- **.NET 8 SDK** (backend) — [download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **SQL Server** access to H1PACS (192.168.12.205) — read-only login `h1stats`

## Quick Start

### Frontend

```bash
cd frontend
npm install
npm run dev
```

Open http://localhost:5180

Default dev login (until SQL auth is wired): **admin** / **admin**

### Backend

Install .NET 8 SDK, then:

```bash
cd backend
dotnet restore
dotnet run --project src/H1Stats.Api
```

API: http://localhost:5002  
Health: http://localhost:5002/api/health

Configure SQL connection in `backend/src/H1Stats.Api/appsettings.Development.json`.

## Project Structure

```
H1Stats/
├── docs/           PRD, engineering principles
├── frontend/       React + TypeScript + Tailwind (Hearts 1st branding)
└── backend/
    └── src/
        ├── H1Stats.Api/           Web API, controllers, DI bootstrap
        ├── H1Stats.Core/          Domain models, module registry, interfaces
        └── H1Stats.Infrastructure/ SQL repositories, auth, configuration
```

## Branding

Hearts 1st palette (from logo):

- Primary teal: `#33758F`
- Accent burgundy: `#A63746`

See `frontend/tailwind.config.js` for full `brand.*` tokens.

## Documentation

- [PRD](docs/PRD.md)
- [Engineering Principles](docs/ENGINEERING_PRINCIPLES.md)
