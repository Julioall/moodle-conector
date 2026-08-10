# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MoodleConnector.slnx ./
COPY src/MoodleConnector.Domain/MoodleConnector.Domain.csproj src/MoodleConnector.Domain/
COPY src/MoodleConnector.Application/MoodleConnector.Application.csproj src/MoodleConnector.Application/
COPY src/MoodleConnector.Infrastructure/MoodleConnector.Infrastructure.csproj src/MoodleConnector.Infrastructure/
COPY src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj src/MoodleConnector.Presentation/
COPY src/MoodleConnector.Web/package.json src/MoodleConnector.Web/package-lock.json src/MoodleConnector.Web/

RUN cd src/MoodleConnector.Web && npm ci --ignore-scripts --no-audit --no-fund

RUN dotnet restore src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj

COPY src/ src/
COPY public/ public/

RUN cd src/MoodleConnector.Web && npm run build
RUN mkdir -p src/MoodleConnector.Presentation/wwwroot/portal && cp -r src/MoodleConnector.Web/dist/. src/MoodleConnector.Presentation/wwwroot/portal/

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
ENV Features__PortalV2Enabled=true
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "MoodleConnector.Presentation.dll"]
