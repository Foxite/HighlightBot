﻿FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine AS build
WORKDIR /src
COPY ["HighlightBot.csproj", "HighlightBot.csproj"]
RUN dotnet restore "HighlightBot.csproj"
COPY . .
RUN mv appsettings.json.example appsettings.json
RUN dotnet publish "HighlightBot.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:6.0-alpine AS final

ARG HIGHLIGHTBOT_VERSION

WORKDIR /app
COPY --from=build /app/publish .

ENV HIGHLIGHTBOT_VERSION=$HIGHLIGHTBOT_VERSION

ENTRYPOINT ["./HighlightBot"]
