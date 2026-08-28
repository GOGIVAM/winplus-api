#!/usr/bin/env python3
"""
Authentification JWT pour FastAPI
Partage le même secret que .NET backend
"""

import jwt
import os
from datetime import datetime, timedelta, timezone
from jwt.algorithms import HMACAlgorithm
from typing import Optional, List
from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPBearer, HTTPAuthorizationCredentials
import logging

logger = logging.getLogger(__name__)

# Configuration JWT (même que .NET)
# La valeur doit être identique à JWT:SecretKey côté .NET (appsettings.json).
JWT_SECRET = os.getenv('JWT_SECRET_KEY', 'your-secret-key-must-match-dotnet')
JWT_ALGORITHM = 'HS256'

# Issuer et audience émis par .NET (JwtService : WinPlusApp / WinPlusUsers).
# Ils doivent être déclarés explicitement : PyJWT 2.x lève
# InvalidAudienceError si le token porte un claim « aud » alors que
# l'appelant n'en fournit aucun. Comme les tokens .NET en portent un, la
# validation échouait avant même de vérifier la signature — deuxième cause des
# 401 sur tous les endpoints IA, après l'algorithme de signature.
JWT_ISSUER = os.getenv('JWT_ISSUER', 'WinPlusApp')
JWT_AUDIENCE = os.getenv('JWT_AUDIENCE', 'WinPlusUsers')


# ══════════════════════════════════════════════════════════════════════════
#  Compatibilité des tokens déjà émis  FENÊTRE AUTO-EXPIRANTE
#
#  .NET signait jusqu'ici avec SecurityAlgorithms.HmacSha256Signature, qui
#  écrit dans l'en-tête « alg » l'URI XML-dsig complet
#      http://www.w3.org/2001/04/xmldsig-more#hmac-sha256
#  au lieu de la forme compacte « HS256 » prévue par la RFC 7518. PyJWT
#  n'accepte que la forme compacte, d'où des 401 sur tous les endpoints IA.
#
#  Le calcul de signature est rigoureusement le même : seul le NOM change.
#  On enregistre donc l'URI comme alias de HMAC-SHA256 dans PyJWT, ce qui
#  permet aux tokens déjà distribués de rester valides — personne n'est
#  déconnecté par la correction côté .NET.
#
#  Réécrire l'en-tête à la volée ne marcherait pas : la signature couvre les
#  octets de l'en-tête encodé. Remplacer l'URI par « HS256 » avant décodage
#  invaliderait justement la signature qu'on cherche à vérifier.
#
#  ── Pourquoi une fenêtre, et non un nettoyage manuel ──
#
#  Une tolérance qu'il faut penser à retirer ne se retire jamais. La fenêtre
#  se ferme donc d'elle-même : passé LEGACY_ALG_WINDOW_HOURS après le premier
#  démarrage, seul HS256 est accepté. Les tokens vivant 24 h, une fenêtre de
#  48 h les couvre tous largement.
#
#  L'horloge démarre au premier lancement du processus et l'échéance est
#  conservée sur disque : redémarrer le service ne la repousse pas. Le jour
#  où le code sera relu, la tolérance aura disparu d'elle-même — et les
#  quelques lignes restantes pourront être supprimées sans précaution.
# ══════════════════════════════════════════════════════════════════════════

XMLDSIG_HMAC_SHA256 = 'http://www.w3.org/2001/04/xmldsig-more#hmac-sha256'

LEGACY_ALG_WINDOW_HOURS = int(os.getenv('JWT_LEGACY_ALG_WINDOW_HOURS', '48'))

# Fichier d'ancrage de l'échéance. /tmp est volontaire : si la machine
# redémarre, le fichier disparaît et la fenêtre se réarme — un redémarrage
# machine implique de toute façon une coupure, donc des tokens à renouveler.
_DEADLINE_FILE = os.getenv(
    'JWT_LEGACY_ALG_DEADLINE_FILE',
    '/tmp/winplus_jwt_legacy_alg_deadline',
)


def _resolve_legacy_deadline() -> datetime:
    """
    Échéance de fin de tolérance, stable d'un redémarrage à l'autre.

    Première exécution : maintenant + LEGACY_ALG_WINDOW_HOURS, écrit sur
    disque. Exécutions suivantes : relue depuis le fichier. Un simple
    « restart » ne prolonge donc pas la fenêtre.
    """
    if LEGACY_ALG_WINDOW_HOURS <= 0:
        # Tolérance explicitement désactivée : échéance dans le passé.
        return datetime.now(timezone.utc) - timedelta(seconds=1)

    try:
        with open(_DEADLINE_FILE, 'r', encoding='utf-8') as handle:
            return datetime.fromisoformat(handle.read().strip())
    except (OSError, ValueError):
        pass

    deadline = datetime.now(timezone.utc) + timedelta(hours=LEGACY_ALG_WINDOW_HOURS)
    try:
        with open(_DEADLINE_FILE, 'w', encoding='utf-8') as handle:
            handle.write(deadline.isoformat())
    except OSError as exc:
        # Sans persistance, la fenêtre se réarme à chaque redémarrage : moins
        # net, mais jamais bloquant pour les utilisateurs.
        logger.warning("Échéance de tolérance non persistée (%s)", exc)

    return deadline


