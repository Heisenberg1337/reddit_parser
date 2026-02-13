FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/NamelessDeity/NamelessDeity.csproj ./NamelessDeity/
RUN dotnet restore NamelessDeity/NamelessDeity.csproj
COPY src/NamelessDeity ./NamelessDeity/
WORKDIR /src/NamelessDeity
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /tmp/NamelessDeity
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NamelessDeity.dll"]
