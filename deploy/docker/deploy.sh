#!/usr/bin/env bash
# Déploiement des conteneurs backend WinPlus sur le serveur.
#
# Installé dans ~/winplus-docker/ (avec compose.yml). Appelé par le CI via une
# clé SSH limitée à ssh-gate.sh, ou à la main :
#
#   deploy.sh deploy   <prod|integration> <sha-du-commit>
#   deploy.sh rollback <prod|integration>          revient à la version précédente
#   deploy.sh status   <prod|integration>
#   deploy.sh init-sql <prod|integration> <sha>    marque tous les scripts SQL de
#                                                  l'image comme déjà appliqués
#                                                  (une seule fois, à l'installation)
#
# Étapes d'un déploiement : connexion ECR (rôle IAM de l'instance, aucune clé
# stockée) -> téléchargement des images -> scripts SQL nouveaux ou modifiés ->
# redémarrage des conteneurs -> contrôle de santé -> en cas d'échec, retour
# automatique à la version précédente.

set -euo pipefail

BASE="${WINPLUS_DOCKER_HOME:-$HOME/winplus-docker}"
STATE="$BASE/state"
COMPOSE_FILE="$BASE/compose.yml"
AWS_REGION="${AWS_REGION:-eu-north-1}"
REGISTRY="${REGISTRY:-286879406623.dkr.ecr.${AWS_REGION}.amazonaws.com}"
DB_HOST="${DB_HOST:-172.31.2.201}"
DB_USER="${DB_USER:-cademi}"
PSQL_IMAGE="postgres:16-alpine"

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
die() { log "ERREUR : $*"; exit 1; }

configure_stack() {
  case "$1" in
    prod)
      export COMPOSE_PROJECT_NAME=winplus-prod
      export DOTNET_PORT=5001 FASTAPI_PORT=8000 FASTAPI_WORKERS=2 WINPLUS_ENV= WINAI_DATA_SUFFIX=
      export DOTNET_ENV_FILE="$HOME/winplus/dotnet/.env.dotnet"
      export PYTHON_ENV_FILE="$HOME/winplus/python/.env"
      export DOTNET_MEM="${DOTNET_MEM:-1g}" FASTAPI_MEM="${FASTAPI_MEM:-2500m}"
      DB_NAME=winplus_db ;;
    integration)
      export COMPOSE_PROJECT_NAME=winplus-integration
      export DOTNET_PORT=5002 FASTAPI_PORT=8001 FASTAPI_WORKERS=1 WINPLUS_ENV=integration WINAI_DATA_SUFFIX=-integration
      export DOTNET_ENV_FILE="$HOME/winplus-integration/dotnet/.env.dotnet"
      export PYTHON_ENV_FILE="$HOME/winplus-integration/python/.env"
      export DOTNET_MEM="${DOTNET_MEM:-700m}" FASTAPI_MEM="${FASTAPI_MEM:-1500m}"
      DB_NAME=winplus_integration ;;
    *) die "pile inconnue : '$1' (prod ou integration)" ;;
  esac
  STACK="$1"
  export REGISTRY
  [ -r "$DOTNET_ENV_FILE" ] || die "fichier d'environnement absent : $DOTNET_ENV_FILE"
  [ -r "$PYTHON_ENV_FILE" ] || die "fichier d'environnement absent : $PYTHON_ENV_FILE"
  # Identifiants S3 du .NET : le code crée ses clients AWS avec la chaîne par
  # défaut du SDK (AWS__AccessKeyId n'est lu nulle part). Sous systemd, il
  # trouvait les clés winplus-app dans ~/.aws ; en conteneur, ~/.aws n'existe
  # pas et le rôle de l'instance (ECR seulement) prenait le relais : S3 refusé.
  # On passe donc les mêmes clés au conteneur sous les noms standard du SDK.
  DOTNET_S3_KEY_ID=$(env_value "$DOTNET_ENV_FILE" AWS__AccessKeyId)
  DOTNET_S3_SECRET=$(env_value "$DOTNET_ENV_FILE" AWS__SecretAccessKey)
  [ -n "$DOTNET_S3_KEY_ID" ] && [ -n "$DOTNET_S3_SECRET" ] \
    || die "AWS__AccessKeyId / AWS__SecretAccessKey absents de $DOTNET_ENV_FILE (S3 serait refusé)"
  export DOTNET_S3_KEY_ID DOTNET_S3_SECRET
  mkdir -p "$STATE"
}

# Valeur d'une variable dans un fichier .env (sans guillemets ni retour chariot).
env_value() { grep -E "^$2=" "$1" | tail -1 | cut -d= -f2- | tr -d '"\r'; }

compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

# Connexion ECR avec le rôle de l'instance (winplus-backend-ec2-role) : les
# fichiers ~/.aws du serveur portent les clés de l'utilisateur winplus-app (S3,
# sauvegardes), qui n'a pas accès à ECR et passerait avant le rôle.
ecr_login() {
  env -u AWS_ACCESS_KEY_ID -u AWS_SECRET_ACCESS_KEY -u AWS_SESSION_TOKEN -u AWS_PROFILE \
      AWS_SHARED_CREDENTIALS_FILE=/dev/null AWS_CONFIG_FILE=/dev/null \
      aws ecr get-login-password --region "$AWS_REGION" \
    | docker login --username AWS --password-stdin "$REGISTRY" > /dev/null
}

current_tag()  { cat "$STATE/$STACK.current"  2>/dev/null || true; }
previous_tag() { cat "$STATE/$STACK.previous" 2>/dev/null || true; }

