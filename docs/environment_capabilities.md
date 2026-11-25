# Environment capabilities (audited on this host)

| Tool | Result |
| --- | --- |
| .NET SDK | `10.0.403` installed; every project explicitly targets `net8.0` |
| EF Core CLI | local tool manifest, `dotnet-ef 8.0.8` (`dotnet tool restore`) |
| SQL Server | `SQLLocalDB` present, instance `MSSQLLocalDB` reachable — migrations applied for real |
| Docker | **not installed** — `docker-compose.yml` is authored but cannot be run on this host |
| Node.js | `v24.19.0` |
| npm | `11.17.0` |
| Tesseract (system) | **not installed**; only the NuGet package plus the bundled `tessdata/ara.traineddata` are used, so nothing needs to be installed to run the API |
| Network at runtime | none required by the API; OCR, language data and fonts ship offline |

Notes:
- `dotnet run` defaults to the Production environment; Swagger is only mapped in Development, so use
  `ASPNETCORE_ENVIRONMENT=Development` when you want the Swagger UI.
- SQL Server LocalDB is Windows only. On Linux use the SQL Server container from
  `docker-compose.yml` and point `ConnectionStrings:IdCardReaderDb` at it.
