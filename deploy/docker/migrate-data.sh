#!/usr/bin/env bash
# Reprise unique des données locales des anciens services systemd dans les
# volumes Docker d'une pile, avant sa première mise en conteneurs.
#
#   migrate-data.sh <prod|integration>
#
# Copie (sans rien supprimer à la source) :
#   WinAI   statistiques d'usage, index BM25, graphe RAG, cache de modèles,
#           échéance des anciens jetons JWT       -> volume fastapi-data (uid 10001)
#   .NET    clés ASP.NET (cookies chiffrés)      -> volume dotnet-keys   (uid 1654)
#           wwwroot/uploads, private_uploads     -> volumes dotnet-uploads, dotnet-private
#
# Les volumes sont créés avec les étiquettes compose : « docker compose up »
# les reprend tels quels.

set -euo pipefail
STACK="${1:-}"
HELPER=alpine:3.20

case "$STACK" in
  integration)
    PROJECT=winplus-integration; SUFFIX=-integration
    PY_USAGE="$HOME/winplus-integration/winai-usage-integration"
    PY_BM25="$HOME/winplus-integration/rag/bm25-integration"
    PY_GRAPH="$HOME/winplus-integration/rag/graph-integration"
    PY_MODELS="$HOME/winplus-integration/models_cache"
    PY_DEADLINE="$HOME/winplus-integration/data/jwt_legacy_alg_deadline"
    NET_PUBLISH="$HOME/winplus-integration/dotnet/publish" ;;
  prod)
    PROJECT=winplus-prod; SUFFIX=
    PY_USAGE="$HOME/winplus-repo/python/data/winai-usage"
    PY_BM25="$HOME/winplus-repo/python/RAG/.bm25_storage"
    PY_GRAPH="$HOME/winplus-repo/python/RAG/.graph_storage"
    PY_MODELS="$HOME/winplus-repo/python/models_cache"
    PY_DEADLINE="$HOME/winplus-repo/python/data/jwt_legacy_alg_deadline"
    NET_PUBLISH="$HOME/winplus/dotnet/publish" ;;
  *) echo "usage : $0 <prod|integration>" >&2; exit 2 ;;
esac
NET_KEYS="$HOME/.aspnet/DataProtection-Keys"

volume() {
  local name="${PROJECT}_$1"
  docker volume inspect "$name" > /dev/null 2>&1 || docker volume create \
    --label "com.docker.compose.project=$PROJECT" \
    --label "com.docker.compose.volume=$1" "$name" > /dev/null
  echo "$name"
}

# copy <source> <volume> <chemin dans le volume> <uid>
copy() {
  local src="$1" vol="$2" dst="$3" uid="$4"
  if [ ! -e "$src" ]; then echo "  (absent, ignoré) $src"; return; fi
  if [ -d "$src" ]; then
    docker run --rm -v "$src:/src:ro" -v "$vol:/dst" "$HELPER" \
      sh -c "mkdir -p '/dst/$dst' && cp -a /src/. '/dst/$dst/' && chown -R $uid:$uid '/dst/$dst'"
  else
    docker run --rm -v "$(dirname "$src"):/src:ro" -v "$vol:/dst" "$HELPER" \
      sh -c "mkdir -p \"\$(dirname '/dst/$dst')\" && cp -a '/src/$(basename "$src")' '/dst/$dst' && chown $uid:$uid '/dst/$dst'"
  fi
  echo "  copié $src -> $vol:/$dst"
}

docker pull --quiet "$HELPER" > /dev/null
echo "Pile $STACK (projet $PROJECT)"
DATA=$(volume fastapi-data)
docker run --rm -v "$DATA:/dst" "$HELPER" chown 10001:10001 /dst
copy "$PY_USAGE"    "$DATA" "winai-usage$SUFFIX"   10001
copy "$PY_BM25"     "$DATA" "bm25_storage$SUFFIX"  10001
copy "$PY_GRAPH"    "$DATA" "graph_storage$SUFFIX" 10001
copy "$PY_MODELS"   "$DATA" "models_cache"         10001
copy "$PY_DEADLINE" "$DATA" "jwt_legacy_alg_deadline" 10001

KEYS=$(volume dotnet-keys);       copy "$NET_KEYS" "$KEYS" "." 1654
UPL=$(volume dotnet-uploads);     copy "$NET_PUBLISH/wwwroot/uploads" "$UPL" "." 1654
PRIV=$(volume dotnet-private);    copy "$NET_PUBLISH/private_uploads" "$PRIV" "." 1654
LOGS=$(volume dotnet-logs);       docker run --rm -v "$LOGS:/dst" "$HELPER" chown 1654:1654 /dst
echo "Reprise terminée."
