"""
WinAI  Assistance de l'agent WinPlus pendant l'entretien de vérification
d'un répétiteur (lot 9.5, suivi 17.16).

- POST /admin/tutor-interview/guide   → guide d'entretien (questions de fond,
  de pédagogie, mises en situation, sécurité avec des mineurs) et grille
  d'évaluation, selon les matières, niveaux et spécialités du tuteur ;
- POST /admin/tutor-interview/report  → brouillon de compte rendu rédigé à
  partir des notes et de la grille saisies par l'agent.

Accès : agent WinPlus (rôle admin) via son propre jeton, relayé par .NET
(AdminTutorVerificationController), ou jeton technique de périmètre
« ai.tutor-interview ». Le contexte (matières, niveaux) est lu en base par
.NET, jamais repris du navigateur.

WinAI ASSISTE seulement : la décision (réussi / échoué) et la validation de
la pièce d'identité restent celles de l'agent et d'un admin. Sans réponse
exploitable du modèle, un guide et un compte rendu de secours, construits à
partir de modèles fixes, sont renvoyés (based_on = "template") pour que
l'agent ne soit jamais bloqué.
"""

import logging
from typing import Any, Dict, List, Optional

from fastapi import APIRouter, Depends
from pydantic import BaseModel

from auth import require_user_or_service, UserTokenData
from routes.teacher_extra_routes import _deepseek_json
from services.language import in_language, language_directive, is_english

logger = logging.getLogger(__name__)

tutor_interview_router = APIRouter()

SERVICE_SCOPE = "ai.tutor-interview"
_admin_only = require_user_or_service(SERVICE_SCOPE, roles=("admin",))

MAX_LIST = 12
MAX_TEXT = 6000


# ─────────────────────────────────────────────────────────────────────────────
# Modèles
# ─────────────────────────────────────────────────────────────────────────────

class GuideRequest(BaseModel):
    subjects: List[str] = []
    levels: List[str] = []
    specialties: List[str] = []
    teaching_style: Optional[str] = None
    bio: Optional[str] = None
    format: str = "video"  # video | in_person


class GuideQuestion(BaseModel):
    question: str
    purpose: str = ""
    good_signs: str = ""


class GuideSection(BaseModel):
    key: str  # fond | pedagogie | situations | securite
    title: str
    questions: List[GuideQuestion]


class GridCriterion(BaseModel):
    key: str
    criterion: str
    description: str
    weight: int


class GuideResponse(BaseModel):
    intro: str
    sections: List[GuideSection]
    identity_checklist: List[str]
    grid: List[GridCriterion]
    pass_rule: str
    based_on: str  # "winai" | "template"


class ReportRequest(BaseModel):
    subjects: List[str] = []
    levels: List[str] = []
    format: str = "video"
    notes: str
    scores: Optional[Dict[str, Any]] = None
    grid: Optional[List[Dict[str, Any]]] = None
    identity_attached: bool = False
    identity_approved: bool = False


class ReportResponse(BaseModel):
    report: str
    recommendation: str  # pass | fail | unsure
    strengths: List[str]
    concerns: List[str]
    based_on: str  # "winai" | "template"


# ─────────────────────────────────────────────────────────────────────────────
# Grille et guide de secours (toujours disponibles)
# ─────────────────────────────────────────────────────────────────────────────

DEFAULT_GRID: List[GridCriterion] = [
    GridCriterion(key="mastery", criterion="Maîtrise des matières",
                  description="Exactitude et profondeur des réponses sur les notions des niveaux visés.", weight=3),
    GridCriterion(key="pedagogy", criterion="Pédagogie",
                  description="Capacité à expliquer simplement, à vérifier la compréhension et à adapter son approche.", weight=3),
    GridCriterion(key="communication", criterion="Communication",
                  description="Clarté, écoute, ponctualité et attitude pendant l'entretien.", weight=2),
    GridCriterion(key="safety", criterion="Sécurité avec des mineurs",
                  description="Connaissance des règles : cadre des séances, présence d'un adulte, refus des contacts hors WinPlus, signalement.", weight=3),
    GridCriterion(key="reliability", criterion="Fiabilité et cohérence du profil",
                  description="Cohérence entre le profil déclaré, l'expérience racontée et les réponses.", weight=2),
]

PASS_RULE = ("Réussite conseillée si aucune note de « Sécurité avec des mineurs » n'est sous 4/5, aucune autre note "
             "sous 3/5, et une moyenne pondérée d'au moins 3,5/5. La pièce d'identité et le selfie doivent en plus être "
             "validés par un admin : sans eux, l'entretien ne vérifie pas le répétiteur.")

