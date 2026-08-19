# 🚀 CMIS 1.1 Browser Binding Engine (.NET 8)

A clean, enterprise-ready **Content Management Interoperability Services (CMIS 1.1) Browser Binding** API built with **ASP.NET Core 8** and **Entity Framework Core**, featuring token-based authentication, role-based access control, a full web console, and continuous deployment to a cloud production environment.

- **Live API:** `https://mini-cmis-api.onrender.com`
- **Console UI:** `https://mini-cmis-api.onrender.com/`
- **Swagger Docs:** `https://mini-cmis-api.onrender.com/swagger`
- **Postman Collection:** [`Postman/MiniCMIS.postman_collection.json`](Postman/MiniCMIS.postman_collection.json)
- **Full API Documentation:** [`Docs/API_DOCUMENTATION.md`](Docs/API_DOCUMENTATION.md)

> Hosted on Render's free tier — the first request after inactivity may take a few seconds to spin up.

---

## 📌 Project Overview

This project implements the **CMIS 1.1 Browser Binding** standard end-to-end: discovery, type definitions, folder/document CRUD, move/rename, recursive deletion, and CMIS-SQL querying, all exposed over standard RESTful/form-based HTTP conventions per the spec. Binary content is persisted directly in the database as byte streams (`byte[]`), and folder hierarchies use a self-referencing entity relationship with a **materialized path** (`cmis:path`) for efficient subtree operations.

A browser-based console (`wwwroot/index.html`) is included for exploring the repository, running CMIS-SQL queries, and managing folders/documents without needing Postman or Swagger.

---

## ✨ Features

### Repository URL (`/browser`)

| Capability | Endpoint |
|---|---|
| Repository discovery | `GET /browser` — returns repository info plus `repositoryUrl` and `rootFolderUrl` |
| Type children | `GET /browser?cmisselector=types` |
| Full type definition | `GET /browser?cmisselector=typeDefinition&typeId=cmis:folder\|cmis:document` — includes full `propertyDefinitions` (type, cardinality, updatability, required) |
| Simple keyword search | `GET /browser?cmisselector=query&q={term}` |
| **CMIS-SQL query** | `POST /browser` (`cmisaction=query`) — supports `IN_FOLDER`, `AND`/`OR`, `LIKE`, `IS [NOT] NULL`, comparisons, `ORDER BY`, and pagination (`maxItems`/`skipCount`) |

### Root Folder URL (`/browser/{repositoryId}/{objectId}`)

| Capability | Endpoint |
|---|---|
| Read object metadata | `GET ...?cmisselector=object` (default) |
| List children | `GET ...?cmisselector=children` |
| Get parent | `GET ...?cmisselector=parents` |
| Download content | `GET ...?cmisselector=content` |
| Create document | `POST ...` (`cmisaction=createDocument`, multipart upload) |
| Create folder | `POST ...` (`cmisaction=createFolder`) |
| **Rename** | `POST ...` (`cmisaction=update`) — rewrites `cmis:path` on the object *and every descendant* |
| **Move** | `POST ...` (`cmisaction=move`) — same descendant-path rewrite, plus cycle protection |
| Delete (single/empty) | `POST ...` (`cmisaction=delete`) — Admin only |
| **Delete tree (recursive)** | `POST ...` (`cmisaction=deleteTree`) — Admin only, single-query subtree deletion via materialized path |

### Auth

| Endpoint | Description |
|---|---|
| `POST /auth/register` / `POST /auth/login` | .NET 8 Identity API Endpoints, bearer token auth |
| `GET /auth/me` | Returns the current user's email and roles — used by the console UI to gate buttons by permission instead of discovering them via 403s |

Role model: **Admin** (full access, including delete/deleteTree), **Manager** (create/rename/move), **User** (read-only). Enforced via `[Authorize(Roles=...)]` plus explicit role checks for destructive actions.

Automatic seeding populates the database on first run with the base CMIS type definitions and a root folder.

### CMIS Property System

