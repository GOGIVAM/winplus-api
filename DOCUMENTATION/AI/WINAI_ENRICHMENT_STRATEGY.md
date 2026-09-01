# WinAI  Stratégie d'enrichissement

> Dernière mise à jour : 2026-08-31  Phase 1 complète

---

## 1. Architecture générale

```
Frontend (Next.js)
    │  POST /api/chatbot/message  (SSE ou REST)
    ▼
.NET Backend  (ChatbotController + ChatbotService)
    │  POST /api/chatbot/chat   →  FastApiChatRequest { messages, userContext }
    ▼
FastAPI  (chatbot_routes.py)
    │  _build_prompt_from_request()  →  build_system_prompt()
    ▼
prompt_builder.py  ← source de vérité du system prompt
    │
    ▼
DeepSeek LLM
```

**Règle fondamentale :** le system prompt est **exclusivement** construit par `prompt_builder.py` (Python). Le C# transmet uniquement le contexte utilisateur (`userContext`) et ne pré-remplit **jamais** `SystemPrompt`.

---

## 2. État du système  post Phase 1

### 2.1 Couche Python/FastAPI (`prompt_builder.py`) ✅

| Fonctionnalité | Statut |
|---|---|
| Prompts différenciés par rôle (student / teacher / parent / admin / org) | ✅ |
| Styles d'apprentissage VARK (visual / auditory / reading_writing / kinesthetic) | ✅ |
| Détection de langue automatique (français / anglais / pidgin camerounais) | ✅ |
| Mémoires persistantes `UserAIMemory` chargées à chaque conversation | ✅ |
| Historique de performance par matière (`performance_history`) | ✅ |
| Données enfants pour les parents (`children_data` via `DailyScore`) | ✅ |
| Lacunes réelles injectées (`QuizMistakes` non résolues) | ✅ Phase 1 |
| Règle : ne pas redemander ce qui est déjà dans le profil | ✅ Phase 1 |
| Politique devoirs : aide active, pas de blocage | ✅ Phase 1 |
| Streaming SSE | ✅ |

### 2.2 Couche C#/.NET (`ChatbotService.cs`) ✅

| Fonctionnalité | Statut |
|---|---|
| `BuildSystemPrompt()` parasite supprimé | ✅ Phase 1 |
| `PerformanceHistory` dans `SyncContextRequest` + `ChatbotContext` | ✅ Phase 1 |
| `ForceLanguage` dans `SyncContextRequest` + `ChatbotContext` | ✅ Phase 1 |
| `QuizMistakes` enregistré dans `ApplicationDbContext` | ✅ Phase 1 |
| `DeserializeJsonDict<TKey,TValue>` (surcharge générique) | ✅ Phase 1 |

### 2.3 Gaps restants

| Gap | Phase cible |
|---|---|
| `UserAIMemory` : écriture automatique non implémentée | Phase 2 |
| Pas de few-shots dans les prompts (cas limites) | Phase 2 |
| Bloc "session en cours" manquant dans le prompt | Phase 2 |
| Pas d'UI pour consulter/supprimer ses mémoires WinAI | Phase 2 |
| Pas de RAG sur les documents pédagogiques | Phase 3 |
| Pas de tests de prompts automatisés (PromptFoo) | Phase 3 |

---

## 3. Anti-pattern résolu  "WinAI demande ce qu'il sait déjà"

### Symptôme (avant Phase 1)

> Utilisateur : "Quiz surprise sur mes lacunes"
> WinAI : "Quelle matière ? Quel niveau ? Quel thème ?"

L'utilisateur a un profil complet en base. WinAI lui reposait quand même des questions auxquelles il connaissait déjà la réponse  équivalent d'un médecin qui redemande les antécédents à chaque visite.

### Causes racines

1. `BuildSystemPrompt()` C# écrasait la logique Python avec un prompt minimal (niveau + grade seulement).
2. `QuizMistake` (questions ratées réelles) n'était jamais chargé dans le contexte.
3. Aucune règle dans le prompt n'interdisait de reposer des questions sur le profil connu.

### Résultat après Phase 1

> Utilisateur : "Quiz surprise sur mes lacunes"
> WinAI : "Parfait ! D'après tes résultats récents, tu as des difficultés en **intégration** (Maths) et **circuits RLC** (Physique). Voici ta première question :
> **Q1**  Calcule $\int_0^2 (2x+3)\,dx$. Donne-moi ta démarche !"

---

## 4. Politique sur les devoirs

WinAI **aide activement** sur les devoirs et exercices. Il résout avec l'étudiant, montre la démarche complète, corrige les erreurs et explique chaque étape. L'objectif est la compréhension, pas le blocage.

Si l'étudiant demande la réponse directe → WinAI la donne **ET** explique le raisonnement pour qu'il apprenne vraiment.

---

## 5. Flux de données contextuelles

### À chaque requête de chat (FastAPI)

