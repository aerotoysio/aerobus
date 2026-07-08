# Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/AeroBus.Core/AeroBus.Core.csproj src/AeroBus.Core/
COPY src/AeroBus.Api/AeroBus.Api.csproj src/AeroBus.Api/
RUN dotnet restore src/AeroBus.Api/AeroBus.Api.csproj
COPY src/ src/
RUN dotnet publish src/AeroBus.Api/AeroBus.Api.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Build provenance surfaced by GET /version — set by CI at image build time.
ARG BUILD_SHA
ARG BUILD_TIME
ARG BUILD_IMAGE
ENV BUILD_SHA=$BUILD_SHA BUILD_TIME=$BUILD_TIME BUILD_IMAGE=$BUILD_IMAGE

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AeroBus.Api.dll"]