IDENTITY_CHECKLIST = [
    "Demander au répétiteur de montrer sa pièce d'identité à la caméra (ou en main propre), recto puis verso.",
    "Vérifier que le nom de la pièce correspond au nom du compte WinPlus.",
    "Comparer la photo de la pièce au visage de la personne présente.",
    "Prendre (ou faire prendre) un selfie de la personne pendant l'entretien et le joindre au dossier avec la pièce.",
    "Ne jamais conserver de copie hors de WinPlus : tout passe par le dossier de vérification.",
]


def _clean_list(values: List[str]) -> List[str]:
    out: List[str] = []
    for v in values or []:
        v = (v or "").strip()
        if v and v not in out:
            out.append(v[:100])
    return out[:MAX_LIST]


def _template_guide(body: GuideRequest) -> GuideResponse:
    subjects = _clean_list(body.subjects) or ["la matière principale"]
    levels = _clean_list(body.levels) or ["les niveaux déclarés"]
    main_subject, main_level = subjects[0], levels[0]
    fond = [
        GuideQuestion(
            question=f"Expliquez une notion clé de {s} attendue en {main_level}, comme vous le feriez à un élève qui la découvre.",
            purpose="Vérifier la maîtrise de la matière au niveau visé.",
            good_signs="Explication exacte, progressive, avec un exemple concret.")
        for s in subjects[:3]
    ]
    fond.append(GuideQuestion(
        question=f"Quelle est l'erreur la plus fréquente des élèves de {main_level} en {main_subject}, et comment la corrigez-vous ?",
        purpose="Évaluer l'expérience réelle du terrain.",
        good_signs="Erreur précise, remédiation concrète et vérification de la compréhension."))
    pedagogie = [
        GuideQuestion(question="Comment se déroule votre première séance avec un nouvel élève ?",
                      purpose="Comprendre la méthode et le diagnostic initial.",
                      good_signs="Diagnostic des besoins, objectifs fixés avec l'élève et la famille."),
        GuideQuestion(question="Comment savez-vous qu'un élève a vraiment compris, et pas seulement acquiescé ?",
                      purpose="Évaluer la vérification des acquis.",
                      good_signs="Exercices de reformulation, questions ouvertes, exercice d'application."),
        GuideQuestion(question="Comment rendez-vous compte de la progression à l'élève et à ses parents ?",
                      purpose="Évaluer le suivi et la communication avec la famille.",
                      good_signs="Compte rendu régulier, objectifs mesurables, transparence."),
    ]
    situations = [
        GuideQuestion(question="Un élève se décourage après une mauvaise note et ne veut plus travailler. Que faites-vous pendant la séance ?",
                      purpose="Mise en situation : posture et motivation.",
                      good_signs="Écoute, petits objectifs atteignables, valorisation des progrès."),
        GuideQuestion(question=f"Un parent vous demande de faire les devoirs de {main_subject} à la place de son enfant. Comment répondez-vous ?",
                      purpose="Mise en situation : éthique et cadre.",
                      good_signs="Refus poli et argumenté, proposition d'un accompagnement à la place."),
        GuideQuestion(question="La séance doit avoir lieu en présentiel mais vous arrivez et aucun adulte n'est présent au domicile. Que faites-vous ?",
                      purpose="Mise en situation : sécurité du présentiel.",
                      good_signs="Ne pas entrer seul avec un mineur, prévenir la famille, reporter ou passer en ligne."),
    ]
    securite = [
        GuideQuestion(question="Quelles règles appliquez-vous pour une séance avec un mineur (lieu, porte, présence d'un adulte, horaires) ?",
                      purpose="Sécurité avec des mineurs.",
                      good_signs="Lieu ouvert ou adulte présent, horaires annoncés, aucune séance non prévue."),
        GuideQuestion(question="Un élève vous propose de continuer à échanger sur un réseau social privé. Comment réagissez-vous ?",
                      purpose="Sécurité : contacts hors cadre.",
                      good_signs="Refus, échanges uniquement via WinPlus ou avec les parents en copie."),
        GuideQuestion(question="Si un élève vous confie qu'il est en danger ou maltraité, que faites-vous ?",
                      purpose="Sécurité : devoir de signalement.",
                      good_signs="Écoute sans juger, ne pas promettre le secret, alerter WinPlus et les autorités compétentes."),
        GuideQuestion(question="Acceptez-vous un paiement en espèces ou hors de WinPlus si la famille le propose ?",
                      purpose="Respect du cadre de la plateforme.",
                      good_signs="Refus : paiement uniquement via WinPlus."),
    ]
    where = "en visio" if body.format == "video" else "en rencontre"
    return GuideResponse(
        intro=(f"Entretien {where} avec un répétiteur en {', '.join(subjects)} ({', '.join(levels)}). "
               "Commencez par la vérification d'identité, puis les questions de fond, de pédagogie, les mises en "
               "situation et la sécurité avec des mineurs. Notez chaque critère de la grille de 1 à 5."),
        sections=[
            GuideSection(key="fond", title="Questions de fond", questions=fond),
            GuideSection(key="pedagogie", title="Pédagogie", questions=pedagogie),
            GuideSection(key="situations", title="Mises en situation", questions=situations),
            GuideSection(key="securite", title="Sécurité avec des mineurs", questions=securite),
        ],
        identity_checklist=IDENTITY_CHECKLIST,
        grid=DEFAULT_GRID,
        pass_rule=PASS_RULE,
        based_on="template",
    )


