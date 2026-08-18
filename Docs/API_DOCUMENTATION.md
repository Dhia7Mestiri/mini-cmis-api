# MiniCMIS API Documentation

## Overview

MiniCMIS implements the CMIS 1.1 Browser Binding: a JSON/HTTP interface for document repositories. Clients start with a discovery call, then perform all operations against the two URLs it returns.

## Getting Started

1. `POST /auth/register` with `{ "email": ..., "password": ... }`
2. `POST /auth/login` with the same credentials → returns `accessToken`
3. Send `Authorization: Bearer <accessToken>` on every subsequent request
4. `GET /browser` → discovery call, returns `repositoryUrl` and `rootFolderUrl`
5. Use those two URLs for all further operations (see table below)

## Endpoints by URL

### Repository URL (`/browser`)

| Method | Selector/Action | Description |
|---|---|---|
| GET | `cmisselector=repositoryInfo` (default) | Repository info + discovery URLs |
| GET | `cmisselector=types` | List child types |
| GET | `cmisselector=typeDefinition&typeId=...` | Full type definition with property definitions |
| GET | `cmisselector=query&q=...` | Simple keyword search |
| POST | `cmisaction=query` (form: `statement`, `maxItems`, `skipCount`) | Full CMIS-SQL query |

### Root Folder URL (`/browser/{repositoryId}/{objectId}`)

| Method | Selector/Action | Description |
|---|---|---|
| GET | `cmisselector=object` (default) | Object metadata |
| GET | `cmisselector=children` | List folder children |
| GET | `cmisselector=parents` | Get parent object |
| GET | `cmisselector=content` | Download binary content |
| POST | `cmisaction=createDocument` (multipart: `file`, `name`) | Upload a document |
| POST | `cmisaction=createFolder` (form: `name`) | Create a folder |
| POST | `cmisaction=update` (form: `name`) | Rename an object |
| POST | `cmisaction=move` (form: `targetFolderId`) | Move an object |
| POST | `cmisaction=delete` | Delete a single (empty) object — Admin only |
| POST | `cmisaction=deleteTree` | Recursively delete a folder and its contents — Admin only |

## CMIS-SQL Query Support

Supported grammar: `SELECT * FROM <type> [WHERE <conditions>] [ORDER BY <property> [ASC|DESC]]`

Conditions support:
- `IN_FOLDER('folderId')`
- `=`, `<>`, `>`, `<`, `>=`, `<=` (type-aware: numeric fields compare numerically, dates compare as dates)
- `LIKE 'pattern%'`
- `IS NULL` / `IS NOT NULL`
- Combined with `AND` / `OR` (evaluated left-to-right; parentheses/operator precedence not supported — documented limitation for this project's scope)

Example:
```
SELECT * FROM cmis:document WHERE IN_FOLDER('root-folder') AND cmis:name LIKE '%.txt' ORDER BY cmis:creationDate DESC
```

Pagination via `maxItems` / `skipCount` form fields on the query request. Response includes `numItems` and `hasMoreItems`.

## Property Format

`cmisselector=typeDefinition` returns each property as:

```json
{
  "id": "cmis:name",
  "localName": "name",
  "propertyType": "string",
  "cardinality": "single",
  "updatability": "readwrite",
  "required": true
}
```

## Error Format

All errors follow the CMIS error model:

```json
{
  "exception": "objectNotFound",
  "message": "Object with ID 'xyz' was not found."
}
```

| HTTP Code | `exception` value | Meaning |
|---|---|---|
| 400 | `invalidArgument` | Malformed or missing required input |
| 401 | (Identity default) | Missing/invalid bearer token |
| 403 | (Forbid) | Authenticated but not authorized (e.g. non-Admin calling delete) |
| 404 | `objectNotFound` | Object/type does not exist |
| 405 | `notSupported` | Unknown `cmisaction` |
| 409 | `nameConstraintViolation` | Duplicate name in the same parent folder |
| 500 | `runtime` | Unexpected server error |

## Out of Scope

Secondary types (aspects), versioning (check-in/check-out), ACLs, and policies are explicitly out of scope for this project.