# Copie les scripts SQL de l'image .NET dans un dossier temporaire.
extract_sql() {
  local tag="$1" dest="$2" cid
  cid=$(docker create "$REGISTRY/winplus-dotnet:$tag")
  docker cp "$cid:/app/sql/." "$dest/" > /dev/null
  docker rm "$cid" > /dev/null
}

# Applique les scripts nouveaux ou modifiés depuis le dernier déploiement.
# Mémoire : $STATE/<pile>.sql-applied (empreinte sha256 + nom). Même règle que
# l'ancien workflow : chaque nouveau script doit être idempotent, les
# Validation_Check ne sont jamais exécutés.
apply_sql() {
  local tag="$1" tmp applied f sum
  tmp=$(mktemp -d); applied="$STATE/$STACK.sql-applied"
  touch "$applied"
  extract_sql "$tag" "$tmp"
  # shellcheck disable=SC1091
  source "$HOME/winplus/backup.env"   # DB_PASSWORD, comme l'ancien workflow
  for f in $(cd "$tmp" && ls SQL_*.sql 2>/dev/null | grep -v Validation_Check | sort); do
    sum=$(sha256sum "$tmp/$f" | cut -d' ' -f1)
    grep -qxF "$sum  $f" "$applied" && continue
    log "SQL : application de $f sur $DB_NAME"
    docker run --rm --network host -e PGPASSWORD="$DB_PASSWORD" -v "$tmp:/sql:ro" "$PSQL_IMAGE" \
      psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -1 -q -f "/sql/$f"
    echo "$sum  $f" >> "$applied"
  done
  rm -rf "$tmp"
}

wait_healthy() {
  local i
  # Le .NET met 10 à 20 s à démarrer, FastAPI plus longtemps s'il charge ses modèles.
  for i in $(seq 1 60); do
    if curl -fsS -m 5 "http://127.0.0.1:$DOTNET_PORT/health" > /dev/null \
       && curl -fsS -m 5 "http://127.0.0.1:$FASTAPI_PORT/health" > /dev/null; then
      log "pile $STACK en bonne santé (essai $i)"; return 0
    fi
    sleep 3
  done
  return 1
}

start_tag() {
  export TAG="$1"
  compose up -d --remove-orphans
}

cmd_deploy() {
  local tag="$1" old
  [[ "$tag" =~ ^[0-9a-f]{40}$ ]] || die "étiquette invalide : '$tag' (sha complet du commit attendu)"
  ecr_login
  export TAG="$tag"
  log "téléchargement des images $tag"
  compose pull --quiet
  apply_sql "$tag"
  old=$(current_tag)
  log "démarrage de $tag (précédente : ${old:-aucune})"
  start_tag "$tag"
  if wait_healthy; then
    [ -n "$old" ] && [ "$old" != "$tag" ] && echo "$old" > "$STATE/$STACK.previous"
    echo "$tag" > "$STATE/$STACK.current"
    echo "$(date -u +%FT%TZ) deploy $tag" >> "$STATE/$STACK.history"
    docker image prune -af --filter "until=168h" > /dev/null || true
    log "déploiement terminé"
  else
    log "contrôle de santé en échec : journaux récents"
    compose logs --tail 40 || true
    if [ -n "$old" ]; then
      log "retour automatique à $old"
      start_tag "$old"
      wait_healthy || log "ATTENTION : la version précédente ne répond pas non plus"
      echo "$(date -u +%FT%TZ) echec $tag, retour $old" >> "$STATE/$STACK.history"
    fi
    exit 1
  fi
}

cmd_rollback() {
  local prev cur
  prev=$(previous_tag); cur=$(current_tag)
  [ -n "$prev" ] || die "aucune version précédente connue pour $STACK"
  ecr_login
  log "retour arrière $STACK : $cur -> $prev"
  start_tag "$prev"
  wait_healthy || die "la version $prev ne répond pas"
  echo "$prev" > "$STATE/$STACK.current"
  [ -n "$cur" ] && echo "$cur" > "$STATE/$STACK.previous"
  echo "$(date -u +%FT%TZ) rollback $cur -> $prev" >> "$STATE/$STACK.history"
}

cmd_status() {
  echo "pile       : $STACK"
  echo "actuelle   : $(current_tag)"
  echo "précédente : $(previous_tag)"
  TAG="$(current_tag)"; export TAG="${TAG:-none}"
  compose ps
}

cmd_init_sql() {
  local tag="$1" tmp applied f
  [[ "$tag" =~ ^[0-9a-f]{40}$ ]] || die "étiquette invalide : '$tag'"
  applied="$STATE/$STACK.sql-applied"
  [ -s "$applied" ] && die "$applied existe déjà : rien à initialiser"
  ecr_login
  docker pull --quiet "$REGISTRY/winplus-dotnet:$tag" > /dev/null
  tmp=$(mktemp -d); extract_sql "$tag" "$tmp"
  for f in $(cd "$tmp" && ls SQL_*.sql | sort); do
    echo "$(sha256sum "$tmp/$f" | cut -d' ' -f1)  $f" >> "$applied"
  done
  rm -rf "$tmp"
  log "$(wc -l < "$applied") scripts marqués comme appliqués pour $STACK"
}

action="${1:-}"; stack="${2:-}"
[ -n "$action" ] && [ -n "$stack" ] || { sed -n '4,16p' "$0"; exit 2; }
configure_stack "$stack"
exec 9> "$STATE/$STACK.lock"
flock -n 9 || die "un déploiement de $STACK est déjà en cours"
case "$action" in
  deploy)   cmd_deploy "${3:-}" ;;
  rollback) cmd_rollback ;;
  status)   cmd_status ;;
  init-sql) cmd_init_sql "${3:-}" ;;
  *) die "action inconnue : $action" ;;
esac
