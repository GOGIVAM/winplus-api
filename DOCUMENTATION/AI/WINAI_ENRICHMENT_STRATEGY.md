# WinAI — Stratégie d'enrichissement (août 2026)

---

## Anti-pattern critique — WinAI demande ce qu'il sait déjà

### Symptôme observé

> Utilisateur : "Quiz surprise sur mes lacunes"
> WinAI : "Quelle matière veux-tu réviser ? Quel niveau ? Quel thème ?"

C'est une **régression de confiance** : l'utilisateur a un profil complet en base, WinAI lui demande quand même des informations qu'il possède. C'est l'équivalent d'un médecin qui redemande à chaque visite les antécédents du patient qu'il a en dossier.

### Cause racine

Deux problèmes distincts :

**1. Règle manquante dans le prompt** — `_student_prompt()` n'interdit pas explicitement de reposer des questions sur le profil connu.

**2. Données lacunes non transmises** — La table `QuizMistake` (questions ratées réelles, mauvaise réponse donnée, bonne réponse) existe en DB mais n'est **jamais chargée** dans le contexte WinAI. Sans ces données, WinAI ne peut pas "savoir" les lacunes et doit les demander.

### Correctifs

#### A. Règle dans `prompt_builder.py` — `_student_prompt()`

Ajouter dans les règles absolues :

```python
"- Tu connais déjà le profil de l'utilisateur (niveau, matières, objectifs, scores). "
"NE POSE JAMAIS de questions sur des informations que tu possèdes déjà dans le contexte. "
"Si l'utilisateur demande un quiz, lance-le directement en utilisant ses matières et lacunes connues. "
"Si le profil est vide, alors seulement tu peux demander."
```

Et ajouter un bloc dédié aux lacunes (voir section `_mistakes_block` ci-dessous).

#### B. Charger les `QuizMistake` non résolus dans le contexte

Dans [chatbot_routes.py](../python/routes/chatbot_routes.py), ajouter une fonction de chargement :

```python
def _load_quiz_mistakes(user_id: int, limit: int = 10) -> list[dict]:
    """Charge les questions récentes ratées et non résolues."""
    try:
        db = Database()
        session = db.SessionLocal()
        try:
            rows = (
                session.query(QuizMistake)
                .filter(
                    QuizMistake.UserId == user_id,
                    QuizMistake.IsResolved == False
                )
                .order_by(QuizMistake.CreatedAt.desc())
                .limit(limit)
                .all()
            )
            return [
                {
                    "subject": r.Subject or "Général",
                    "question": r.Question,
                    "given_answer": r.GivenAnswer,
                    "correct_answer": r.CorrectAnswer,
                }
                for r in rows
            ]
        finally:
            session.close()
    except Exception as e:
        logger.warning(f"Could not load quiz mistakes for {user_id}: {e}")
        return []
```

Ajouter `QuizMistake` au modèle `database.py` s'il n'y est pas encore, et l'injecter dans `UserContext` :

```python
@dataclass
class UserContext:
    # ... champs existants ...
    quiz_mistakes: List[Dict] = field(default_factory=list)  # lacunes réelles non résolues
```

#### C. Nouveau bloc dans `prompt_builder.py`

```python
def _mistakes_block(ctx: UserContext) -> str:
    if not ctx.quiz_mistakes:
        return ""
    lines = []
    for m in ctx.quiz_mistakes[:8]:
        subject = m.get("subject", "")
        question = m.get("question", "")[:120]
        given = m.get("given_answer", "?")
        correct = m.get("correct_answer", "?")
        lines.append(f"  [{subject}] Q: {question}… | Répondu: {given} | Correct: {correct}")
    return (
        "\n\n[Lacunes identifiées — questions récemment ratées]\n"
        + "\n".join(lines)
        + "\n→ Pour un quiz ciblé, prioritise ces notions. Ne redemande pas la matière ou le niveau."
    )
```

Injecter dans `_student_prompt()` : `{_mistakes_block(ctx)}` avant `Adapte systématiquement…`

#### D. Enregistrer `QuizMistake` dans ApplicationDbContext

Dans [ApplicationDbContext.cs](../dotnet/Data/ApplicationDbContext.cs), ajouter :

```csharp
public DbSet<QuizMistake> QuizMistakes => Set<QuizMistake>();
```

Permet au .NET de charger et transmettre les lacunes via le sync de contexte.

### Résultat attendu après correction

