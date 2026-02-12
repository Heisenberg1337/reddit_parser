FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/RedditVideoBot/RedditVideoBot.csproj ./RedditVideoBot/
RUN dotnet restore RedditVideoBot/RedditVideoBot.csproj
COPY src/RedditVideoBot ./RedditVideoBot/
WORKDIR /src/RedditVideoBot
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /tmp/RedditVideoBot
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RedditVideoBot.dll"]
