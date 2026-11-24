#!/usr/bin/env bash
# Porte d'entrée de la clé SSH du CI (winplus-docker-deploy).
#
# Dans ~/.ssh/authorized_keys du serveur, la clé est déclarée ainsi :
#   command="/home/ubuntu/winplus-docker/ssh-gate.sh",no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty ssh-ed25519 AAAA... winplus-docker-deploy
#
# Quelle que soit la commande envoyée, seule l'une de celles-ci est exécutée :
#   deploy <prod|integration> <sha de 40 caractères>
#   rollback <prod|integration>
#   status <prod|integration>
# Tout le reste est refusé : la clé ne donne ni shell, ni copie de fichiers,
# ni tunnel.

set -euo pipefail
read -r -a args <<< "${SSH_ORIGINAL_COMMAND:-}"

valid=false
case "${args[0]:-}" in
  deploy)
    [[ "${#args[@]}" -eq 3 && "${args[1]}" =~ ^(prod|integration)$ && "${args[2]}" =~ ^[0-9a-f]{40}$ ]] && valid=true ;;
  rollback|status)
    [[ "${#args[@]}" -eq 2 && "${args[1]}" =~ ^(prod|integration)$ ]] && valid=true ;;
esac

if [ "$valid" != true ]; then
  logger -t winplus-ssh-gate "commande refusée : ${SSH_ORIGINAL_COMMAND:-<vide>}"
  echo "commande refusée" >&2
  exit 1
fi

logger -t winplus-ssh-gate "commande acceptée : ${args[*]}"
exec "$HOME/winplus-docker/deploy.sh" "${args[@]}"