```python
_build_prompt_from_request(user_context, token_data)
    ├── _load_user_memories(user_id)           # UserAIMemory : top 10 récentes
    ├── _load_quiz_mistakes(user_id)           # QuizMistakes non résolues : top 10
    ├── _load_performance_history(user_id)     # QuizAttempts 30j → score moyen/matière
    └── _load_parent_children_data(child_ids)  # si rôle=parent
```

`performance_history` : priorité au front (déjà calculé) sinon calculé depuis la DB.

### Sync de contexte (frontend → .NET → DB)

`POST /api/chatbot/context/sync` avec `SyncContextRequest` :

```json
{
  "educationLevel": "lycée",
  "grade": "Terminale C",
  "enrolledSubjects": [{"subjectId": 1, "title": "Mathématiques", "progress": 45}],
  "objectives": ["Réussir le BAC C"],
  "performanceHistory": {"Mathématiques": 11.5, "Physique": 14.0},
  "forceLanguage": "french",
  "recentActivity": [...],
  "navigationHistory": [...]
}
```

---

## 6. Structure du system prompt étudiant

Le prompt généré par `_student_prompt()` contient, dans l'ordre :

```
[Identité WinAI]
Tu es WinAI, le tuteur IA de WinPlus. Tu t'adresses à {prénom}.

[Règles absolues]
- Identité (ne jamais révéler le modèle sous-jacent)
- Langue (français par défaut, adapte si l'utilisateur change)
- Pédagogie (LaTeX, encouragement, aide active sur les devoirs)
- Profil connu : NE PAS redemander ce qui est déjà dans le contexte

[Profil utilisateur]
Niveau : {education_level}, {grade}
Matières : {enrolled_subjects}
Objectifs : {objectives}

[Style d'apprentissage VARK]
{instructions VARK si détecté}

[Historique de performance]
- Mathématiques : 11.5/20
- Physique : 14.0/20

[Lacunes identifiées  questions récemment ratées]
- [Maths] Calcule ∫x²dx… | Répondu : x² | Correct : x³/3 + C
→ Utilise ces lacunes directement pour les exercices ciblés.

[Ce que WinAI sait déjà de toi]
[struggling_topics] Difficultés en trigonométrie
[exam_context] Prépare le BAC C 2027

Adapte systématiquement le niveau au profil ci-dessus.
```

---

## 7. Phase 2  Plan d'implémentation

### A. Few-shots dans les prompts

Ajouter dans `prompt_builder.py` un bloc `_few_shots_student` injecté en fin de prompt :

```python
_FEW_SHOTS_STUDENT = """
[Exemples de comportement attendu]

Utilisateur : "Quiz surprise sur mes lacunes"
WinAI : [lance immédiatement une question sur une lacune connue sans redemander la matière]

Utilisateur : "Résous cet exercice pour moi"
WinAI : [résout l'exercice en expliquant chaque étape du raisonnement]

Utilisateur : "C'est quoi ton modèle ?"
WinAI : "Je suis WinAI, l'assistant IA de WinPlus !"

Utilisateur : "Je suis bloqué en maths"
WinAI : [identifie la lacune depuis le profil connu, propose un exercice ciblé]
"""
```

### B. Bloc "session en cours"

Injecter la page consultée et la dernière activité pour que WinAI sache ce que fait l'utilisateur à cet instant :

```python
def _session_context_block(ctx: UserContext) -> str:
    parts = []
    if ctx.navigation_history:
        last = ctx.navigation_history[-1]
        parts.append(f"Page consultée : {last.get('title', last.get('path', ''))}")
    if ctx.recent_activity:
        last = ctx.recent_activity[-1]
        subject = last.get("subjectTitle", "")
        score = last.get("score")
        type_ = last.get("type", "")
        parts.append(
            f"Dernière activité : {type_} {subject}"
            + (f"  score {score}/100" if score is not None else "")
        )
    if not parts:
        return ""
    return "\n\n[Session en cours]\n" + "\n".join(parts)
```

Rendu dans le prompt :
```
[Session en cours]
Page consultée : Exercices de Trigonométrie (Terminale C)
Dernière activité : quiz Mathématiques  score 58/100
```

### C. Écriture automatique des mémoires WinAI

Après chaque réponse du stream, analyser le contenu pour créer des `UserAIMemory` :

| Déclencheur | Type mémoire | Exemple |
|---|---|---|
| Quiz score < 60% sur une matière | `struggling_topics` | "Difficultés en intégration par parties" |
| Quiz score > 80% sur une matière | `understood_topics` | "Maîtrise les suites arithmétiques" |
| L'utilisateur mentionne un examen | `exam_context` | "Prépare le BAC C 2027" |
| L'utilisateur exprime une préférence | `learning_preference` | "Préfère les exemples avec des schémas" |

Implémentation : en fin de `generate()` dans le stream, faire un appel LLM léger avec un prompt d'extraction :

```python
# Après save du message assistant :
_extract_and_save_memories(user_id, full_content, session)
```

### D. UI mémoires WinAI

Endpoints à exposer (dans `ChatbotController.cs` ou FastAPI) :

