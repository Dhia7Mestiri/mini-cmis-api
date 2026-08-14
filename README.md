# 🚀 CMIS 1.1 Browser Binding Engine (.NET 8)

A clean, enterprise-ready **Content Management Interoperability Services (CMIS 1.1) Browser Binding** API built with **ASP.NET Core 8** and **Entity Framework Core**, featuring token-based authentication and deployed to a cloud production environment.

- **Live API:** `https://mini-cmis-api.onrender.com`
- **Swagger Docs:** `https://mini-cmis-api.onrender.com/swagger`

> Hosted on Render's free tier — the first request after inactivity may take a few seconds to spin up.

---

## 📌 Project Overview

This project implements the official **CMIS 1.1 Browser Binding** standard, enabling document management operations over HTTP using standard RESTful endpoint conventions. Binary content is persisted directly in the database as byte streams (`byte[]`), and folder hierarchies are supported through self-referencing entity relationships.

---

## ✨ Features

| Capability | Endpoint |
|---|---|
| Repository info & discovery | `GET /browser` — returns repository capabilities and supported CMIS standards |
| Type definitions | `GET /browser?cmisselector=types` — lists supported CMIS types (`cmis:folder`, `cmis:document`) |
| Hierarchy & navigation | `GET /browser/{repoId}/{objectId}?cmisselector=children` — retrieves child objects of a folder |
| Binary streaming | `GET /browser/{repoId}/{objectId}?cmisselector=content` — streams file binaries to clients |
| Create document / folder | `POST /browser/{repoId}/{objectId}` — `multipart/form-data` upload (`cmisaction=createDocument`) or folder creation (`cmisaction=createFolder`) |
| Delete object | `POST /browser/{repoId}/{objectId}` — `cmisaction=delete`, safely removes files and empty directories |
| Keyword search | `GET /browser?cmisselector=query&q={term}` — performs search queries across stored objects |
| Automatic seeding | Seeds the database on startup with standard type definitions and a root folder |

Authentication is handled via .NET 8's built-in **Identity API Endpoints** (`AddIdentityApiEndpoints`) with bearer tokens — clients register/login at `/auth/register` and `/auth/login`, then pass the returned token on subsequent requests.

---

## 🛠️ Architecture & Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core Web API (.NET 8) |
| Authentication | .NET 8 Identity API Endpoints (Bearer Token Auth) |
| ORM | Entity Framework Core 8.0 |
| Database (local dev) | Microsoft SQL Server (LocalDB) |
| Database (production) | PostgreSQL (Render managed instance) |
| API Specification | Swagger / OpenAPI |
| Design Pattern | Service Layer + Repository Pattern |
| Containerization | Docker / Docker Compose |
| Hosting & CI/CD | Render Cloud PaaS (Continuous Deployment via GitHub) |

The app auto-selects its EF Core provider at startup: **SQL Server** for local development, and **PostgreSQL** (via Npgsql) in production. It also automatically parses Render's `postgres://` connection URI into the format Npgsql expects — no manual conversion needed on deploy.

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

Swagger UI will be available at `https://localhost:5001/swagger` (port may vary). A health check is exposed at `/health`.

### Alternative: run with Docker

```bash
docker compose up --build
```

---

## ☁️ Production Deployment (Render)

- Database: PostgreSQL managed instance on Render
- The `DefaultConnection` string is set via Render's environment variables (Render provides it in `postgres://` URI form; the app parses it into Npgsql format automatically at startup)
- Every push to `main` triggers Render to automatically build and deploy via Docker

---

## 🗺️ Roadmap

- [ ] Add GitHub Deployments integration for deployment history tracking
- [ ] Add integration tests for CMIS operations
- [ ] Support for versioning and check-in/check-out

---

## 📄 License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
