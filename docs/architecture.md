# Architecture

## Solution layout (Clean Architecture, .NET 8)

```
IdCardReader.slnx
├── Domain/            NationalIdNumber, NationalIdResult, IdCard        (no dependencies)
├── Application/       DTOs, ports (IIdCardRepository, IUnitOfWork,       -> Domain
│                      IImageStore, IIdCardOcrService), IdCardService,
│                      FluentValidation rules
├── Infrastructure/    EF Core (SQL Server), Tesseract OCR, AES-GCM       -> Application
│                      image store
├── Api/               Controllers, rate limiting, ProblemDetails        -> Application, Infrastructure
└── Tests/
    ├── Application.Tests/              parser + service unit tests
    ├── Infrastructure.IntegrationTests EF model + repository (SQLite)
    └── Api.Tests/                      HTTP contract (WebApplicationFactory)
```

Dependency rule: Domain knows nothing, Application knows Domain, Infrastructure knows Application,
Api knows Application + Infrastructure. Nothing points inwards except through interfaces.

## HTTP contract

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/idcards/scan` | multipart `image` (JPEG/PNG, <= 5 MB). Returns a preview. **Persists nothing.** |
| POST | `/api/idcards` | Stores a reviewed card. Re-validates the national ID server side. |
| GET | `/api/idcards?search=&page=1&pageSize=20` | Paged list, search matches name or national ID. |
| GET | `/api/idcards/{id}` | Single card. |
| GET | `/health` | Liveness probe. |

`POST /api/idcards` request:

```json
{ "nationalId": "29001011234567", "fullName": "…", "address": "…", "lowConfidence": false, "imageBase64": null }
```

`imageBase64` is only accepted when `Storage:StoreImage=true`; otherwise the call fails with
`image_storage_disabled`. Derived fields (`birthDate`, `isMale`, governorate) are **never** taken from
the client, they are recomputed from the national ID number.

Errors are RFC 7807 `application/problem+json` with a translatable `code` extension:
`image_required`, `image_too_large`, `unsupported_image_format`, `scan_failed`, `invalid_national_id`,
`duplicate_national_id`, `validation_failed`, `image_storage_disabled`, `id_card_not_found`.

## Database

Single table `IdCards`:

| Column | Type | Notes |
| --- | --- | --- |
| Id | int identity | PK |
| NationalId | varchar(14) | **unique index** `IX_IdCards_NationalId` -> duplicate returns 409 |
| FullName | nvarchar(200) | required |
| Address | nvarchar(500) | required, may be empty |
| BirthDate | date | derived |
| IsMale | bit | derived from digit 13 |
| GovernorateCode | varchar(2) | derived from digits 8-9 |
| GovernorateEnglish / GovernorateArabic | nvarchar(100) | lookup |
| LowConfidence | bit | OCR confidence at review time |
| ImageReference | nvarchar(400) | null unless image storage is enabled |
| CreatedAt | datetime2 | UTC |

Migration: `Infrastructure/Persistence/Migrations/*_InitialCreate.cs`.
`Database:Provider=Sqlite` exists for local/dev and tests only; SQL Server uses real migrations,
SQLite builds the schema from the model (`EnsureCreated`).

## OCR pipeline

`TesseractIdCardOcrService` (offline, no network, no GPU):

1. Accepts only real JPEG/PNG payloads (magic bytes checked before the engine is touched).
2. Runs three passes on the same image with the bundled Arabic LSTM model:
   free text (PSM 6), digit-restricted (PSM 6), digit-restricted sparse (PSM 11).
3. Extracts every 14-digit window, keeps only candidates that pass domain validation.
4. Majority vote: winner needs >= 60 % of votes **and** at least one direct read; otherwise
   `lowConfidence=true`. Sliding windows inside a longer digit run are treated as the fallback path.
5. Name/address come from the free-text pass, label-stripped and de-duplicated; they are always
   user-editable because OCR of Arabic handwriting-style fonts is best effort.
6. Concurrency is bounded by `SemaphoreSlim(Ocr:MaxConcurrentScans)` (default 1) so a dual-core box
   does not thrash.

## Decisions and deviations

| Decision | Reason |
| --- | --- |
| OpenCvSharp not used | only `OpenCvSharp` 2.4.0.1 is on NuGet and targets .NET Framework; `Mat` is unusable on net8.0. Card contour detection / perspective warp is therefore **not implemented** — the service reads the whole image. |
| No AutoMapper | 13.0.1 carries three high severity advisories (GHSA-6vhm-j9h7-f9gh, GHSA-gpwp-5797-mjff, GHSA-rvv3-g6hj-g44x) and 15+ is commercially licensed. A single explicit mapper is used instead. |
| No Ardalis.Specification | the query surface is one filtered, ordered, paged read; a specification layer would add a dependency without removing any complexity. |
| `EngineMode.LstmOnly` | the bundled `ara.traineddata` (tessdata_fast) has no legacy components, OEM 3 fails to load it. |
| Images off by default | `Storage:StoreImage=false`. When enabled, AES-256-GCM blobs are written outside the web root and only an opaque reference is stored. |
| Logs contain no PII | scan logs record duration, `NationalIdFound` and `LowConfidence` only — never names, addresses, numbers or image bytes. |
