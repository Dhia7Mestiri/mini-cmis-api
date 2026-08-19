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
| GET | `cmisselector=object` (default) | Object metadata (full properties envelope) |
| GET | `cmisselector=children` | List folder children (full properties envelope) |
| GET | `cmisselector=parents` | Get parent object (full properties envelope) |
| GET | `cmisselector=content` | Download binary content |
| POST | `cmisaction=createDocument` (multipart: `file`, `name`, `properties`) | Upload a document |
| POST | `cmisaction=createFolder` (form: `name`, `properties`) | Create a folder |
| POST | `cmisaction=setContentStream` (multipart: `file`) | Replace a document's binary content |
| POST | `cmisaction=update` (form: `name` and/or `properties`) | Rename an object and/or set/clear custom properties |
| POST | `cmisaction=move` (form: `targetFolderId`) | Move an object |
| POST | `cmisaction=delete` | Delete a single (empty) object — Admin only |
| POST | `cmisaction=deleteTree` | Recursively delete a folder and its contents — Admin only |

## CMIS-SQL Query Support

Supported grammar: `SELECT * FROM <type> [WHERE <conditions>] [ORDER BY <property> [ASC|DESC]]`

Conditions support:
- `IN_FOLDER('folderId')`
- `=`, `<>`, `>`, `<`, `>=`, `<=` (type-aware: numeric fields compare numerically, dates compare as dates — this also applies to custom properties, typed per their `propertyDefinition`)
- `LIKE 'pattern%'`
- `IS NULL` / `IS NOT NULL`
- Combined with `AND` / `OR` (evaluated left-to-right; parentheses/operator precedence not supported — documented limitation for this project's scope)

Both system properties (`cmis:name`, `cmis:creationDate`, ...) and custom properties (`custom:department`, ...) can appear anywhere a property id is expected — in `WHERE` and in `ORDER BY`. For multi-valued custom properties, comparisons operate on the first stored value (documented simplification).

Example:
```
SELECT * FROM cmis:document WHERE IN_FOLDER('root-folder') AND cmis:name LIKE '%.txt' ORDER BY cmis:creationDate DESC
SELECT * FROM cmis:document WHERE custom:department = 'Finance'
```

Pagination via `maxItems` / `skipCount` form fields on the query request. Response includes `numItems` and `hasMoreItems`.

## Custom Properties

Beyond the fixed CMIS system properties (`cmis:name`, `cmis:objectId`, dates, etc.), each type can declare its own custom properties. These are stored per-type in the database and validated on every write.

### Declaring a custom property for a type

Custom property definitions live in the `TypePropertyDefinitions` table (seeded in `DbInitializer` — two demo properties ship out of the box: `custom:department` on `cmis:document`, `custom:owner` on `cmis:folder`). Each definition has:

```json
{
  "propertyId": "custom:department",
  "localName": "department",
  "propertyType": "string",
  "cardinality": "single",
  "updatability": "readwrite",
  "required": false
}
```

`cmisselector=typeDefinition&typeId=...` returns these merged together with the fixed system properties, so a client discovers the full schema — system + custom — in one call.

### Setting custom properties on create

Pass an optional `properties` form field alongside `createDocument` / `createFolder`, containing a JSON object of property id → value (or a JSON array of values for `cardinality: multi` properties):

```
properties={"custom:department":"Finance"}
```

Validation rules, all returned as CMIS-formatted 400 errors on failure:
- Unknown property id for the type → `invalidArgument`
- Property marked `updatability: readonly` → `invalidArgument`
- A `cardinality: multi` property given a non-array value (or vice versa) → `invalidArgument`
- A `required: true` property missing at creation → `invalidArgument`

### Updating / clearing custom properties

`cmisaction=update` accepts `name`, `properties`, or both — at least one is required. Custom properties are replaced wholesale per key:

```
properties={"custom:department":"Legal"}      // sets/overwrites
properties={"custom:department":""}           // clears (vider une propriété)
```

Properties marked `updatability: oncreate` can only be supplied at creation time, not through `update`; `readonly` properties can never be written.

### Reading custom properties back

`cmisselector=object` (and `children`/`parents`) return the full CMIS properties envelope — system and custom properties together, each shaped as below.

## Property Format

`cmisselector=typeDefinition` returns each **property definition** as:

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

Object reads (`cmisselector=object|children|parents`) return each **property value** wrapped in an envelope, keyed by property id:

```json
{
  "id": "doc-101",
  "name": "Welcome.txt",
  "typeId": "cmis:document",
  "parentId": "root-folder",
  "path": "/Welcome.txt",
  "properties": {
    "cmis:objectId": { "id": "cmis:objectId", "localName": "objectId", "type": "id", "cardinality": "single", "value": "doc-101" },
    "cmis:name": { "id": "cmis:name", "localName": "name", "type": "string", "cardinality": "single", "value": "Welcome.txt" },
    "custom:department": { "id": "custom:department", "localName": "department", "type": "string", "cardinality": "single", "value": "Finance" }
  }
}
```

Multi-valued properties return `value` as a JSON array, ordered by insertion.

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