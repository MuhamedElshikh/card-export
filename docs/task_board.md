# Task board

Legend: `[x]` done and verified, `[~]` in progress, `[ ]` not started.

## Phase 1 — Domain + OCR spike
- [x] `NationalIdNumber` parser: Arabic-Indic + Persian digit normalization, 14 digits, century digit,
      YYMMDD real-date and not-in-future checks, governorate codes 01-04/11-20/22-29/31-35/88, gender.
- [x] `NationalIdResult` derived fields.
- [x] Bundled offline Arabic LSTM model (`ara.traineddata`, 2.4 MB).
- [x] `TesseractIdCardOcrService` with 3 passes + majority vote + low-confidence flag.
- [x] Parser tests (19 application tests green).

## Phase 2 — API + database
- [x] EF Core `IdCard` entity, unique index on `NationalId`, SQL Server migration `InitialCreate`.
- [x] Migration applied to SQL Server LocalDB (verified by `dotnet ef database update`).
- [x] `POST /api/idcards/scan` (5 MB limit, magic-byte check, rate limited, nothing persisted).
- [x] `POST /api/idcards` (server-side re-validation, 409 on duplicate, FluentValidation).
- [x] `GET /api/idcards` (paged + search), `GET /api/idcards/{id}`.
- [x] ProblemDetails with translatable `code` for every failure path.
- [x] AES-GCM image store behind `Storage:StoreImage` (default off).
- [x] Tests: 19 application + 5 infrastructure + 12 API = 36 green.
- [x] Verified live: HTTP create/list/get against LocalDB, scan returns 200 with
      `nationalId=null, lowConfidence=true` for a blank image (no fabricated data).

## Phase 3 — Angular frontend
- [x] Scaffold standalone app (Angular 22, signals, OnPush, ngx-translate, RTL/LTR, Arabic default).
- [x] Scan page: upload, preview, review/correct form, live derived fields, save.
- [x] Records page: paged list + search.
- [x] Translatable error codes in both languages.
- [x] 23 frontend tests green (parser, API client, review form).
- [x] Verified in a real browser against the live API: upload -> OCR preview -> manual correction of
      Arabic-Indic digits -> derived fields recomputed -> save -> record 2 stored in SQL Server and
      listed on the records page with Arabic dates and governorate names.

## Phase 4 — Packaging
- [x] `Api/Dockerfile` (multi-stage, non-root, libgomp1 for Tesseract).
- [x] `web/Dockerfile` + `web/nginx.conf` (same-origin `/api` proxy, SPA fallback, 6 MB body limit).
- [x] `docker-compose.yml` (api + SQL Server 2022 + web, healthcheck, named volumes).
- [x] `.env.example`, `.dockerignore`, `web/.dockerignore`.
- [ ] Not executed: Docker is not installed on this host (see `docs/environment_capabilities.md`).

## Phase 5 — Documentation + eval
- [x] `docs/architecture.md`
- [x] `README.md` (prerequisites, run, config, API, tests, privacy, honest limitations)
- [x] `Tools/IdCards.Eval` console harness (accuracy, lowConfidence share, avg/p95 latency, peak RAM).
- [ ] Real accuracy/latency/RAM numbers: waiting for the user's own ID images.

## Known gaps (honest list)
- Card contour detection + perspective correction is not implemented (no usable OpenCV binding on net8).
- Per-field crops from a JSON layout template are not implemented; the whole image is OCR'd.
- No measured accuracy numbers yet: the harness exists, the images must come from the user.
