# WinPlus AI Features — Developer Guide

Four AI features powered by the Python FastAPI backend (port 8000) and wired into the React frontend.

---

## 1. Personalized Learning Path

### Endpoint
```
GET /api/learning-path/{user_id}
```

### Response
```json
{
  "success": true,
  "user_id": 42,
  "learning_velocity": 2.5,
  "total_duration_days": 90,
  "estimated_end_date": "2026-09-15",
  "phases": [
    {
      "phase": 1,
      "name": "Fondamentaux",
      "duration_days": 30,
      "focus_areas": ["Algèbre", "Géométrie"],
      "difficulty": "facile",
      "target_completion": 80,
      "actions": ["Réviser les formules de base", "Faire 3 exercices par jour"]
    }
  ],
  "recommendations": {
    "daily_study_time": "2h/jour",
    "focus_areas": ["Algèbre linéaire"],
    "growth_areas": ["Statistiques"]
  },
  "generated_at": "2026-06-11T10:00:00"
}
```

### Frontend
- Component: `src/features/learningPath/LearningPath.tsx`
- Hook: `src/features/learningPath/useLearningPath.ts`
- Tab: Dashboard → "Mon Parcours"

### Notes
- Wraps `UserPerformanceAnalyzer.generate_learning_path(user_id)`
- Phase "current" is determined by `learning_velocity` on the frontend
- Focus area chips link to catalog search via `onNavigateCatalog` prop

---

## 2. Adaptive Quiz Generation

### Endpoint
```
POST /api/adaptive-quiz
```

### Request
```json
{
  "user_id": 42,
  "subject": "",
  "count": 8
}
```

### Response
```json
{
  "success": true,
  "subject": "Mathématiques",
  "weak_areas": ["Probabilités", "Statistiques"],
  "questions": [
    {
      "id": 1,
      "question": "Quelle est la probabilité de...",
      "options": ["A) 1/4", "B) 1/2", "C) 3/4", "D) 1"],
      "correct_answer": "B) 1/2",
      "explanation": "Parce que...",
      "difficulty": "Moyen",
      "topic": "Probabilités"
    }
  ],
  "count": 8,
  "generated_at": "2026-06-11T10:00:00"
}
```

### Frontend
- Component: `src/components/Quiz/QuizHubMain.tsx` — "WinAI" card at top of "Disponibles" tab
- Uses the existing `QuizActive` screen after generating
- `correct_answer` (snake_case from Python) is normalized to `correctAnswer` (camelCase) on the frontend

### Notes
- Uses DeepSeek LLM; response time can be 10–30s
- Backend first analyzes weak areas via `analyze_user_progress`, then prompts DeepSeek for questions targeting those areas
- Strips markdown fences from LLM response before `json.loads()`
- Synthetic Quiz object has `id: -1` and is not persisted in the database
- Count is clamped to 3–20 by the backend schema

---

## 3. Learning Style Analysis (VARK)

### Endpoint
```
POST /api/learning-style
```

### Request
```json
{
  "answers": [
    { "question_id": "q1", "style_tag": "V" },
    { "question_id": "q2", "style_tag": "K" }
  ]
}
```

### Response
```json
{
  "success": true,
  "style": "kinesthetic",
  "label": "Apprenant Kinesthésique",
  "description": "Tu apprends par la pratique et les exercices concrets.",
  "winplus_tips": [
    "Lance-toi sur les quiz WinPlus",
    "Résous des annales",
    "Demande des exercices pratiques à WinAI",
    "Alterne cours courts et exercices immédiats"
  ],
  "score_breakdown": { "V": 1, "A": 2, "R": 1, "K": 4 }
}
```

### Style keys
| Tag | Key | Label |
|-----|-----|-------|
| V | `visual` | Apprenant Visuel |
| A | `auditory` | Apprenant Auditif |
| R | `reading_writing` | Apprenant Lecteur/Scripteur |
| K | `kinesthetic` | Apprenant Kinesthésique |
| tie | `mixed` | Apprenant Polyvalent |

### Frontend
- Component: `src/features/learningStyle/LearningStyleQuiz.tsx`
- Launched from: Dashboard → "Prédiction IA" tab → "Commencer le test"
- 8 VARK questions, each with 4 options tagged V/A/R/K
- Falls back to local scoring if the API is unreachable

---

## 4. Success Prediction

### Endpoint
```
GET /api/success-prediction/{user_id}
```

### Response
```json
{
  "success": true,
  "user_id": 42,
  "probability": 73,
  "level": "high",
  "positive_factors": [
    { "label": "Bonne régularité", "impact": "positive", "detail": "Tu travailles de façon consistante" }
  ],
  "negative_factors": [
    { "label": "Faible complétion", "impact": "negative", "detail": "Seulement 45% des contenus complétés" }
  ],
  "context": {
    "completion_rate": 45.0,
    "learning_velocity": 2.5,
    "learning_pattern": "consistent",
    "total_hours": 38.5,
    "enrolled_subjects": 3
  },
  "computed_at": "2026-06-11T10:00:00"
}
```

### Probability formula
```
probability = completion_rate × 0.4
            + velocity_score  × 0.3   (velocity mapped 0–5 → 0–100)
            + pattern_score   × 0.2   (very_consistent=100, consistent=75, moderate=50, intermittent=25, inconsistent=15)
            + time_score      × 0.1   (total_hours / 200 × 100, capped at 100)
```
Capped at 95 to avoid false certainty.

### Level thresholds
- `high`: ≥ 70%
- `medium`: 45–69%
- `low`: < 45%

### Frontend
- Component: `src/components/ai/SuccessPrediction.tsx`
- Shown in: Dashboard → "Prédiction IA" tab
- SVG ring gauge with animated `stroke-dashoffset`
- Color: green ≥70%, orange ≥45%, red <45%
- WinAI recommendation streamed via `POST /api/chatbot/stream` (SSE)

---

## Service layer

All four features are called via `src/services/pythonApi.ts`:

```typescript
import { pythonAI, pythonStreamUrl } from './pythonApi';

pythonAI.getLearningPath(userId)
pythonAI.generateAdaptiveQuiz(userId, subject, count)
pythonAI.analyzeLearningStyle(answers)
pythonAI.getSuccessPrediction(userId)
```

Base URL resolution order:
1. `VITE_PYTHON_URL` env var
2. `TOKEN_CONFIG.API.FLASK_URL` (from tokenConfig.ts)
3. `http://localhost:8000` (hardcoded fallback)

---

## Environment variables

```env
VITE_PYTHON_URL=http://localhost:8000   # Python FastAPI base URL
```

Set this in `.env.local` for local development or in your deployment environment.
