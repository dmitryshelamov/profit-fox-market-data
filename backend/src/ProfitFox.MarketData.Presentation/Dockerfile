# The image of the service. Build context: backend/, which holds the shared build settings and package versions.
# It listens where the Kestrel section of the settings says; docker-compose.prod.yml opens it on 5236 (REST) and 5237 (gRPC).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/ProfitFox.MarketData.Presentation/ProfitFox.MarketData.Presentation.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# The healthcheck of docker-compose.prod.yml asks /health with curl, which the image lacks.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "ProfitFox.MarketData.Presentation.dll"]
