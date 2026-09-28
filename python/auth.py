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
#
# Module 20 : la valeur par défaut « your-secret-key-must-match-dotnet » a été
# retirée. Elle est publique (elle vit dans ce dépôt) : si la variable n'était
# pas positionnée en production, n'importe qui pouvait forger un jeton
# role=admin accepté par tous les endpoints WinAI, sans que rien ne le signale.
# Le service refuse désormais de démarrer bruyamment plutôt que de démarrer
# avec un secret connu, sur le modèle du .NET qui lève déjà une erreur.
#
# Aucune valeur de secret n'est écrite ni lue ici : côté déploiement, la
# variable d'environnement JWT_SECRET_KEY doit être positionnée, avec
# exactement la même valeur que JWT:SecretKey côté .NET.
JWT_SECRET = os.getenv('JWT_SECRET_KEY')
if not JWT_SECRET:
    raise RuntimeError(
        "JWT_SECRET_KEY n'est pas positionnée. Le service WinAI refuse de "
        "démarrer sans secret de signature : sans elle, tout jeton forgé "
        "serait accepté. Positionnez la variable d'environnement "
        "JWT_SECRET_KEY avec la même valeur que JWT:SecretKey côté .NET."
    )

JWT_ALGORITHM = 'HS256'

# Issuer et audience émis par .NET (JwtService : WinPlusApp / WinPlusUsers).
# Ils doivent être déclarés explicitement : PyJWT 2.x lève
# InvalidAudienceError si le token porte un claim « aud » alors que
# l'appelant n'en fournit aucun. Comme les tokens .NET en portent un, la
# validation échouait avant même de vérifier la signature  deuxième cause des
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
#  permet aux tokens déjà distribués de rester valides  personne n'est
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
#  où le code sera relu, la tolérance aura disparu d'elle-même  et les
#  quelques lignes restantes pourront être supprimées sans précaution.
# ══════════════════════════════════════════════════════════════════════════

XMLDSIG_HMAC_SHA256 = 'http://www.w3.org/2001/04/xmldsig-more#hmac-sha256'

LEGACY_ALG_WINDOW_HOURS = int(os.getenv('JWT_LEGACY_ALG_WINDOW_HOURS', '48'))

# Fichier d'ancrage de l'échéance.
#
# Module 20 : l'emplacement était /tmp, présenté comme volontaire. Dans un
# conteneur, /tmp appartient au système de fichiers éphémère de l'instance :
# chaque redémarrage du conteneur et non seulement de la machine effaçait
# le fichier et RÉARMAIT la fenêtre pour 48 heures de plus. Une tolérance
# censée se fermer d'elle-même ne se fermait donc jamais, et un algorithme de
# signature non standard restait accepté indéfiniment.
#
# L'échéance vit maintenant à côté du code, dans un répertoire de données qui
# survit au redémarrage du conteneur (JWT_LEGACY_ALG_DEADLINE_FILE reste
# surchargeable pour pointer un volume persistant en déploiement).
_DEFAULT_DEADLINE_FILE = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    'data',
    'jwt_legacy_alg_deadline',
)

_DEADLINE_FILE = os.getenv('JWT_LEGACY_ALG_DEADLINE_FILE', _DEFAULT_DEADLINE_FILE)


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
        os.makedirs(os.path.dirname(_DEADLINE_FILE) or '.', exist_ok=True)
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
    def __init__(self, user_id: int, email: str, role: str,
                 is_service: bool = False, scopes: Optional[List[str]] = None):
        self.user_id = user_id
        self.email = email
        self.role = role
        # Jeton technique d'une tâche de fond .NET (voir require_user_or_service).
        self.is_service = is_service
        self.scopes = scopes or []


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
        # circule encore  ce qui signalerait un .NET non redéployé.
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
    # Module 20 : comparaison insensible à la casse. Les rôles sont normalisés
    # en minuscules côté .NET, mais ce projet a déjà produit des endpoints
    # morts en comparant un rôle en casse capitalisée (voir la correction de
    # [Authorize(Roles = "Admin")]). Un contrôle de rôle qui échoue sur la
    # casse est un refus silencieux, pas une faille : on l'évite ici plutôt
    # que de le découvrir en production.
    normalized = {role.lower() for role in allowed_roles}

    async def role_checker(user: UserTokenData = Depends(verify_token)) -> UserTokenData:
        if (user.role or '').lower() not in normalized:
            logger.warning(f"Access forbidden for user {user.user_id}. Required roles: {allowed_roles}")
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail=f"Access forbidden. Required roles: {', '.join(allowed_roles)}"
            )
        return user

    return role_checker