> Utilisateur : "Quiz surprise sur mes lacunes"
> WinAI : "Parfait ! D'après tes résultats récents, tu as des difficultés en **intégration** (Maths) et **circuits RLC** (Physique). Je commence par un exercice de niveau Terminale C :
> **Question 1** : Calcule ∫(2x + 3)dx entre 0 et 2. Tu as 2 minutes !"

---

## État des lieux : ce qui existe déjà

### Couche Python/FastAPI — `prompt_builder.py` ✅ Solide

Le cœur de l'IA est déjà bien structuré :

| Fonctionnalité | Statut |
|---|---|
| Prompts différenciés par rôle (student / teacher / parent / admin / org) | ✅ Implémenté |
| Styles d'apprentissage VARK (visual / auditory / reading_writing / kinesthetic) | ✅ Implémenté |
| Détection de langue (français / anglais / pidgin camerounais) | ✅ Implémenté |
| Mémoires persistantes (`UserAIMemory` en DB) | ✅ Chargées au runtime |
| Historique de performance par matière (`performance_history`) | ✅ Supporté par `UserContext` |
| Données enfants pour les parents (`children_data`) | ✅ Chargées depuis `DailyScore` |
| Streaming SSE | ✅ Implémenté |

### Couche C#/.NET — `ChatbotService.cs` ⚠️ En retard

`BuildSystemPrompt()` dans le C# est **minimal et désynchronisé** de la version Python. Il n'ajoute que `EducationLevel` et `Grade` alors que le Python gère VARK, mémoires, performances, etc. Ce prompt C# est transmis à FastAPI via `FastApiChatRequest.SystemPrompt` et **écrase** la logique Python si renseigné.

**Règle à appliquer :** ne jamais pré-remplir `SystemPrompt` depuis le C#. Laisser FastAPI construire le prompt via `_build_prompt_from_request()`.

### Gaps identifiés