- System and custom CMIS property definitions
- Per-type custom property definitions
- Single and multi-valued properties
- Required property validation
- Read-only/updatability validation
- Custom property values persisted per object
- CMIS property envelopes for object responses
- Custom property updates and cleanup on deletion
- CMIS-SQL filtering against custom properties

---

## 🛠️ Architecture & Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core Web API (.NET 8) |
| Authentication | .NET 8 Identity API Endpoints (Bearer Token Auth) + role-based authorization |
| ORM | Entity Framework Core 8.0 |
| Database (local dev) | Microsoft SQL Server (LocalDB) |
| Database (production) | PostgreSQL (Render managed instance) |
| CMIS-SQL engine | Custom parser (`Services/CmisQueryParser.cs`) — regex-based tokenizer/evaluator, no external SQL engine dependency |
| Frontend | Vanilla JS console (`wwwroot/index.html`) — no build step, no framework dependency |
| API Specification | Swagger / OpenAPI |
| Design Pattern | Service Layer + Repository Pattern |
| Containerization | Docker / Docker Compose |
| Hosting & CI/CD | Render Cloud PaaS (Continuous Deployment via GitHub) |

The app auto-selects its EF Core provider at startup: **SQL Server** for local development, and **PostgreSQL** (via Npgsql) in production. It also automatically parses Render's `postgres://` connection URI into the format Npgsql expects — no manual conversion needed on deploy.

> **Known cross-environment nuance:** SQL Server's default collation is case-*insensitive*, PostgreSQL's is case-*sensitive*. This affects duplicate-name checks and `LIKE`/`StartsWith` matching (rename, move, deleteTree, search) — behavior is consistent within each environment but not guaranteed identical between dev and prod for mixed-case names. Documented trade-off, not a bug.

---

## 🚀 Quick Start (Local Development)

### 1. Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Microsoft SQL Server or SQL Server Express

### 2. Configure the database

The default `appsettings.json` is already set up for LocalDB:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MiniCmisDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

If you're using a named SQL Server instance instead, update the `DefaultConnection` string accordingly. No JWT/auth configuration is needed — ASP.NET Core Identity's built-in bearer token endpoints work out of the box.

### 3. Apply migrations

```bash
dotnet ef database update
```

### 4. Run the API

```bash
dotnet run
```

- Console UI: `https://localhost:5001/`
- Swagger UI: `https://localhost:5001/swagger`
- Health check: `/health`

### Alternative: run with Docker

```bash
docker compose up --build
```

---

## 🧪 Testing

A dedicated xUnit test project (`tests/CMIS_IyaSoft.Tests`) provides both unit and integration coverage.

- **Unit tests** for `CmisService`, covering:
  - Folder and document creation
  - Duplicate-name validation
  - Rename, move, and hierarchy/path updates
  - Single and recursive deletion
  - CMIS-SQL querying and pagination
  - Custom property definitions and values
  - Required, read-only, and multi-value property validation
  - CMIS property envelopes
  - Custom property updates and cleanup
  - Content stream replacement

- **Integration tests** using `WebApplicationFactory<Program>` and EF Core InMemory, covering:
  - Health endpoint
  - Repository and Browser Binding endpoints
  - Authentication and authorization
  - Type definitions
  - CMIS object/property envelope responses

Run the complete test suite with:

```bash
dotnet test
```

---

## ☁️ Production Deployment (Render)

- Database: PostgreSQL managed instance on Render
- The `DefaultConnection` string is set via Render's environment variables (Render provides it in `postgres://` URI form; the app parses it into Npgsql format automatically at startup)
- Every push to `main` triggers Render to automatically build and deploy via Docker

---

## 🗺️ Roadmap

- [ ] Add GitHub Deployments integration for deployment history tracking
- [ ] CMIS-SQL: support parentheses / operator precedence in WHERE clauses
- [ ] Property validation against custom type definitions
- [ ] Support for versioning and check-in/check-out (explicitly out of scope for this project's spec)

---

## 📄 License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
