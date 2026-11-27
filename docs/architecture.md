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
| POST | `/api/idcards/scan` | multipart `frontImage` + `backImage` (JPEG/PNG, <= 5 MB each). Returns a preview. **Persists nothing.** |
| POST | `/api/idcards` | Stores a reviewed card. Re-validates the national ID server side. |
| GET | `/api/idcards?search=&page=1&pageSize=20` | Paged list, search matches name or national ID. |
| GET | `/api/idcards/{id}` | Single card. |
| GET | `/health` | Liveness probe. |

`POST /api/idcards` request:

```json
{ "nationalId": "29001011234567", "fullName": "…", "address": "…", "occupation": "…", "maritalStatus": "…", "expiryDate": "2030-01-01", "lowConfidence": false, "imageBase64": null }
```

`imageBase64` is only accepted when `Storage:StoreImage=true`; otherwise the call fails with
`image_storage_disabled`. Derived fields (`birthDate`, `isMale`, governorate) are **never** taken from
the client, they are recomputed from the national ID number.

Errors are RFC 7807 `application/problem+json` with a translatable `code` extension:
`front_image_required`, `back_image_required`, `image_too_large`, `unsupported_image_format`, `scan_failed`, `invalid_national_id`,
`duplicate_national_id`, `validation_failed`, `image_storage_disabled`, `id_card_not_found`.

## Database

Single table `IdCards`:

| Column | Type | Notes |
| --- | --- | --- |
| Id | int identity | PK |
| NationalId | varchar(14) | **unique index** `IX_IdCards_NationalId` -> duplicate returns 409 |
| FullName | nvarchar(200) | required |
| Address | nvarchar(500) | required, may be empty |
| Occupation | nvarchar(200) | optional, reviewed OCR from the back |
| MaritalStatus | nvarchar(100) | optional, reviewed OCR |
| ExpiryDate | date | optional, read from the back |
| BirthDate | date | derived |
| IsMale | bit | derived from digit 13 |
| GovernorateCode | varchar(2) | derived from digits 8-9 |
| GovernorateEnglish / GovernorateArabic | nvarchar(100) | lookup |
| LowConfidence | bit | OCR confidence at review time |
| ImageReference | nvarchar(400) | null unless image storage is enabled |
| CreatedAt | datetime2 | UTC |

Migrations: `Infrastructure/Persistence/Migrations/`.
`Database:Provider=Sqlite` exists for local/dev and tests only; SQL Server uses real migrations,
SQLite builds the schema from the model (`EnsureCreated`).

## OCR pipeline (v2)

`TesseractIdCardOcrService` (offline, no image upload, no GPU):

1. The API requires both sides and checks JPEG/PNG magic bytes and the 5 MB per-image limit before OCR is invoked.
2. `ImagePreprocessor` decodes with SkiaSharp, applies EXIF orientation, scales to
   `Ocr:NumberLongEdge` (1100 px; tiny images are raised to at least 900 px), converts to grayscale
   with BT.601 luma, stretches the histogram between the 1st and 99th percentile, estimates skew,
   deskews and sharpens. This sequence runs for both images on every scan.
3. A projection-profile search removes small text skew. It returns 0 when the image has no dominant
   horizontal direction (a sideways photo), because rotating then would add skew instead of removing it.
4. **Glyph reader (first):** the card prints the number in a decorative Arabic-Indic font (dot-shaped
   zeros) that the stock Tesseract models cannot read. `GlyphDigitReader` finds a row of exactly 14
   components in the binarised image, normalises each glyph and matches it against the template bank
   `Infrastructure/Imaging/glyph-templates.json` (nearest neighbour, position priors for century,
   month and day digits, distance limits, domain validation). Front and back are both read; when they
   agree the Tesseract/PP-OCR number passes are skipped. A single glyph read is only a vote.
   Add templates from your own cards with
   `IdCards.Eval --export-glyphs photo.jpg <14 digit number> glyph-templates.json` (only 28x28 glyph
   bitmaps are written). The seed bank has no digits 1 and 7: such glyphs are rejected and the
   Tesseract passes below take over until templates for them are added.
4b. The Tesseract number search tries orientations 0°, 180°, 90°, 270° on a local-mean binarised image
   (tolerates shadows and uneven light), then 0°/180° with a global Otsu threshold if nothing
   convincing was found. Each orientation is read twice: `eng` with a Western-digit whitelist
   (much better at printed digits) and `ara` with Western plus Arabic-Indic digits. The search
   stops as soon as two direct passes agree. When the result is low-confidence and Tesseract saw
   digits, a lazily loaded PP-OCRv5 ONNX engine reads the prepared image in four orientations,
   trying Latin and then Arabic recognition.
5. **No invented data:** `DigitCandidates` accepts a number only when one OCR line contains exactly
   14 digits (spaces between printed groups are ignored) and the value passes domain validation.
   Sliding windows inside longer digit runs and digits joined across lines are never accepted —
   they are arithmetic coincidences. The winner needs >= 60 % of the votes **and** at least two
   independent direct reads; a single read is reported as `lowConfidence=true`. PP-OCRv5 is used
   only when a whole validated line has model confidence >= 0.90 and only one distinct valid
   candidate remains. A fallback result stays `lowConfidence=true` for user review.
