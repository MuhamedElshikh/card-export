# Error log

Root causes of failures hit while building this project, so they are not repeated.

## 1. `CS1026: ) expected` in `Program.cs` (blocked the API for several attempts)
**Symptom:** the build failed at the last line of the CORS block, no matter how the file was rewritten.
**Cause:** `options.AddPolicy("AllowAngularApp", builder => builder...AllowCredentials();` was missing the
closing parenthesis of `AddPolicy(`. The compiler reports the error at end of line, which made it look
like an encoding or project problem.
**Fix:** close both calls: `.AllowCredentials());`.

## 2. Swagger 500: "Error reading parameter(s) ... as [FromForm] attribute used with IFormFile"
**Cause:** Swashbuckle 6.6.2 refuses `[FromForm] IFormFile` parameters.
**Fix:** drop the attribute. ASP.NET Core already infers form-file binding for `IFormFile` parameters.

## 3. `SQLite Error 19: 'NOT NULL constraint failed: IdCards.Id'` in API tests
**Cause:** the API startup ran the SQL Server migration against SQLite; the identity annotation is
provider specific, so the key was not generated.
**Fix:** `ApplyMigrationsAsync` uses `MigrateAsync()` for SQL Server and `EnsureCreatedAsync()` when the
provider is SQLite (dev/test only).

## 4. `TesseractException: Failed to initialise tesseract engine` / "Tesseract (legacy) engine requested"
**Cause:** the bundled `ara.traineddata` is from `tessdata_fast` and contains no legacy engine data,
but the code asked for `EngineMode.TesseractAndLstm` (OEM 3).
**Fix:** `EngineMode.LstmOnly` (OEM 1).

## 5. `ArgumentException: The path is empty` on every API request
**Cause:** `appsettings.json` had `"Storage:Path": ""`, and `settings.Path ?? default` treats the empty
string as a real value, so `Path.GetFullPath("")` threw while the singleton store was constructed.
**Fix:** treat null/whitespace as "not configured".

## 6. `NU1605` package downgrade (EF Core 8.0.0 vs SqlServer 8.0.8)
**Cause:** mixing EF Core versions across packages.
**Fix:** pin every EF package to 8.0.8.

## 7. `CS1739: the best overload for 'SaveIdCardRequest' does not have a parameter named 'lowConfidence'`
**Cause:** named arguments are case sensitive; the record parameter is `LowConfidence`.
**Fix:** match the exact parameter name.

## 8. Corrupted Arabic governorate strings
**Cause:** the original lookup table had mojibake/English mixed into Arabic values
(`دمietta`, `بني Suef`, `الفيمية`).
**Fix:** replaced with the correct Arabic names and updated the assertion in the parser test.

## 9. "Data completely wrong and no national ID" on real card photos
**Symptom:** the user uploaded a photo; the preview had no national ID and the name/address were
text taken from the background.
**Causes (all in the v1 OCR pipeline):**
- the whole image was OCR'd with no EXIF orientation, scaling, contrast, binarisation or deskew;
- the Arabic LSTM model was used for digits, although the English model is much better at them;
- the digit runs were split by the spaces that Egyptian cards print between digit groups, so no
  14-digit run ever reached the validator;
- `PageSegMode.SingleBlock` assumed one uniform text block, which a photo of a card is not;
- name/address accepted any line with two letters, so background noise became data.
**Fix:** the v2 pipeline in `TesseractIdCardOcrService` + `Imaging/*`: SkiaSharp preprocessing,
skew removal, four orientations with early exit, `eng` + `ara` digit passes, whitespace-tolerant
candidate extraction, strict name/address filtering, pooled engines. See `docs/architecture.md`.

## 10. Sideways fixture produced no number after the v2 rewrite
**Cause:** `EstimateSkewDegrees` picked the first angle when every angle scored almost equally
(vertical text has no dominant horizontal projection), adding ~8° of skew to an already rotated image.
**Fix:** return 0 unless the best projection score is at least 15 % above the upright baseline.

## 11. `ResizeToLongEdge` upscaled every image
**Cause:** `targetEdge` defaulted to the configured maximum instead of the image's own long edge,
so a 200×100 image was blown up to 1600 px on every scan.
**Fix:** keep the current size when it is smaller than the maximum, and only raise it when
`minimumLongEdge` is set. Covered by `ImagePreprocessorTests`.

## 12. User: "guessed data, no first name, card number completely wrong"
**Reproduced with failing tests before any fix** (`dotnet test` on 47 infrastructure tests):
- `Assert.Empty()` failed: OCR noise `990012900101123456789` produced a candidate
  `29001011234567` via a sliding 14-digit window; digits split over lines were joined as a
  "document fallback". Both passes could produce the same coincidence, so it won the vote,
  became `NationalId` and generated a birth date/gender/governorate from thin air.
- `Assert.Null()` failed: unlabelled text `جمهورية مصر العربية` became the full name.
- `Assert.Equal()` failed: `الاسم بالكامل: أحمد محمد علي` produced the name
  `بالكامل: أحمد محمد علي` (and `الاسم بالكامل` alone produced `بالكامل`) because only
  `الاسم` was stripped.
**Fix:**
- `DigitCandidates.Extract` now accepts only an exact 14-digit run on one line; no windows, no
  joined lines, no fallback. `VoteResult.LowConfidence` also requires two independent direct reads.
- `ArabicTextFields` strips the complete label, requires 2+ name tokens, rejects card headings and
  never promotes unlabelled text to name/address.
- Tests rewritten to assert the absence of invented values. 78 backend tests green.
**Still open:** real-photo digit/name accuracy needs the user's own image; no sample IDs may be
fabricated to measure it.