def _parse_questions(raw: Any) -> List[GuideQuestion]:
    out: List[GuideQuestion] = []
    if not isinstance(raw, list):
        return out
    for q in raw[:8]:
        if isinstance(q, dict) and str(q.get("question", "")).strip():
            out.append(GuideQuestion(
                question=str(q.get("question", "")).strip()[:500],
                purpose=str(q.get("purpose", "")).strip()[:300],
                good_signs=str(q.get("good_signs", "")).strip()[:300],
            ))
        elif isinstance(q, str) and q.strip():
            out.append(GuideQuestion(question=q.strip()[:500]))
    return out


# ─────────────────────────────────────────────────────────────────────────────
# Endpoints
# ─────────────────────────────────────────────────────────────────────────────

@tutor_interview_router.post("/admin/tutor-interview/guide", response_model=GuideResponse)
async def tutor_interview_guide(body: GuideRequest, current_user: UserTokenData = Depends(_admin_only)):
    fallback = _template_guide(body)
    subjects = _clean_list(body.subjects)
    levels = _clean_list(body.levels)
    specialties = _clean_list(body.specialties)
    prompt = (
        "Prépare un guide d'entretien de vérification pour un répétiteur (cours particuliers) au Cameroun.\n"
        f"Matières : {', '.join(subjects) or 'non précisées'}.\n"
        f"Niveaux : {', '.join(levels) or 'non précisés'}.\n"
        f"Spécialités : {', '.join(specialties) or 'aucune'}.\n"
        f"Style déclaré : {(body.teaching_style or 'non précisé')[:50]}.\n"
        f"Forme de l'entretien : {'visio' if body.format == 'video' else 'rencontre physique'}.\n"
        "Le répétiteur pourra donner des cours à domicile à des mineurs.\n"
        "Réponds en JSON strict avec les clés : "
        '{"intro": "2 phrases", '
        '"fond": [{"question": "...", "purpose": "...", "good_signs": "..."}], '
        '"pedagogie": [...], "situations": [...], "securite": [...]}. '
        "4 questions de fond précises sur les programmes camerounais des niveaux cités (une par matière principale "
        "au moins), 3 de pédagogie, 3 mises en situation concrètes, 4 sur la sécurité avec des mineurs. "
        + ("En anglais, registre formel, sans données personnelles." if is_english() else "En français, vouvoiement, sans données personnelles.")
    )
    system = ("Tu es WinAI, assistant des agents WinPlus qui vérifient les répétiteurs. Tu proposes des questions "
              "d'entretien justes, vérifiables et bienveillantes. Tu ne décides jamais à la place de l'agent.")
    raw = _deepseek_json(prompt, system, max_tokens=1800)
    if not isinstance(raw, dict):
        return fallback

    sections: List[GuideSection] = []
    for key, title in (("fond", "Questions de fond"), ("pedagogie", "Pédagogie"),
                       ("situations", "Mises en situation"), ("securite", "Sécurité avec des mineurs")):
        questions = _parse_questions(raw.get(key))
        if not questions:
            questions = next(s.questions for s in fallback.sections if s.key == key)
        sections.append(GuideSection(key=key, title=title, questions=questions))

    intro = str(raw.get("intro") or "").strip()[:600] or fallback.intro
    # La grille et la règle de réussite restent fixes : mêmes critères pour
    # tous les tuteurs, comparables d'un entretien à l'autre.
    return GuideResponse(intro=intro, sections=sections, identity_checklist=IDENTITY_CHECKLIST,
                         grid=DEFAULT_GRID, pass_rule=PASS_RULE, based_on="winai")


