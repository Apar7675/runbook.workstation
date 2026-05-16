# RunBook Agent Rules

This repo is moving toward `RunBook.Service`, not `RunBook.Desktop`.
Do not deepen long-term dependency on Desktop.
Local runtime integration must not depend on Desktop UI lifetime.

## 1. Purpose

This file enforces RunBook architecture for AI-generated changes.

Architecture violations are not allowed even if the code compiles.

## 2. Authority Reference

- `Docs/Architecture/RunBook-System-Bible.md` is the top authority.
- App bibles in `Docs/Architecture/` are secondary authorities for app-local boundaries.
- `Docs/Architecture/RunBook-Production-Packaging-Bible.md` is authoritative for production packaging and runtime containment.
- If this file conflicts with the System Bible, the System Bible wins.
- This repo does not carry `Docs/Architecture/` locally. Reference the shared architecture docs at `D:\RunBook.Desktop\Docs\Architecture\`.

## 3. Non-Negotiable Architecture Rules

- `RunBook.Service` owns runtime execution.
- `RunBook.Runtime` is headless logic only. It contains no UI and no WPF dependencies.
- `RunBook.Desktop` is UI, configuration, preview, and calibration only.
- Desktop must never own runtime again.
- Runtime truth has exactly one writer: `RunBook.Service`.
- Preview is not monitoring.
- No duplicate source of truth is allowed.
- `RunBook.Workstation` must move toward `RunBook.Service`, not `RunBook.Desktop`.
- `RunBook.Control` is the remote authority for cloud, admin, billing, entitlement, and access policy concerns.
- `RunBook.Mobile` is a remote consumer and must not bypass approved authority boundaries.

## 4. Forbidden Actions (Hard Blocks)

- DO NOT start runtime from Desktop.
- DO NOT call `EnsureStarted` or `EnsureStartedAsync` from UI, ViewModels, or Desktop startup paths.
- DO NOT call `StartMonitoring` or `StopMonitoring` from UI or ViewModels.
- DO NOT reintroduce `CameraRuntime` or any static god-object that mixes runtime, UI, preview, repository, or workflow concerns.
- DO NOT make `RunBook.Service` depend on `RunBook.Desktop`.
- DO NOT mix preview or calibration code into headless runtime code.
- DO NOT create multiple writers of runtime truth.
- DO NOT read runtime state directly from runtime host objects in Desktop.
- DO NOT create new cross-app authority docs outside `Docs/Architecture/`.
- DO NOT duplicate source-of-truth rules in multiple places.
- DO NOT make production Workstation packages depend on machine-global .NET, Visual Studio, Python, OCR, Pdfium, OpenCV, repo-local development folders, or hard-coded local paths.

## 5. Runtime Ownership Rules

- Runtime execution happens only inside `RunBook.Service`.
- `RunBook.Runtime` contains logic only.
- Desktop may read only persisted runtime outputs owned by `RunBook.Service`.
- Runtime state is written by `RunBook.Service` only.

## 6. Data / Source-Of-Truth Rules

- Company data lives in the company shell and is local authority.
- Runtime state lives in runtime-specific storage.
- `RunBook.Service` writes runtime state.
- `RunBook.Desktop` reads runtime state.
- `RunBook.Control` owns cloud, account, billing, entitlement, and access-policy data.
- Overlapping ownership is not allowed.

## 7. Communication Rules

- Desktop reads Service-owned runtime outputs.
- Service writes runtime outputs.
- Workstation must target Service, not Desktop, for long-term local runtime integration.
- Mobile uses approved remote authority paths through Control and backing remote services.
- UI code must not call runtime ownership paths directly.

## 8. Change Control Rules

- Before making architecture changes, verify them against `Docs/Architecture/RunBook-System-Bible.md`.
- If a change affects runtime ownership, storage, communication, or source-of-truth rules, re-check the System Bible before editing code.
- If a change affects production publishing, installer shape, runtime containment, OCR/native dependency packaging, or developer-tool exclusion, re-check the Production Packaging Bible before editing code.
- If a change conflicts with the System Bible or an app bible, stop.
- If unsure, stop and ask for clarification.

## 9. Instruction For Missing Context

If a change depends on architecture details not present in the current context, do not guess. Ask for the relevant System Bible or app bible section before proceeding.
