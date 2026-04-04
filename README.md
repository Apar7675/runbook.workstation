# RunBook.Workstation

RunBook.Workstation is the satellite shop-floor client for the RunBook ecosystem.

## Purpose

- Employee passcode login at remote workstations
- Permission-driven module shell
- Time Clock first, with placeholder shells for future modules
- Workstation identity and registration tied to RunBook Control
- Desktop-managed local workstation policy through employee access settings

## Runtime Data

This app keeps runtime data under:

- `Data\_core`
- `Data\_logs`
- `Data\_temp`
- `Data\cache`
- `Data\config`

Key files:

- `Data\config\workstation-settings.json`
- `Data\cache\registration.json`
- `Data\cache\control-session.dat`
- `Data\cache\workstation-session.dat`
- `Data\cache\timeclock-cache.json`

## Required Configuration

Set these values in `Data\config\workstation-settings.json` or through the first-run settings panel:

- `ControlBaseUrl`
- `ShopId`
- `ShopName`
- `WorkstationName`

Supervisor setup now signs in through Control and stores the refreshable session in encrypted local storage instead of the config file.

The workstation keeps a stable `WorkstationId` locally and uses it for registration with Control.

## Backend Contracts

This app currently uses:

- `POST /api/workstation/register`
- `POST /api/workstation/login`
- `GET /api/workstation/timeclock`
- `POST /api/workstation/timeclock`

Desktop syncs workstation policy to Control with:

- `POST /api/desktop/workstation-sync-employee`
- `GET /api/desktop/workstations`

## Employee Access Model

Workstation passcode login reuses the employee PIN hash model already used in RunBook Desktop:

- `mobile_pin_salt_base64`
- `mobile_pin_hash_base64`

Desktop resolves workstation access per employee with:

- `workstation_access_enabled`
- `can_timeclock`
- `can_dashboard_view`
- `can_jobs_module`
- `can_inspection_entry`
- `can_camera_view`
- `workstation_session_timeout_minutes`

## Build

From `D:\RunBook.Workstation`:

```powershell
dotnet build .\RunBook.Workstation.sln
```
