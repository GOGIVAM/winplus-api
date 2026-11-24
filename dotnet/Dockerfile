# Image de production de l'API .NET WinPlus.
# Construite par le CI (.github/workflows/build-images.yml), jamais sur le serveur.
#
#   docker build -t winplus-dotnet ./dotnet
#
# Le conteneur tourne en utilisateur non root (« app », fourni par l'image
# Microsoft), avec un système de fichiers en lecture seule (voir
# deploy/docker/compose.yml). Seuls les dossiers ci-dessous sont inscriptibles,
# et chacun est un volume persistant :
#   /app/logs                          journaux Serilog (logs/winplus-.txt)
#   /app/wwwroot/uploads               repli local quand S3 est indisponible
#   /app/private_uploads               fichiers privés (PrivateFileStore)
#   /home/app/.aspnet/DataProtection-Keys  clés ASP.NET (cookies chiffrés)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restauration seule d'abord : la couche reste en cache tant que le .csproj ne change pas.
COPY backend.csproj ./
RUN dotnet restore backend.csproj

COPY . .
RUN dotnet publish backend.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false
# Scripts SQL manuels (Migrations/SQL_*.sql) : deploy.sh les extrait de l'image
# et applique ceux qui sont nouveaux ou modifiés avant de basculer.
RUN mkdir -p /app/sql && find Migrations -maxdepth 1 -name 'SQL_*.sql' -exec cp {} /app/sql/ \;

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Le code appartient à root : l'utilisateur « app » peut le lire, pas le modifier.
COPY --from=build /app/publish .
COPY --from=build /app/sql /app/sql

RUN mkdir -p /app/logs /app/wwwroot/uploads /app/private_uploads /home/app/.aspnet/DataProtection-Keys \
 && chown -R app:app /app/logs /app/wwwroot/uploads /app/private_uploads /home/app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

ARG GIT_SHA=inconnu
LABEL org.opencontainers.image.title="winplus-dotnet" \
      org.opencontainers.image.revision="${GIT_SHA}"

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "backend.dll"]
