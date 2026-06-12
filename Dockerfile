# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MoodleConnector.slnx ./
COPY src/MoodleConnector.Domain/MoodleConnector.Domain.csproj src/MoodleConnector.Domain/
COPY src/MoodleConnector.Application/MoodleConnector.Application.csproj src/MoodleConnector.Application/
COPY src/MoodleConnector.Infrastructure/MoodleConnector.Infrastructure.csproj src/MoodleConnector.Infrastructure/
COPY src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj src/MoodleConnector.Presentation/

RUN dotnet restore src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj

COPY src/ src/

RUN dotnet publish src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "MoodleConnector.Presentation.dll"]
