# 🚀 Mini-CMIS API

[![Render Status](https://api.render.com/deploy/srv-.../badge.svg)](https://render.com)
[![API Status](https://img.shields.io/badge/API-Live-brightgreen)](https://mini-cmis-api.onrender.com/swagger)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Cloud-blue)](https://www.postgresql.org/)

A robust **Content Management Interoperability Services (CMIS) 1.1** backend API built with **.NET 8**, featuring secure token-based authentication via **ASP.NET Core Identity**, deployed in a cloud production environment.

## 🌐 Live Demo & Endpoints
* **Base API URL:** `https://mini-cmis-api.onrender.com`
* **Swagger Documentation:** `https://mini-cmis-api.onrender.com/swagger`

---

## 🛠️ Tech Stack & Architecture

* **Backend Framework:** .NET 8 Web API
* **Authentication:** ASP.NET Core Identity (Bearer Token Auth)
* **Database & ORM:** PostgreSQL (Cloud Instance) via Entity Framework Core
* **Hosting & CI/CD:** Render Cloud PaaS (Continuous Deployment via GitHub)

---

## 🚀 CI/CD Pipeline
This project is configured with an automated production pipeline:
1. **Source Control:** Code is maintained on GitHub (`main` branch).
2. **Automated Deployment:** Every push to `main` automatically triggers Render to build the application and deploy with live status integration.