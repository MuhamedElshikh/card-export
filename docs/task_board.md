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
- [ ] Scaffold standalone app (signals, OnPush, ngx-translate, RTL/LTR, Arabic default).
- [ ] Scan page: upload, preview, review/correct form, save.
- [ ] Records page: paged list + search.
- [ ] Translatable error codes.

## Phase 4 — Packaging
- [ ] `Dockerfile` (multi-stage) + `docker-compose.yml` (api + SQL Server).
- [ ] `.env.example`, healthchecks, volume for SQL data.

## Phase 5 — Documentation
- [x] `docs/architecture.md`
- [ ] `README.md` (prerequisites, run, config, privacy).
- [ ] Eval harness `IdCards.Eval` (needs real images from the user).

## Known gaps (honest list)
- Card contour detection + perspective correction is not implemented (no usable OpenCV binding).
- Per-field crops from a JSON layout template are not implemented; the whole image is OCR'd.
- No measured accuracy/latency/RAM numbers yet: that requires real ID images supplied by the user.