6. Front-side name/address are read at `Ocr:TextLongEdge` (1400 px, after the card crop) on the winning
   orientation with the accurate Arabic model when `tessdata-best` is deployed (fast model otherwise).
   Up to three variants run (grayscale Auto, grayscale SparseText, binarised Auto) and the best
   name/address fields are merged. `ArabicTextFields` strips the **whole** label
   (including `بالكامل`, which otherwise becomes the "name"), requires at least two name tokens,
   rejects card headings and common address markers, and can join a preceding single name token to
   an unlabelled name line. Cards without labels: the one or two address-like lines after the name
   are taken as the address. Both stay user-editable.
7. The back is scanned at the same text resolution and orientations. `ArabicBackFields` first uses
   labels when present, then handles the unlabeled Egyptian layout: it recognizes marital status
   only from a fixed vocabulary and takes up to three consecutive text lines before that status row
   as the occupation/qualification. If Tesseract leaves fields missing, PP-OCRv5 retries Arabic text
   recognition and contributes only lines with confidence >= 0.75. The expiry parser also recognizes
   `البطاقة سارية حتى` and a unique unlabeled full date; Arabic-Indic date digits are normalized.
   Ambiguous or missing values stay empty for user review.
8. Birth date, gender and governorate are **derived from the accepted national ID**. When the ID is
   not accepted they stay empty.
9. Tesseract engines are pooled and leased, never created per request. The PP-OCRv5 engine is loaded
   lazily once and reused. Concurrency is bounded by `SemaphoreSlim(Ocr:MaxConcurrentScans)`
   (default 1) so a dual-core box does not thrash.
10. Intermediate PNGs live in a unique temp directory deleted in a `finally` block. Scan logs record
   vote count, share, rotation, skew, pass count and fallback field-presence flags — never names,
   addresses, numbers or bytes.

**Card crop:** `CardRegion` locates the card in the photo (Sobel edges, two long horizontal and
two long vertical borders, ID-1 aspect ratio, border-support scoring) and crops away the
background before any OCR pass. Detection is conservative — any doubt keeps the full image.
Cropping removes background text noise and gives every pass more pixels per character.
**Still not implemented:** perspective warp of a tilted card. That needs a contour/Hough
pipeline that no usable net8.0 binding provides here; the crop is axis-aligned.

## Decisions and deviations

| Decision | Reason |
| --- | --- |
| OpenCvSharp not used | only `OpenCvSharp` 2.4.0.1 is on NuGet and targets .NET Framework; `Mat` is unusable on net8.0. Card contour detection / perspective warp is therefore **not implemented**. SkiaSharp handles EXIF, scaling, grayscale, contrast, binarisation, deskew and rotation instead. |
| No AutoMapper | 13.0.1 carries three high severity advisories (GHSA-6vhm-j9h7-f9gh, GHSA-gpwp-5797-mjff, GHSA-rvv3-g6hj-g44x) and 15+ is commercially licensed. A single explicit mapper is used instead. |
| No Ardalis.Specification | the query surface is one filtered, ordered, paged read; a specification layer would add a dependency without removing any complexity. |
| SkiaSharp 2.88.9 | MIT licensed, Windows + Linux native assets, no fontconfig needed (`NativeAssets.Linux.NoDependencies`). ImageSharp 2.1 is Apache-2.0 but has decoder advisories, 3.x changes licence; System.Drawing.Common is Windows-only. |
| Bundled `eng.traineddata` | tessdata_fast English model (~4.1 MB) reads printed Western digits far better than the Arabic LSTM model; the Arabic model is still used for Arabic-Indic digits and all text. Both ship offline. |
| `EngineMode.LstmOnly` | both bundled models are tessdata_fast with no legacy components, so OEM 3 fails to load them. |
| PP-OCRv5 fallback | `PdfNetStack.Ocr` (MIT) runs PP-OCRv5 ONNX locally; the separate Arabic/Latin model package (Apache-2.0 models, about 21 MB) is copied into build/publish output. The fallback runs only for low-confidence Tesseract reads with digit evidence, requires confidence >= 0.90 and one unique, domain-valid 14-digit line, and remains flagged for review. |
| Pooled engines | creation and model loading cost hundreds of milliseconds; engines are leased from a small pool instead of being rebuilt for each scan. Tesseract engines are not thread safe. |
| Images off by default | `Storage:StoreImage=false`. When enabled, AES-256-GCM blobs are written outside the web root and only an opaque reference is stored. |
| Logs contain no PII | scan logs record duration, vote count, winner share, direct-read flag, rotation, skew, pass count, `PaddleFallbackUsed`, `NationalIdFound` and `LowConfidence` only — never names, addresses, numbers or image bytes. |
