# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/MarketingStudio.Domain/MarketingStudio.Domain.csproj src/MarketingStudio.Domain/
COPY src/MarketingStudio.Application/MarketingStudio.Application.csproj src/MarketingStudio.Application/
COPY src/MarketingStudio.Infrastructure/MarketingStudio.Infrastructure.csproj src/MarketingStudio.Infrastructure/
COPY src/MarketingStudio.Api/MarketingStudio.Api.csproj src/MarketingStudio.Api/
RUN dotnet restore src/MarketingStudio.Api/MarketingStudio.Api.csproj
COPY src/ src/
RUN dotnet publish src/MarketingStudio.Api/MarketingStudio.Api.csproj -c Release -o /app --no-restore

# Run (non-root user provided by the base image)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Writable dirs for the non-root user: dev-generated keys (/keys) and the Data Protection key ring.
# A new named volume mounted here inherits this ownership.
RUN mkdir -p /keys /var/lib/marketingstudio/dataprotection-keys \
    && chown -R $APP_UID /keys /var/lib/marketingstudio
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Health is probed externally at /health/live and /health/ready (the runtime image has no curl).
ENTRYPOINT ["dotnet", "MarketingStudio.Api.dll"]
