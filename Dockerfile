# syntax=docker/dockerfile:1

FROM node:22-bookworm-slim AS web-build
WORKDIR /web
COPY src/MoodleConnector.Web/package.json src/MoodleConnector.Web/package-lock.json ./
RUN npm ci --ignore-scripts --no-audit --no-fund
COPY src/MoodleConnector.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MoodleConnector.slnx ./
COPY src/MoodleConnector.Domain/MoodleConnector.Domain.csproj src/MoodleConnector.Domain/
COPY src/MoodleConnector.Application/MoodleConnector.Application.csproj src/MoodleConnector.Application/
COPY src/MoodleConnector.Infrastructure/MoodleConnector.Infrastructure.csproj src/MoodleConnector.Infrastructure/
COPY src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj src/MoodleConnector.Presentation/
RUN dotnet restore src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj

COPY src/ src/
COPY public/ public/
COPY --from=web-build /web/dist/ src/MoodleConnector.Presentation/wwwroot/

RUN dotnet publish src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       curl ca-certificates \
       tesseract-ocr \
       tesseract-ocr-por \
       libleptonica-dev \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
ENV Features__AppV2Enabled=true
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "MoodleConnector.Presentation.dll"]

