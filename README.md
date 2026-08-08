# CMIS 1.1 Browser Binding Engine (.NET 8)

A clean, enterprise-ready Content Management Interoperability Services (CMIS 1.1) Browser Binding API built with **ASP.NET Core (.NET 8)**, **Entity Framework Core**, and **SQL Server**.

---

## 📌 Project Overview

This project implements the official **CMIS 1.1 Browser Binding** standard, enabling document management operations over HTTP using standard RESTful endpoint conventions. Binary content is persisted directly in SQL Server as byte streams (`byte[]`), and folder hierarchies are supported through self-referencing entity relationships.

---

## ✨ Features

- **Repository Information & Discovery**: `GET /browser` returns repository capabilities and supported CMIS standards.
- **Type Definitions**: `GET /browser?cmisselector=types` lists supported CMIS types (`cmis:folder`, `cmis:document`).
- **Hierarchy & Navigation**: `GET /browser/{repoId}/{objectId}?cmisselector=children` retrieves child objects of any folder.
- **Binary Streaming**: `GET /browser/{repoId}/{objectId}?cmisselector=content` streams file binaries directly to clients.
- **Document & Folder Creation**: `POST /browser/{repoId}/{objectId}` handles `multipart/form-data` file uploads (`cmisaction=createDocument`) and folder generation (`cmisaction=createFolder`).
- **Object Deletion**: `POST /browser/{repoId}/{objectId}` (`cmisaction=delete`) safely removes files and empty directories.
- **Keyword Search**: `GET /browser?cmisselector=query&q={term}` performs SQL `LIKE` queries across stored objects.
- **Automatic Seeding**: Seeds the database on startup with standard type definitions and a root folder directory.

---

## 🛠️ Architecture & Tech Stack

- **Framework**: ASP.NET Core Web API (.NET 8)
- **Database / ORM**: Microsoft SQL Server + Entity Framework Core 8.0
- **API Specification**: Swagger / OpenAPI
- **Design Pattern**: Service Layer + Repository Pattern with Entity Framework Core

---

## 🚀 Quick Start

### 1. Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Microsoft SQL Server or SQL Server Express

### 2. Database Setup
Update `appsettings.json` with your local SQL Server instance:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=YOUR_SERVER_NAME;Database=MiniCmisDb;Trusted_Connection=True;TrustServerCertificate=True;"
}