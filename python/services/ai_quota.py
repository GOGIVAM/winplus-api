"""
Contrôle de quota WinAI côté Python — Partie 8.8 du suivi (défense en
profondeur), révisé en 8.10 (limites de session et hebdomadaire).

Le mur principal est côté .NET (Services/AiQuotaService.cs), qui réserve puis
décompte chaque message au coût réel dans "AiTokenUsages". Ce module est en
LECTURE SEULE (8.9 : un seul point de décompte, .NET) et applique sur
l'endpoint FastAPI /api/chatbot/stream :

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

⚠ Grille, ratios et durées DUPLIQUÉS de backend/dotnet/Services/AiUsagePolicy.cs
(blocs BEGIN/END identiques des deux côtés). Toute modification doit être faite
aux deux endroits ; le script de comparaison décrit dans le rapport de la passe
8.10 vérifie qu'ils coïncident. La source de vérité de la référence d'un plan
reste la colonne "PricingPlans"."MaxChatMessages", lue ici en premier.
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

# BEGIN POLICY_CONSTANTS
FREE_TIER_REFERENCE_TOKENS = 200_000
DEFAULT_PAID_REFERENCE_TOKENS = 800_000
WEEK_DIVISOR = 4
SESSION_DIVISOR_OF_WEEK = 3
SESSION_HOURS = 5
WEEK_DAYS = 7
REPLAY_WINDOW_MINUTES = 15
PROVISIONAL_TOKENS_PER_MESSAGE = 1_000
PARENT_BONUS_FALLBACK_DIVISOR = 4
INSTITUTION_BONUS_DIVISOR = 5
PARENT_BONUS_FULL_SHARES = 3
INSTITUTION_BONUS_FULL_SHARES = 20
CHARS_PER_TOKEN_ESTIMATE = 4
# END POLICY_CONSTANTS

# BEGIN REFERENCE_GRID
REFERENCE_GRID: Tuple[Tuple[str, int], ...] = (
    ("annuel", 3_000_000),
    ("premium", 2_000_000),
    ("standard", 800_000),
    ("starter", 200_000),
    ("expert", 5_000_000),
    ("pro", 2_400_000),
    ("basique", 200_000),
    ("famille+", 2_000_000),
    ("famille +", 2_000_000),
    ("famille", 1_000_000),
    ("vip", 4_000_000),
)
# END REFERENCE_GRID

# BEGIN PARENT_BONUS_GRID
PARENT_BONUS_GRID: Tuple[Tuple[str, int], ...] = (
    ("famille+", 500_000),
    ("famille +", 500_000),
    ("famille", 250_000),
    ("vip", 1_000_000),
)
# END PARENT_BONUS_GRID

SESSION_LENGTH = timedelta(hours=SESSION_HOURS)
WEEK_LENGTH = timedelta(days=WEEK_DAYS)
REPLAY_WINDOW = timedelta(minutes=REPLAY_WINDOW_MINUTES)


def _is_free(price) -> Optional[bool]:
    return None if price is None else float(price) <= 0


def reference_tokens_for(column_value, price, name) -> int:
    """Quota de référence d'un plan (miroir de ReferenceTokensFor)."""
    if isinstance(column_value, int) and column_value > 0:
        return column_value
    if _is_free(price) is True:
        return FREE_TIER_REFERENCE_TOKENS
    n = (name or "").strip().lower()
    if not n:
        return FREE_TIER_REFERENCE_TOKENS
    for keyword, tokens in REFERENCE_GRID:
        if keyword in n:
            return tokens
    return DEFAULT_PAID_REFERENCE_TOKENS


def parent_bonus_for(column_value, price, name) -> int:
    if _is_free(price) is True:
        return 0
    n = (name or "").strip().lower()
    for keyword, tokens in PARENT_BONUS_GRID:
        if keyword in n:
            return tokens
    return reference_tokens_for(column_value, price, name) // PARENT_BONUS_FALLBACK_DIVISOR


def institution_bonus_for(column_value, price, name) -> int:
    if _is_free(price) is True:
        return 0
    return max(FREE_TIER_REFERENCE_TOKENS,
               reference_tokens_for(column_value, price, name) // INSTITUTION_BONUS_DIVISOR)


def shared_bonus(per_beneficiary: int, full_shares: int, beneficiaries: int) -> int:
    """Plafond global des bonus (8.10, hypothèse à valider) : enveloppe de
    `full_shares` parts complètes répartie entre tous les bénéficiaires."""
    if per_beneficiary <= 0:
        return 0
    if beneficiaries <= full_shares:
        return per_beneficiary
    return per_beneficiary * full_shares // beneficiaries


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


# Abonnement payant en cours — même règle que AiQuotaService (.NET).
_ACTIVE_PAID_SUB_SQL = text('''
    SELECT p."MaxChatMessages", p."Price", COALESCE(s."PlanName", p."Name")
    FROM "Subscriptions" s
    JOIN "PricingPlans" p ON p."Id" = s."PricingPlanId"
    WHERE s."UserId" = :uid AND s."IsDeleted" = FALSE AND s."IsActive" = TRUE
      AND LOWER(s."Status") = 'active' AND p."Price" > 0
    ORDER BY s."StartDate" DESC
    LIMIT 1
''')


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


def _reference_tokens(session, user_id: int) -> int:
    """Référence totale : plan personnel + bonus parent + bonus établissement,
    bonus plafonnés (miroir de AiQuotaService.ResolveReferenceAsync)."""
    own = session.execute(_ACTIVE_PAID_SUB_SQL, {"uid": user_id}).first()
    total = reference_tokens_for(*own) if own else FREE_TIER_REFERENCE_TOKENS

    parent_ids = [r[0] for r in session.execute(
        text('SELECT DISTINCT "ParentId" FROM "ParentStudentLinks" WHERE "StudentId" = :uid AND "Status" = \'accepted\''),
        {"uid": user_id},
    )]
    for pid in parent_ids:
        row = session.execute(_ACTIVE_PAID_SUB_SQL, {"uid": pid}).first()
        if not row:
            continue
        children = session.execute(
            text('SELECT COUNT(DISTINCT "StudentId") FROM "ParentStudentLinks" WHERE "ParentId" = :pid AND "Status" = \'accepted\''),
            {"pid": pid},
        ).scalar() or 0
        total += shared_bonus(parent_bonus_for(*row), PARENT_BONUS_FULL_SHARES, int(children))

    inst_rows = session.execute(text('''
        SELECT DISTINCT u."InstitutionId", u."Id"
        FROM "InstitutionStudents" s
        JOIN "Users" u ON u."InstitutionId" = s."InstitutionId"
        WHERE s."StudentId" = :uid AND s."IsActive" = TRUE
          AND u."Role" = 'institution' AND u."IsDeleted" = FALSE
    '''), {"uid": user_id}).all()
    best: dict[int, int] = {}
    for inst_id, inst_user_id in inst_rows:
        row = session.execute(_ACTIVE_PAID_SUB_SQL, {"uid": inst_user_id}).first()
        if row:
            best[inst_id] = max(best.get(inst_id, 0), institution_bonus_for(*row))
    for inst_id, per_student in best.items():
        students = session.execute(
            text('SELECT COUNT(DISTINCT "StudentId") FROM "InstitutionStudents" WHERE "InstitutionId" = :iid AND "IsActive" = TRUE'),
            {"iid": inst_id},
        ).scalar() or 0
        total += shared_bonus(per_student, INSTITUTION_BONUS_FULL_SHARES, int(students))
    return total


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
