"""
Contrôle de quota WinAI côté Python Partie 8.8 du suivi (défense en
profondeur), révisé en 8.10 (limites de session et hebdomadaire).

Le mur principal est côté .NET (Services/AiQuotaService.cs), qui réserve puis
décompte chaque message au coût réel dans "AiTokenUsages". Ce module est en
LECTURE SEULE (8.9 : un seul point de décompte, .NET) et applique sur
les endpoints FastAPI /api/chatbot/stream, /chat et /complete (décision 10.4,
voir routes/chatbot_routes.py::_enforce_ai_quota) :

1. Réserve exigée (point A de la passe 8.10). L'unique appelant légitime de
   /stream est le proxy .NET, qui pose (ou rattache) une réserve pour le
   message et transmet son identifiant. Le message est autorisé seulement si
   cette ligne existe, n'est PAS finalisée et a moins de REPLAY_WINDOW_MINUTES.
   Un identifiant déjà finalisé, trop ancien, inconnu ou absent n'ouvre plus
   rien : sans cela, un appel direct au port Python (ou un identifiant rejoué)
   donnait WinAI gratuit et illimité, puisque Python ne décompte pas.

2. Limites session / semaine (8.10) : calculées ici avec EXACTEMENT les mêmes
   règles que AiUsagePolicy.cs, pour renvoyer un 402 explicite (quelle limite,
   quand elle se réinitialise, jamais de nombre de tokens) quand c'est la
   raison du refus.

⚠ Ratios et durées DUPLIQUÉS de backend/dotnet/Services/AiUsagePolicy.cs
(blocs BEGIN/END identiques des deux côtés). Lot 9.7b : il n'y a plus de grille
de plans à recopier. La référence d'un compte vient de ses droits typés
(EntitlementService, .NET), inscrite par .NET sur chaque réserve
("AiTokenUsages"."ReferenceTokens") et relue ici.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from typing import Iterable, Optional, Tuple

from sqlalchemy import text

logger = logging.getLogger(__name__)

# ═══════════════════════════════ POLICY ════════════════════════════════════
# Copie conforme de AiUsagePolicy.cs. Ne modifier qu'en même temps que lui.
# Lot 9.7b : la grille de référence par NOM de plan et la grille des bonus
# parent / établissement sont SUPPRIMÉES (ici et côté .NET). La référence d'un
# compte vient de ses droits typés, résolus par EntitlementService (.NET), qui
# l'inscrit sur chaque réserve ("AiTokenUsages"."ReferenceTokens").

# BEGIN POLICY_CONSTANTS
WEEK_DIVISOR = 4
SESSION_DIVISOR_OF_WEEK = 3
SESSION_HOURS = 5
WEEK_DAYS = 7
REPLAY_WINDOW_MINUTES = 15
PROVISIONAL_TOKENS_PER_MESSAGE = 1_000
CHARS_PER_TOKEN_ESTIMATE = 4
# END POLICY_CONSTANTS

# Repli seulement quand aucune réserve n'a encore été posée par .NET pour ce
# compte (aucune référence connue) : valeur par défaut du droit
# ai.reference_tokens du plan gratuit (EntitlementCatalog.cs).
FREE_REFERENCE_FALLBACK = 200_000

SESSION_LENGTH = timedelta(hours=SESSION_HOURS)
WEEK_LENGTH = timedelta(days=WEEK_DAYS)
REPLAY_WINDOW = timedelta(minutes=REPLAY_WINDOW_MINUTES)



def weekly_limit(reference: int) -> int:
    return max(1, reference // WEEK_DIVISOR)


def session_limit(reference: int) -> int:
    return max(1, weekly_limit(reference) // SESSION_DIVISOR_OF_WEEK)


@dataclass
class WindowState:
    limit: int
    used: int
    anchor_at: Optional[datetime]
    is_reached: bool
    resets_at: Optional[datetime]


def evaluate_window(records: Iterable[Tuple[datetime, int]], now: datetime,
                    length: timedelta, limit: int) -> WindowState:
    """Fenêtre glissante ]now - length, now], ancrée au premier enregistrement
    qu'elle contient (miroir exact de AiUsagePolicy.Evaluate)."""
    start = now - length
    in_window = sorted((r for r in records if r[0] > start), key=lambda r: r[0])
    used = sum(max(0, int(t)) for _, t in in_window)
    anchor = in_window[0][0] if in_window else None
    reached = used >= limit
    resets_at = None
    if reached:
        remaining = used
        for at, tokens in in_window:
            remaining -= max(0, int(tokens))
            if remaining < limit:
                resets_at = at + length
                break
    return WindowState(limit, used, anchor, reached, resets_at)


def binding_limit(session: WindowState, week: WindowState) -> Tuple[Optional[str], Optional[datetime]]:
    """Limite qui bloque (miroir de AiUsagePolicy.Binding)."""
    if week.is_reached and session.is_reached:
        if session.resets_at and week.resets_at and session.resets_at > week.resets_at:
            return "session", session.resets_at
        return "week", week.resets_at
    if week.is_reached:
        return "week", week.resets_at
    if session.is_reached:
        return "session", session.resets_at
    return None, None