# ══════════════════════════════════════════════════════════════════════════════
#  Authentification service à service (Modules 23 et 36)
#
#  Les tâches de fond .NET (BackgroundService) n'ont aucun jeton utilisateur à
#  relayer : leurs appels partaient sans en-tête Authorization et recevaient
#  401, avalé par un repli silencieux. .NET signe désormais un jeton technique
#  (Services/ServiceTokenProvider.cs) :
#    - même secret que les jetons utilisateurs (JWT_SECRET_KEY, déjà requis) :
#      aucun nouveau secret à positionner en déploiement ;
#    - audience DISTINCTE (SERVICE_AUDIENCE) : verify_token, qui exige
#      JWT_AUDIENCE, le refuse, et le JwtBearer .NET aussi. Un jeton technique
#      ne peut donc appeler QUE les routes qui déclarent explicitement
#      require_user_or_service ;
#    - claim « scope » : chaque route n'accepte que le périmètre de la tâche
#      qui l'appelle (droits restreints, pas un jeton passe-partout) ;
#    - durée de vie courte (5 minutes), émis juste avant l'appel.
# ══════════════════════════════════════════════════════════════════════════════

SERVICE_AUDIENCE = 'WinPlusAIService'
SERVICE_TOKEN_USE = 'service'


def _peek_audience(token: str) -> Optional[str]:
    """Lit l'audience SANS vérifier la signature, uniquement pour aiguiller
    vers la bonne validation. La signature est toujours vérifiée ensuite."""
    try:
        aud = jwt.decode(token, options={"verify_signature": False}).get('aud')
    except jwt.InvalidTokenError:
        return None
    if isinstance(aud, list):
        return SERVICE_AUDIENCE if SERVICE_AUDIENCE in aud else (aud[0] if aud else None)
    return aud


def _verify_service_token(token: str, allowed_scopes: set) -> UserTokenData:
    try:
        payload = jwt.decode(
            token,
            JWT_SECRET,
            algorithms=[JWT_ALGORITHM],
            audience=SERVICE_AUDIENCE,
            issuer=JWT_ISSUER,
        )
    except jwt.ExpiredSignatureError:
        logger.error("Jeton technique expiré : la tâche de fond .NET doit en émettre un par appel.")
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED,
                            detail="Service token expired",
                            headers={"WWW-Authenticate": "Bearer"})
    except jwt.InvalidTokenError as e:
        logger.error(f"Jeton technique invalide : {e}")
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED,
                            detail="Service token is invalid",
                            headers={"WWW-Authenticate": "Bearer"})

    if payload.get('token_use') != SERVICE_TOKEN_USE:
        logger.error("Jeton à l'audience technique sans token_use=service : refusé.")
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED,
                            detail="Service token is invalid",
                            headers={"WWW-Authenticate": "Bearer"})

    scopes = [s for s in str(payload.get('scope', '')).split() if s]
    if not allowed_scopes.intersection(scopes):
        logger.warning(f"Jeton technique hors périmètre : scopes={scopes}, attendus={sorted(allowed_scopes)}")
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN,
                            detail="Service token scope not allowed for this endpoint")

    logger.info(f"✅ Appel service à service authentifié (scopes={scopes}, sub={payload.get('sub')})")
    return UserTokenData(user_id=0, email='', role='service', is_service=True, scopes=scopes)


def require_user_or_service(*service_scopes: str, roles: Optional[tuple] = None):
    """
    Dépendance pour une route appelée À LA FOIS par des utilisateurs et par une
    tâche de fond .NET.

    - Jeton utilisateur : validé comme verify_token, puis contrôle de rôle si
      `roles` est fourni (même règle, insensible à la casse, que require_role).
    - Jeton technique : accepté seulement si son scope figure dans
      `service_scopes`.

    Passer `roles=()` (tuple vide) interdit tout jeton utilisateur : la route
    n'est alors ouverte qu'à la tâche de fond.
    """
    allowed_scopes = set(service_scopes)
    normalized_roles = None if roles is None else {r.lower() for r in roles}

    async def checker(credentials: HTTPAuthorizationCredentials = Depends(security)) -> UserTokenData:
        token = credentials.credentials
        if _peek_audience(token) == SERVICE_AUDIENCE:
            return _verify_service_token(token, allowed_scopes)

        user = await verify_token(credentials)
        if normalized_roles is not None and (user.role or '').lower() not in normalized_roles:
            logger.warning(f"Access forbidden for user {user.user_id}. Required roles: {sorted(normalized_roles)}")
            raise HTTPException(status_code=status.HTTP_403_FORBIDDEN,
                                detail="Access forbidden for this role")
        return user

    return checker
