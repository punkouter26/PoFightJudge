# NET_RULES

Authoritative rules for `Po{Name}` .NET solutions. Deviations must be recorded in `AGENT.MD` with a reason.

## 1. Core Principles & Architecture

- **1.1 Naming Standard** — Prefix solutions, projects, and root namespaces with `Po{Name}`.
- **1.2 Tech Stack** — .NET 10 / C# 15 with Centralized Package Management (`/Directory.Packages.props`).
- **1.3 Compiler Guards** — Enforce `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` globally in `Directory.Build.props`.
- **1.4 Solution Layout**
  - `src/Po{Name}.API/`: Minimal API host using autonomous, decoupled Vertical Slice Architecture (`Features/{FeatureName}`).
  - `src/Po{Name}.Client/`: Blazor WASM UI hosted directly by `Po{Name}.API`.
  - `src/Po{Name}.Shared/`: DTOs, Enums, Interfaces, JSON contexts. Zero business logic or data access.
  - `tests/`: `Po{Name}.Unit` (business logic), `Po{Name}.Integration` (Testcontainers/Azurite), `Po{Name}.E2EAPI` (HTTP contract tests), `Po{Name}.E2EUI` (Playwright tests).

## 2. API, Security & Infrastructure

- **2.1 Endpoints** — Map via `IEndpointRouteBuilder` + `MapGroup()`. Auto-document with `Microsoft.AspNetCore.OpenApi` and serve via Scalar UI.
- **2.2 Dev/Test Auth** — Use `FakeAuthHandler` reading `X-Fake-User` and `X-Fake-Roles` headers. MUST throw `InvalidOperationException` in Production.
- **2.3 Secrets & Identity** — Resource Group `PoShared` (or `Po{Name}`). Authenticate via System-Assigned Managed Identity / `DefaultAzureCredential` + Azure Key Vault in Azure. Secrets in committed `appsettings*.json` are strictly forbidden; local development may use `dotnet user-secrets` for values that cannot come from Key Vault (PoMarriedFight uses neither: Key Vault via `az login` locally, see CLAUDE.md).
- **2.4 Health & Diagnostics**
  - `/health`: Native .NET health status for external dependencies.
  - `/diag`: Real-time operational summary. Must strictly redact all secrets, tokens, and connection strings.

## 3. UI/UX & Blazor WASM

- **3.1 Layout Structure** — Header format: `[Left: Branding | Center: Contextual Actions | Right: Session / Logout]`.
- **3.2 UI Controls & Styling** — Radzen Blazor library. Zero inline CSS—use scoped `.razor.css` and global CSS variables only. Auto-detect system Light/Dark themes.
- **3.3 Mock Indicator** — Display a persistent warning banner ("USING MOCK DATA") whenever an active state uses mock/local data.
- **3.4 Code Hygiene** — Continuously purge unused files, dead code, orphaned assets, and unused `using` directives across all commits.
