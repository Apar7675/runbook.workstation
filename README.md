# RunBook Workstation

RunBook Workstation is the shop-floor satellite client in the RunBook system.

## Role

Workstation is not a company-data authority.
It is a local station app for:

- employee passcode sign-in
- workstation registration and trust
- time clock
- narrow shop-floor modules allowed by Desktop and Control policy

## What Workstation Owns Locally

Workstation keeps only workstation-local runtime state under its own local runtime store:

- `Data\_core`
- `Data\_logs`
- `Data\_temp`
- `Data\cache`
- `Data\config`

Examples:

- workstation settings
- registration snapshot
- control session cache
- workstation session
- timeclock cache
- timeclock queue
- employee auth cache

These are workstation runtime artifacts, not authoritative company manufacturing data.

## What Workstation Does Not Own

Workstation must not:

- create or own a company shell
- own the authoritative manufacturing database
- become the source of truth for Desktop-owned files, drawings, or work-order history

## Trust / Auth Model

Workstation trust is separate from Desktop secondary attachment trust.

Current trust path:

1. Workstation keeps a stable local `WorkstationId`.
2. Workstation registers through Desktop local pairing and/or Control registration flows.
3. Workstation signs in employees through workstation-specific session/token flows.
4. Control remains the remote authority for workstation billing/access outcomes.

## Current API Surface

Workstation currently talks to:

### Desktop local APIs

- `api/workstation-local/register`
- `api/workstation-local/login`
- `api/workstation-local/auth-package`
- `api/workstation-local/timeclock/*`
- workstation-local work-order / drawing / inspection endpoints where enabled

### Control APIs

- `POST /api/workstation/register`
- `POST /api/workstation/login`
- `GET /api/workstation/timeclock`
- `POST /api/workstation/timeclock`
- Desktop-facing workstation management routes used by Desktop sync/admin flows

## Architecture Boundaries

- Desktop remains the local manufacturing authority.
- Control remains the remote identity / entitlement / registration authority.
- Workstation remains a trusted satellite execution client.

## Build

From `D:\RunBook.Workstation`:

```powershell
dotnet build .\RunBook.Workstation.sln
```

