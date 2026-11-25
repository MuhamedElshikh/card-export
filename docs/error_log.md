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
