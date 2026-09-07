FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Tools/BattleCore/TeamfightTCG.BattleCore.csproj Tools/BattleCore/
COPY Services/BattleReplay/TeamfightTCG.BattleReplayService.csproj Services/BattleReplay/
RUN dotnet restore Services/BattleReplay/TeamfightTCG.BattleReplayService.csproj

COPY Assets/Scripts/BattleCore/ Assets/Scripts/BattleCore/
COPY Services/BattleReplay/ Services/BattleReplay/
RUN dotnet publish Services/BattleReplay/TeamfightTCG.BattleReplayService.csproj \
    --configuration Release --output /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV FIRESTORE_DATABASE_ID=cardbattle
COPY --from=build /app/ .
USER $APP_UID
ENTRYPOINT ["dotnet", "TeamfightTCG.BattleReplayService.dll"]
