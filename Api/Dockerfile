# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore is done against the project files only so the layer cache survives source edits.
COPY Domain/IdCardReader.Domain.csproj Domain/
COPY Application/IdCardReader.Application.csproj Application/
COPY Infrastructure/IdCardReader.Infrastructure.csproj Infrastructure/
COPY Api/IdCardReader.Api.csproj Api/
RUN dotnet restore Api/IdCardReader.Api.csproj

COPY Domain/ Domain/
COPY Application/ Application/
COPY Infrastructure/ Infrastructure/
COPY Api/ Api/

RUN dotnet publish Api/IdCardReader.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage (Debian based image: the Tesseract native libraries shipped by the NuGet package
# are built for glibc, they will not load on Alpine/musl).
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

RUN useradd -r -u 1001 appuser && chown -R appuser /app
USER appuser

EXPOSE 8080
ENTRYPOINT ["dotnet", "IdCardReader.Api.dll"]