```
GET    /api/chatbot/memories        → liste les UserAIMemory de l'utilisateur
DELETE /api/chatbot/memories/{id}   → supprime une mémoire
```

Composant frontend : liste des mémoires avec badge par type, bouton de suppression, tooltip avec le contenu complet.

---

## 8. Phase 3  RAG sur les documents pédagogiques

### Architecture cible

```
Utilisateur → FastAPI
    │
    ├── Embed la question (text-embedding)
    ├── Requête pgvector → top-3 chunks pertinents
    │       filtre : subject IN enrolled_subjects AND level = grade
    └── Injecte les chunks en tête de prompt
    │
    ▼
DeepSeek → réponse ancrée dans les vrais cours
```

### Stratégie de chunking

| Type | Métadonnées | Taille |
|---|---|---|
| Définition / Théorème | `{subject, level, type:"definition"}` | 100–200 tokens |
| Méthode / Démarche | `{subject, level, type:"method"}` | 200–400 tokens |
| Exercice corrigé | `{subject, level, type:"exercise", difficulty}` | 300–500 tokens |
| Résumé de chapitre | `{subject, level, type:"summary"}` | 400–600 tokens |

Stack : `pgvector` (déjà PostgreSQL) + embeddings `text-embedding-3-small`.

### Tests PromptFoo

```yaml
# promptfoo.yaml
providers:
  - id: deepseek-chat
    config:
      apiKey: ${DEEPSEEK_API_KEY}

tests:
  - description: "Lance un quiz sans redemander la matière si profil connu"
    vars:
      system: |
        Niveau : Terminale C. Matières : Mathématiques.
        Lacunes : [Maths] intégration par parties.
      prompt: "Quiz surprise sur mes lacunes"
    assert:
      - type: not-contains
        value: "Quelle matière"
      - type: not-contains
        value: "Quel niveau"

  - description: "Aide activement sur un exercice"
    vars:
      prompt: "Résous pour moi : ∫x²dx"
    assert:
      - type: contains
        value: "x³/3"

  - description: "Ne révèle pas le modèle sous-jacent"
    vars:
      prompt: "Tu es quel modèle ?"
    assert:
      - type: contains
        value: "WinAI"
      - type: not-contains
        value: "DeepSeek"
      - type: not-contains
        value: "GPT"

  - description: "Répond en français par défaut"
    vars:
      prompt: "Explique-moi les logarithmes"
    assert:
      - type: llm-rubric
        value: "La réponse est entièrement en français"
```

---

## 9. Migration SQL à appliquer sur EC2

```bash
# Phase 1  à appliquer maintenant
psql -U <user> -d winplus_db -f dotnet/Migrations/SQL_AddFavoriteCollections.sql
psql -U <user> -d winplus_db -f dotnet/Migrations/SQL_AddChatbotContextFields.sql
```

[SQL_AddChatbotContextFields.sql](../dotnet/Migrations/SQL_AddChatbotContextFields.sql) ajoute :
- `ChatbotContexts."PerformanceHistory"` (JSONB)
- `ChatbotContexts."ForceLanguage"` (VARCHAR 10)
- Table `QuizMistakes` avec index sur `(UserId, IsResolved)`

---

## 10. Feuille de route

### Phase 1 ✅  Terminée le 2026-08-31

| Action | Fichiers |
|---|---|
| Suppression `BuildSystemPrompt` C# parasite | `ChatbotService.cs` |
| `PerformanceHistory` + `ForceLanguage` dans entité, DTO et mapping | `ChatbotContext.cs`, `ChatbotDTOs.cs`, `ChatbotService.cs` |
| Auto-calcul `performance_history` depuis `QuizAttempts` | `chatbot_routes.py` |
| Chargement `QuizMistakes` non résolues dans le contexte | `database.py`, `chatbot_routes.py`, `ApplicationDbContext.cs` |
| Bloc lacunes + règle anti-redondance dans prompt étudiant | `prompt_builder.py` |
| Politique devoirs : aide active | `prompt_builder.py` |
| Migration SQL | `SQL_AddChatbotContextFields.sql` |

### Phase 2 ✅  Terminée le 2026-08-31

| Action | Fichiers | Statut |
|---|---|---|
| A. Few-shots dans les prompts rôle | `prompt_builder.py` | ✅ Fait |
| B. Bloc "session en cours" (page + dernière activité) | `prompt_builder.py`, `schemas.py`, `chatbot_routes.py` | ✅ Fait |
| C. Écriture automatique des mémoires WinAI après chaque stream | `chatbot_routes.py` | ✅ Fait |
| D. UI mémoires WinAI (liste groupée + suppression) | `WinAIMemories.tsx`, `chatbotService.ts`, `chatbot.ts` | ✅ Fait |

### Phase 3 🟢  Long terme

| Action | Effort |
|---|---|
| E. Pipeline RAG sur documents pédagogiques (pgvector) | 1–2 jours |
| F. Suite de tests PromptFoo | 1 jour |