def _weighted_average(scores: Dict[str, Any], grid: List[GridCriterion]) -> Optional[float]:
    total, weights = 0.0, 0
    for c in grid:
        try:
            value = float(scores.get(c.key))
        except (TypeError, ValueError):
            continue
        if 1 <= value <= 5:
            total += value * c.weight
            weights += c.weight
    return round(total / weights, 2) if weights else None


def _template_report(body: ReportRequest) -> ReportResponse:
    scores = body.scores or {}
    avg = _weighted_average(scores, DEFAULT_GRID)
    lines = [
        f"Entretien {'en visio' if body.format == 'video' else 'en rencontre'} — "
        f"matières : {', '.join(_clean_list(body.subjects)) or 'non précisées'} ; "
        f"niveaux : {', '.join(_clean_list(body.levels)) or 'non précisés'}.",
        "",
        "Notes de l'agent :",
        body.notes.strip()[:MAX_TEXT],
        "",
    ]
    if scores:
        lines.append("Grille d'évaluation :")
        for c in DEFAULT_GRID:
            if c.key in scores:
                lines.append(f"- {c.criterion} : {scores[c.key]}/5")
        if avg is not None:
            lines.append(f"Moyenne pondérée : {avg}/5")
        lines.append("")
    lines.append("Identité : " + ("pièce et selfie validés par un admin." if body.identity_approved
                                   else "pièce et selfie joints, en attente de validation par un admin." if body.identity_attached
                                   else "pièce et selfie NON joints au dossier : à joindre avant toute vérification."))
    recommendation = "unsure"
    safety = scores.get("safety")
    try:
        safety_val = float(safety) if safety is not None else None
    except (TypeError, ValueError):
        safety_val = None
    if avg is not None:
        if (safety_val is not None and safety_val < 4) or avg < 3.5:
            recommendation = "fail"
        else:
            recommendation = "pass"
    return ReportResponse(report="\n".join(lines), recommendation=recommendation, strengths=[], concerns=[], based_on="template")


@tutor_interview_router.post("/admin/tutor-interview/report", response_model=ReportResponse)
async def tutor_interview_report(body: ReportRequest, current_user: UserTokenData = Depends(_admin_only)):
    fallback = _template_report(body)
    notes = body.notes.strip()[:MAX_TEXT]
    scores_text = ", ".join(f"{k} : {v}/5" for k, v in (body.scores or {}).items()) or "non renseignées"
    prompt = (
        "Rédige le compte rendu d'un entretien de vérification d'un répétiteur, à partir des notes de l'agent.\n"
        f"Matières : {', '.join(_clean_list(body.subjects)) or 'non précisées'} ; niveaux : {', '.join(_clean_list(body.levels)) or 'non précisés'}.\n"
        f"Forme : {'visio' if body.format == 'video' else 'rencontre'}.\n"
        f"Notes de la grille (1 à 5) : {scores_text}.\n"
        f"Règle de réussite : {PASS_RULE}\n"
        f"Identité : {'validée' if body.identity_approved else 'jointe, à valider' if body.identity_attached else 'non jointe'}.\n"
        f"Notes de l'agent :\n{notes}\n\n"
        "Réponds en JSON strict : "
        '{"report": "compte rendu factuel en 8 à 15 lignes, sans inventer de faits absents des notes", '
        '"recommendation": "pass|fail|unsure", "strengths": ["..."], "concerns": ["..."]}.'
    )
    system = ("Tu es WinAI. Tu rédiges des comptes rendus factuels et neutres pour les agents WinPlus. "
              "Tu t'en tiens strictement aux notes fournies et tu signales ce qui manque. La décision finale appartient à l'agent.")
    raw = _deepseek_json(prompt, system, max_tokens=1200)
    if not isinstance(raw, dict) or not str(raw.get("report", "")).strip():
        return fallback

    recommendation = str(raw.get("recommendation", "unsure")).strip().lower()
    if recommendation not in ("pass", "fail", "unsure"):
        recommendation = "unsure"
    # Sans identité validée, WinAI ne recommande jamais la réussite de la vérification.
    report = str(raw.get("report")).strip()[:MAX_TEXT]
    if not body.identity_approved and not body.identity_attached:
        report += "\n\nAttention : la pièce d'identité et le selfie ne sont pas joints au dossier."

    def _strs(value: Any) -> List[str]:
        return [str(v).strip()[:300] for v in value[:8] if str(v).strip()] if isinstance(value, list) else []

    return ReportResponse(report=report, recommendation=recommendation,
                          strengths=_strs(raw.get("strengths")), concerns=_strs(raw.get("concerns")), based_on="winai")