LEGACY_ALG_DEADLINE = _resolve_legacy_deadline()

try:
    jwt.register_algorithm(XMLDSIG_HMAC_SHA256, HMACAlgorithm(HMACAlgorithm.SHA256))
except ValueError:
    # Déjà enregistré : le module a été rechargé, rien à faire.
    pass

if datetime.now(timezone.utc) < LEGACY_ALG_DEADLINE:
    logger.info(
        "Tolérance JWT ancien format active jusqu'au %s "
        "(les tokens .NET déjà émis restent valides). "
        "Après cette date, seul HS256 sera accepté.",
        LEGACY_ALG_DEADLINE.isoformat(timespec='seconds'),
    )
else:
    logger.info("Tolérance JWT ancien format expirée : seul HS256 est accepté.")


def accepted_algorithms() -> list:
    """
    Noms d'algorithmes acceptés à l'instant présent.

    HS256 toujours ; l'URI XML-dsig seulement tant que la fenêtre de
    compatibilité est ouverte. Évaluée à chaque appel, sans quoi un service
    resté en ligne plusieurs jours n'aurait jamais vu l'échéance passer.
    """
    if datetime.now(timezone.utc) < LEGACY_ALG_DEADLINE:
        return [JWT_ALGORITHM, XMLDSIG_HMAC_SHA256]
    return [JWT_ALGORITHM]

# Security scheme
security = HTTPBearer()


class UserTokenData:
    """Container pour les données utilisateur extraites du token"""
    def __init__(self, user_id: int, email: str, role: str):
        self.user_id = user_id
        self.email = email
        self.role = role


async def verify_token(credentials: HTTPAuthorizationCredentials = Depends(security)) -> UserTokenData:
    """
    Dépendance FastAPI pour vérifier le token JWT
    Utilisation: @app.get("/protected") def endpoint(user = Depends(verify_token))
    """
    token = credentials.credentials
    
    try:
        # Valider JWT avec même secret que .NET.
        # audience et issuer sont obligatoires ici : voir le commentaire sur
        # JWT_AUDIENCE plus haut.
        payload = jwt.decode(
            token,
            JWT_SECRET,
            algorithms=accepted_algorithms(),
            audience=JWT_AUDIENCE,
            issuer=JWT_ISSUER,
        )
        
        # Extraire informations utilisateur
        user_id = payload.get('user_id')
        email = payload.get('email')
        role = payload.get('role', 'student')
        
        # Vérifier que user_id est présent
        if not user_id:
            logger.warning("Token does not contain user_id")
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Token does not contain user_id",
                headers={"WWW-Authenticate": "Bearer"},
            )
        
        logger.info(f"✅ Authenticated request from user_id={user_id}, role={role}")
        
        return UserTokenData(
            user_id=user_id,
            email=email,
            role=role
        )
        
    except jwt.ExpiredSignatureError:
        logger.warning("Token expired")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Your session has expired, please login again",
            headers={"WWW-Authenticate": "Bearer"},
        )
    except jwt.InvalidAudienceError as e:
        logger.error(
            f"Audience invalide ({e}). Attendu : {JWT_AUDIENCE}. "
            "Vérifiez JWT:Audience côté .NET et JWT_AUDIENCE ici."
        )
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Authentication token is invalid",
            headers={"WWW-Authenticate": "Bearer"},
        )
    except jwt.InvalidIssuerError as e:
        logger.error(
            f"Issuer invalide ({e}). Attendu : {JWT_ISSUER}."
        )
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Authentication token is invalid",
            headers={"WWW-Authenticate": "Bearer"},
        )
    except jwt.InvalidAlgorithmError as e:
        # Deux cas : soit le token vient d'un émetteur inattendu, soit la
        # fenêtre de compatibilité s'est refermée alors qu'un ancien token
        # circule encore — ce qui signalerait un .NET non redéployé.
        window_open = datetime.now(timezone.utc) < LEGACY_ALG_DEADLINE
        logger.error(
            "Algorithme non accepté (%s). Acceptés : %s. "
            "Fenêtre de compatibilité : %s.%s",
            e,
            accepted_algorithms(),
            'ouverte' if window_open else 'fermée',
            '' if window_open else (
                " Si des tokens à l'ancien format circulent encore, "
                "vérifiez que .NET signe bien avec SecurityAlgorithms.HmacSha256."
            ),
        )
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Authentication token is invalid",
            headers={"WWW-Authenticate": "Bearer"},
        )
    except jwt.InvalidTokenError as e:
        logger.error(f"Invalid token: {e}")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Authentication token is invalid",
            headers={"WWW-Authenticate": "Bearer"},
        )
    except Exception as e:
        logger.error(f"Token validation error: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail="Authentication failed"
        )


def require_role(*allowed_roles: str):
    """
    Dépendance pour vérifier le rôle utilisateur
    Usage: 
        async def protected_endpoint(user: UserTokenData = Depends(require_role('admin', 'teacher'))):
            ...
    """
    async def role_checker(user: UserTokenData = Depends(verify_token)) -> UserTokenData:
        if user.role not in allowed_roles:
            logger.warning(f"Access forbidden for user {user.user_id}. Required roles: {allowed_roles}")
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail=f"Access forbidden. Required roles: {', '.join(allowed_roles)}"
            )
        return user
    
    return role_checker