def classify_existing(exists: bool, is_finalized: bool, created_at: Optional[datetime], now: datetime) -> str:
    """'new' | 'reattach' | 'consumed' (miroir de AiUsagePolicy.ClassifyExisting)."""
    if not exists:
        return "new"
    if is_finalized:
        return "consumed"
    if created_at is None or created_at <= now - REPLAY_WINDOW:
        return "consumed"
    return "reattach"


_FR_DAYS = ("lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche")
_FR_MONTHS = ("janvier", "février", "mars", "avril", "mai", "juin", "juillet",
              "août", "septembre", "octobre", "novembre", "décembre")


def format_reset_time(resets_at_utc: datetime) -> str:
    """Heure lisible, fuseau fixe du Cameroun (UTC+1), comme côté .NET."""
    local = resets_at_utc.astimezone(timezone.utc) + timedelta(hours=1)
    return (f"le {_FR_DAYS[local.weekday()]} {local.day} {_FR_MONTHS[local.month - 1]} "
            f"à {local.hour:02d}h{local.minute:02d}")


def limit_message(kind: str, resets_at: Optional[datetime]) -> str:
    """Libellé du refus, identique à AiUsagePolicy.LimitMessage."""
    when = format_reset_time(resets_at) if resets_at else None
    if kind == "session":
        if when is None:
            return ("Vous avez atteint votre limite de session WinAI (5 heures glissantes). "
                    "Passez à un plan supérieur pour continuer.")
        return (f"Vous avez atteint votre limite de session WinAI. Vous pourrez de nouveau écrire {when}. "
                "Un plan supérieur augmente cette limite.")
    if when is None:
        return ("Vous avez atteint votre limite hebdomadaire WinAI (7 jours glissants). "
                "Passez à un plan supérieur pour continuer.")
    return (f"Vous avez atteint votre limite hebdomadaire WinAI. Vous pourrez de nouveau écrire {when}. "
            "Un plan supérieur augmente cette limite.")

# ═══════════════════════════ fin POLICY ════════════════════════════════════


def _reference_tokens(session, user_id: int) -> int:
    """Référence totale du compte (droits + bonus parent et licence), telle
    que .NET l'a résolue (EntitlementService) et inscrite sur sa dernière
    réserve. Lot 9.7b : plus aucune lecture de plan par nom ni par prix ici."""
    row = session.execute(text('''
        SELECT "ReferenceTokens" FROM "AiTokenUsages"
        WHERE "UserId" = :uid AND "ReferenceTokens" IS NOT NULL
        ORDER BY "CreatedAt" DESC LIMIT 1
    '''), {"uid": user_id}).first()
    if row and row[0] and int(row[0]) > 0:
        return int(row[0])
    return FREE_REFERENCE_FALLBACK


@dataclass
class QuotaCheck:
    allowed: bool
    # "reserved" (réserve .NET valide), "limit_reached", "reservation_required"
    reason: str = ""
    limit: Optional[str] = None
    resets_at: Optional[datetime] = None
    message: str = ""


def _aware(dt: Optional[datetime]) -> Optional[datetime]:
    if dt is None:
        return None
    return dt if dt.tzinfo else dt.replace(tzinfo=timezone.utc)


def _window_states(session, user_id: int, now: datetime) -> Tuple[WindowState, WindowState]:
    reference = _reference_tokens(session, user_id)
    rows = session.execute(
        text('SELECT "CreatedAt", "TokensCharged" FROM "AiTokenUsages" WHERE "UserId" = :uid AND "CreatedAt" > :start'),
        {"uid": user_id, "start": now - WEEK_LENGTH},
    ).all()
    records = [(_aware(r[0]), int(r[1] or 0)) for r in rows]
    return (evaluate_window(records, now, SESSION_LENGTH, session_limit(reference)),
            evaluate_window(records, now, WEEK_LENGTH, weekly_limit(reference)))


def check_quota(session, user_id: int, client_message_id: Optional[str] = None) -> QuotaCheck:
    """Autorise un message WinAI sur /stream seulement s'il porte une réserve
    .NET valide (ligne non finalisée de moins de REPLAY_WINDOW_MINUTES).
    Sinon, refuse : 402 explicite si une limite est atteinte, sinon
    « reservation_required ». Peut lever si la base est inaccessible ou la
    table absente : l'appelant refuse alors (fail-closed, 503)."""
    now = datetime.now(timezone.utc)

    if client_message_id:
        row = session.execute(
            text('SELECT "IsFinalized", "CreatedAt" FROM "AiTokenUsages" WHERE "UserId" = :uid AND "ClientMessageId" = :mid LIMIT 1'),
            {"uid": user_id, "mid": client_message_id.strip()[:64]},
        ).first()
        verdict = classify_existing(row is not None, bool(row[0]) if row else False,
                                    _aware(row[1]) if row else None, now)
        if verdict == "reattach":
            return QuotaCheck(True, "reserved")

    sess, week = _window_states(session, user_id, now)
    kind, resets_at = binding_limit(sess, week)
    if kind:
        return QuotaCheck(False, "limit_reached", kind, resets_at, limit_message(kind, resets_at))
    return QuotaCheck(False, "reservation_required",
                      message="Ce message doit passer par l'API WinPlus.")