| Gap | Impact | Statut |
|---|---|---|
| `SyncContextRequest` (C#) ne transmet pas `performance_history` | WinAI ne connaît pas les scores réels | ✅ Corrigé Phase 1 |
| `SyncContextRequest` ne transmet pas `force_language` | Détection langue non activable depuis le front | ✅ Corrigé Phase 1 |
| C# `BuildSystemPrompt` court-circuite Python | Régression silencieuse sur tout le prompt | ✅ Corrigé Phase 1 |
| `QuizMistakes` non chargées → WinAI demande ce qu'il sait | Anti-pattern de confiance utilisateur | ✅ Corrigé Phase 1 |
| Politique devoirs bloquante | WinAI refusait d'aider sur les exercices | ✅ Corrigé Phase 1 |
| `UserAIMemory` non exposé côté frontend | Mémoires opaques pour l'utilisateur | 🟡 Phase 2 |
| Pas de few-shots dans les prompts rôle | Réponses hors-format sur certains cas limites | 🟡 Phase 2 |
| Pas de RAG sur les documents pédagogiques | WinAI ne connaît pas le contenu des cours | 🟢 Phase 3 |

---

## Niveau 1 — Corriger les régressions actuelles (Immédiat)

### 1.1 Désactiver le `BuildSystemPrompt` C# parasite

Dans [ChatbotService.cs](../dotnet/Services/ChatbotService.cs), méthode `BuildFastApiRequestAsync()` :

```csharp
// AVANT (problématique)
if (includeContext)
{
    var context = await _repository.GetContextForUserAsync(userId);
    if (context != null)
    {
        request.UserContext = MapToChatbotContextResponse(context);
        request.SystemPrompt = BuildSystemPrompt(request.UserContext); // ← supprime cette ligne
    }
}

// APRÈS (correct)
if (includeContext)
{
    var context = await _repository.GetContextForUserAsync(userId);
    if (context != null)
    {
        request.UserContext = MapToChatbotContextResponse(context);
        // SystemPrompt = null → FastAPI construit le prompt via prompt_builder.py
    }
}
```

La méthode privée `BuildSystemPrompt(ChatbotContextResponse)` dans `ChatbotService.cs` peut être supprimée entièrement.

### 1.2 Ajouter `performance_history` et `force_language` au sync de contexte

Dans [ChatbotDTOs.cs](../dotnet/Models/DTOs/ChatbotDTOs.cs), enrichir `SyncContextRequest` :

```csharp
public class SyncContextRequest
{
    // ... champs existants ...

    /// <summary>
    /// Scores moyens par matière sur les 30 derniers jours : {"Mathématiques": 14.5, "Physique": 11.0}
    /// </summary>
    public Dictionary<string, float>? PerformanceHistory { get; set; }

    /// <summary>
    /// Langue forcée : "french" | "english" | "pidgin"
    /// </summary>
    [MaxLength(10)]
    public string? ForceLanguage { get; set; }
}
```

Et dans [ChatbotDTOs.cs](../dotnet/Models/DTOs/ChatbotDTOs.cs), enrichir `FastApiChatRequest` pour passer ces champs à FastAPI :

```csharp
public class FastApiChatRequest
{
    // ... champs existants ...
    public Dictionary<string, float>? PerformanceHistory { get; set; }
    public string? ForceLanguage { get; set; }
}
```

Dans [schemas.py](../python/schemas.py), s'assurer que `ChatbotContextRequest` accepte ces champs :

```python
class ChatbotContextRequest(BaseModel):
    # ... champs existants ...
    performance_history: Optional[Dict[str, float]] = None
    force_language: Optional[str] = None  # "french" | "english" | "pidgin"
```

### 1.3 Auto-calculer `performance_history` depuis la DB

Dans [chatbot_routes.py](../python/routes/chatbot_routes.py), enrichir `_build_prompt_from_request()` :

```python
def _load_performance_history(user_id: int) -> dict[str, float]:
    """Calcule le score moyen par matière sur les 30 derniers jours."""
    try:
        from datetime import datetime, timedelta, timezone
        db = Database()
        session = db.SessionLocal()
        cutoff = datetime.now(timezone.utc) - timedelta(days=30)
        try:
            attempts = (
                session.query(QuizAttempt)
                .filter(QuizAttempt.UserId == user_id, QuizAttempt.CompletedAt >= cutoff)
                .all()
            )
            by_subject: dict[str, list[float]] = {}
            for a in attempts:
                subject = getattr(a, "SubjectTitle", None) or "Général"
                by_subject.setdefault(subject, []).append(float(a.Score or 0))
            return {s: round(sum(v) / len(v), 1) for s, v in by_subject.items()}
        finally:
            session.close()
    except Exception as e:
        logger.warning(f"Could not load performance history for {user_id}: {e}")
        return {}
```

---

## Niveau 2 — Enrichissement du system prompt (Court terme)

### 2.1 Ajouter des few-shots aux cas limites connus

Dans `prompt_builder.py`, ajouter une section few-shots dans `_student_prompt()` pour les cas récurrents :

```python
_FEW_SHOTS_STUDENT = """
[Exemples de comportement attendu]

Utilisateur : "Résous-moi ce devoir"
WinAI : "Je ne peux pas résoudre le devoir à ta place, mais voici comment aborder ce type de problème étape par étape : ..."

Utilisateur : "C'est quoi ton modèle ?"
WinAI : "Je suis WinAI, l'assistant IA de WinPlus. Je suis là pour t'aider dans tes révisions !"

Utilisateur : "Aide-moi en mathématiques, je suis nul"
WinAI : "Pas de souci ! Commençons par identifier exactement où ça bloque. Quel est le dernier sujet que tu as étudié ?"
"""
```

### 2.2 Ajouter un bloc "session courante" dans le contexte

Injecter dans le prompt la page courante et la dernière activité :

```python
def _session_context_block(ctx: UserContext) -> str:
    """Bloc dynamique : ce que fait l'utilisateur RIGHT NOW."""
    # ctx.navigation_history[-1] si disponible
    # ctx.recent_activity[-1] si disponible
    ...
```

Exemple de rendu dans le prompt :
```
[Session en cours]
L'étudiant consulte actuellement : Exercices de Trigonométrie (Terminale C).
Dernière activité : Quiz "Dérivées" — score 12/20 il y a 2h.
```

---

## Niveau 3 — Mémoires WinAI (Moyen terme)

### 3.1 Flux de création de mémoires

La table `UserAIMemory` existe. Le flux actuel :
- Chargement en lecture seule au début de chaque conversation ✅
- Écriture : **non implémentée** ❌

Types de mémoires à créer automatiquement :

| Type | Déclencheur | Exemple |
|---|---|---|
| `struggling_topics` | Score quiz < 60% | "Difficultés en intégration par parties" |
| `understood_topics` | Score quiz > 80% | "Maîtrise les suites arithmétiques" |
| `learning_preference` | Détecté via conversation | "Préfère les exemples concrets" |
| `exam_context` | Mentionné dans le chat | "Prépare le BAC C 2027" |

### 3.2 Endpoint de gestion des mémoires

Ajouter dans [ChatbotController.cs](../dotnet/Controllers/ChatbotController.cs) ou dans FastAPI :

```
GET  /api/chatbot/memories          → liste les mémoires de l'utilisateur
POST /api/chatbot/memories          → crée une mémoire manuellement
DELETE /api/chatbot/memories/{id}   → supprime une mémoire
```

---

## Niveau 4 — RAG sur les documents pédagogiques (Phase 2)

### Architecture cible

```
Utilisateur → FastAPI → [Retriever] → pgvector → chunks pertinents
                     ↓
              Inject dans prompt
                     ↓
              DeepSeek → réponse enrichie
```

### Stratégie de chunking

Découper les sujets/fiches en chunks **orientés tâche** (pas par page) :

| Type de chunk | Métadonnées | Taille cible |
|---|---|---|
| Définition / Théorème | `{subject, level, type:"definition"}` | 100-200 tokens |
| Méthode / Démarche | `{subject, level, type:"method"}` | 200-400 tokens |
| Exercice corrigé | `{subject, level, type:"exercise", difficulty}` | 300-500 tokens |
| Résumé de chapitre | `{subject, level, type:"summary"}` | 400-600 tokens |

### Stack recommandée

- **Embeddings** : `text-embedding-3-small` (OpenAI) ou embeddings DeepSeek
- **Stockage vecteurs** : `pgvector` (déjà PostgreSQL sur EC2)
- **Retrieval** : top-3 chunks par cosine similarity, injectés en début de prompt

---

## Niveau 5 — Outillage et validation (Phase 2)

### Tests de prompts

Utiliser [PromptFoo](https://promptfoo.dev) pour valider les variations de prompts :

```yaml
# promptfoo.yaml
providers:
  - id: deepseek-chat
    config:
      apiKey: ${DEEPSEEK_API_KEY}

tests:
  - description: "Ne donne pas de réponse directe à un devoir"
    vars:
      prompt: "Résous cet exercice : ∫x²dx"
    assert:
      - type: not-contains
        value: "x³/3 + C"

  - description: "Répond en français par défaut"
    vars:
      prompt: "Bonjour, explique-moi les logarithmes"
    assert:
      - type: contains
        value: "logarithme"
```

---

## Feuille de route consolidée

### Phase 1 — Terminée le 2026-08-31 ✅

| Étape | Fichier(s) modifiés | Statut |
|---|---|---|
| 1. Supprimer `BuildSystemPrompt` C# parasite | `ChatbotService.cs` | ✅ Fait |
| 2. Ajouter `performance_history` + `force_language` au DTO sync | `ChatbotDTOs.cs`, `ChatbotContext.cs` | ✅ Fait |
| 3. Auto-calcul `performance_history` depuis `QuizAttempts` (DB) | `chatbot_routes.py` | ✅ Fait |
| 4. Charger `QuizMistakes` dans le contexte WinAI | `database.py`, `chatbot_routes.py`, `ApplicationDbContext.cs` | ✅ Fait |
| 5. Bloc lacunes + règle anti-redondance dans le prompt étudiant | `prompt_builder.py` | ✅ Fait |
| 6. Politique devoirs : aide active (pas de blocage) | `prompt_builder.py` | ✅ Fait |
| 7. Migration SQL Phase 1 | `SQL_AddChatbotContextFields.sql` | ✅ Prête à appliquer |

**À appliquer sur EC2 :**
```bash
psql -U <user> -d winplus_db -f SQL_AddChatbotContextFields.sql
```

---

### Phase 2 — À faire (court terme)

| Étape | Fichier(s) à modifier | Effort | Priorité |
|---|---|---|---|
| A. Few-shots dans les prompts rôle | `prompt_builder.py` | 1h | 🟡 Moyenne |
| B. Bloc "session en cours" (page consultée + dernière activité) | `prompt_builder.py` | 45 min | 🟡 Moyenne |
| C. Écriture automatique des mémoires WinAI après chaque session | `chatbot_routes.py` | 2h | 🟡 Moyenne |
| D. UI mémoires WinAI (voir/supprimer ses mémoires) | composant React | 3h | 🟡 Moyenne |

### Phase 3 — RAG & validation (long terme)

| Étape | Fichier(s) à modifier | Effort | Priorité |
|---|---|---|---|
| E. Pipeline RAG sur documents pédagogiques | pgvector + service embedding | 1-2 jours | 🟢 Phase 3 |
| F. Tests PromptFoo sur jeu de données représentatif | `promptfoo.yaml` | 1 jour | 🟢 Phase 3 |
