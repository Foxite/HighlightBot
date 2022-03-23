FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["HighlightBot/HighlightBot.csproj", "HighlightBot/"]
RUN dotnet restore "HighlightBot/HighlightBot.csproj"
COPY . .
WORKDIR "/src/HighlightBot"
RUN dotnet build "HighlightBot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HighlightBot.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HighlightBot.dll"]
