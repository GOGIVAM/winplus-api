#!/usr/bin/env python3
"""
Service IA FastAPI avec authentification
Utilise les tables ASP.NET (Subjects, CourseContents)
"""

from fastapi import FastAPI, Query, HTTPException, Depends, status, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from slowapi import Limiter
from slowapi.util import get_remote_address
from slowapi.errors import RateLimitExceeded
from contextlib import asynccontextmanager
from dotenv import load_dotenv
from datetime import datetime
import os
import logging
from typing import Optional, List

from database import Database, init_db, Subject, CourseContent
from models.nlp_analyzer import NLPAnalyzer
from models.recommender import Recommender
from services.user_performance_analyzer import UserPerformanceAnalyzer
from auth import verify_token, require_role, UserTokenData
from routes.chatbot_routes import chatbot_router
from routes.quiz_explain_routes import quiz_explain_router
from routes.exam_coach_routes import exam_coach_router
from routes.parent_alert_routes import parent_alert_router
import json
from schemas import (
    HealthResponse, SubjectResponse, RecommendationResponse,
    RecommendationsListResponse, AnalysisRequest, AnalysisResponse,
    ProgressAnalysisResponse, ApiResponse, PaginatedResponse,
    LearningPathResponse, AdaptiveQuizRequest, AdaptiveQuizResponse,
    LearningStyleRequest, LearningStyleResponse,
    SuccessPredictionResponse,
)
from services.deepseek_client import get_deepseek_client

# Configuration
load_dotenv()
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# ==================== LIFESPAN MANAGEMENT ====================
@asynccontextmanager
async def lifespan(app: FastAPI):
    """Gestion du cycle de vie de l'application"""
    # Startup
    logger.info("ðŸš€ Starting FastAPI application...")
    db = Database()
    nlp_analyzer = NLPAnalyzer()
    logger.info("âœ… Database and NLP analyzer initialized")
    
    yield  # Application runs here
    
    # Shutdown
    logger.info("ðŸ›‘ Shutting down FastAPI application...")


# ==================== APP INITIALIZATION ====================
app = FastAPI(
    title="WinPlus IA Service",
    description="Educational Platform with ML/NLP capabilities",
    version="2.0.0",
    lifespan=lifespan
)

# ==================== CORS CONFIGURATION ====================
app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        "https://winplus.cm",
        "https://www.winplus.cm",
        "http://localhost:5173",
        "http://localhost:3000",
    ],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# ==================== RATE LIMITING ====================
limiter = Limiter(key_func=get_remote_address)
app.state.limiter = limiter


@app.exception_handler(RateLimitExceeded)
async def rate_limit_exceeded_handler(request, exc):
    return JSONResponse(
        status_code=status.HTTP_429_TOO_MANY_REQUESTS,
        content={
            'success': False,
            'error': 'rate_limit_exceeded',
            'message': 'Too many requests. Please try again later.'
        }
    )


# ==================== INITIALIZATION ====================
db = Database()
nlp_analyzer = NLPAnalyzer()
recommender = Recommender(db)
performance_analyzer = UserPerformanceAnalyzer(db=db, nlp_analyzer=nlp_analyzer, recommender=recommender)


# ==================== ROUTERS ====================
app.include_router(chatbot_router, prefix="/api/chatbot", tags=["chatbot"])
app.include_router(quiz_explain_router, prefix="/api/quiz", tags=["quiz"])
app.include_router(exam_coach_router, prefix="/api/exam-coach", tags=["exam-coach"])
app.include_router(parent_alert_router, prefix="/api/parent-alerts", tags=["parent"])


# ==================== HEALTH CHECK (Public) ====================
@app.get('/health', response_model=HealthResponse, tags=["health"])
@limiter.limit("30/minute")
async def health(request: Request):
    """Health check - pas d'auth requise"""
    return {
        'status': 'healthy',
        'service': 'IA Educational Service',
        'version': '2.0.0',
        'database': 'PostgreSQL (ASP.NET Subjects schema)'
    }


# ==================== SUBJECTS ENDPOINTS ==================
@app.get('/api/subjects', response_model=PaginatedResponse, tags=["subjects"])
@limiter.limit("30/minute")
async def get_subjects(
    request: Request,
    category: Optional[str] = Query(None),
    search: Optional[str] = Query(None),
    featured: Optional[bool] = Query(None),
    skip: int = Query(0, ge=0),
    limit: int = Query(20, ge=1, le=100)
):
    """Liste des Ã©preuves avec filtres"""
    try:
        filters = {}
        
        if category:
            filters['category'] = category
        if search:
            filters['search'] = search
        if featured is not None:
            filters['featured'] = featured
        
        subjects = db.get_all_subjects(filters)
        total = len(subjects)
        subjects = subjects[skip:skip + limit]
        
        logger.info(f"[API] ðŸ“š GET /api/subjects - {len(subjects)} Ã©preuves retournÃ©es")
        
        return {
            'success': True,
            'count': len(subjects),
            'data': subjects
        }
    except Exception as e:
        logger.error(f"[API] âŒ GET /api/subjects - Error: {str(e)}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


@app.get('/api/subjects/{subject_id}', response_model=ApiResponse, tags=["subjects"])
@limiter.limit("30/minute")
async def get_subject_detail(request: Request, subject_id: int):
    """DÃ©tail d'une Ã©preuve avec ses contenus"""
    try:
        subject = db.get_subject_by_id(subject_id)
        
        if not subject:
            logger.warning(f"[API] âš ï¸ GET /api/subjects/{subject_id} - Not found")
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail='Subject not found'
            )
        
        contents = db.get_course_contents(subject_id)
        subject['contents'] = contents
        
        logger.info(f"[API] ðŸ“– GET /api/subjects/{subject_id} - {len(contents)} contents")
        
        return {
            'success': True,
            'data': subject
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"[API] âŒ GET /api/subjects/{subject_id} - Error: {str(e)}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


@app.get('/api/categories', tags=["subjects"])
@limiter.limit("30/minute")
async def get_categories(request: Request):
    """Liste des catÃ©gories"""
    try:
        categories = db.get_categories()
        
        logger.info(f"[API] ðŸ“‚ GET /api/categories - {len(categories)} catÃ©gories")
        
        return {
            'success': True,
            'data': categories
        }
    except Exception as e:
        logger.error(f"[API] âŒ GET /api/categories - Error: {str(e)}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


@app.get('/api/popular', tags=["subjects"])
@limiter.limit("30/minute")
async def get_popular_subjects(
    request: Request,
    limit: int = Query(10, ge=1, le=50)
):
    """Ã‰preuves populaires"""
    try:
        subjects = db.get_popular_subjects(limit)
        
        logger.info(f"[API] â­ GET /api/popular - {len(subjects)} Ã©preuves")
        
        return {
            'success': True,
            'data': subjects
        }
    except Exception as e:
        logger.error(f"[API] âŒ GET /api/popular - Error: {str(e)}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


# ==================== RECOMMENDATIONS ==================
@app.get('/api/recommendations/{subject_id}', response_model=RecommendationsListResponse, tags=["recommendations"])
@limiter.limit("20/minute")
async def get_recommendations(
    request: Request,
    subject_id: int,
    limit: int = Query(5, ge=1, le=20),
    user: UserTokenData = Depends(verify_token)
):
    """Recommandations basÃ©es sur un sujet"""
    try:
        similar_ids = recommender.get_similar_subjects(subject_id, limit)
        
        if not similar_ids:
            logger.info(f"[API] ðŸ”„ GET /api/recommendations/{subject_id} - No recommendations")
            return {
                'success': True,
                'count': 0,
                'recommendations': [],
                'data_source': 'content_based'
            }
        
        # RÃ©cupÃ©rer les dÃ©tails des Ã©preuves similaires
        session = db.SessionLocal()
        try:
            subjects = session.query(Subject).filter(
                Subject.Id.in_(similar_ids),
                Subject.IsPublished == True,
                Subject.IsDeleted == False
            ).all()
            
            recommendations = [{
                'id': s.Id,
                'title': s.Title,
                'description': s.Description,
                'category': s.Category,
                'price': float(s.Price),
                'average_rating': float(s.AverageRating),
                'enrollment_count': s.EnrollmentCount
            } for s in subjects]
            
            logger.info(f"[API] ðŸ”„ GET /api/recommendations/{subject_id} - {len(recommendations)} recommandations")
            
            return {
                'success': True,
                'count': len(recommendations),
                'recommendations': recommendations,
                'data_source': 'content_based',
            }
        finally:
            session.close()
    except Exception as e:
        logger.error(f"[API] âŒ GET /api/recommendations/{subject_id} - Error: {str(e)}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


# ==================== NLP ANALYSIS ==================
@app.post('/api/analyze', response_model=AnalysisResponse, tags=["nlp"])
@limiter.limit("20/minute")
async def analyze_content(
    request: Request,
    analysis_request: AnalysisRequest,
    user: UserTokenData = Depends(verify_token)
):
    """Analyse NLP d'un contenu"""
    try:
        analysis = nlp_analyzer.analyze(
            analysis_request.text,
            analysis_request.metadata
        )
        
        logger.info(f"[API] ðŸ” POST /api/analyze - Analysis completed for user {user.user_id}")
        
        return analysis
    except Exception as e:
        logger.error(f"[API] âŒ POST /api/analyze - Error: {str(e)}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


# ==================== IA ENDPOINTS (pour .NET avec VRAIES DONNÃ‰ES) ==================

@app.post('/api/recommend', tags=["recommendations"])
@limiter.limit("20/minute")
async def recommend_subjects(
    request: Request,
    user_id: int,
    limit: int = Query(5, ge=1, le=20),
    current_user: UserTokenData = Depends(verify_token)
):
    """
    Recommandations personnalisÃ©es avec VRAIES donnÃ©es + modÃ¨les ML
    INPUT: {user_id, limit}
    OUTPUT: Recommandations basÃ©es sur performance rÃ©elle de l'utilisateur
    """
    try:
        logger.info(f"[API] ðŸ‘¤ POST /api/recommend - user_id={user_id} - RÃ©cupÃ©ration des recommandations")
        
        # Utiliser le service avec VRAIES donnÃ©es utilisateur
        recommendations = performance_analyzer.get_personalized_recommendations(user_id, limit)
        
        if not recommendations:
            logger.info(f"[API] âš ï¸ POST /api/recommend - Aucune recommandation pour user {user_id}")
            return {
                'success': True,
                'message': 'Pas de recommandation disponible en ce moment',
                'recommendations': [],
                'count': 0
            }
        
        logger.info(f"[API] âœ… POST /api/recommend - {len(recommendations)} recommandations basÃ©es sur donnÃ©es rÃ©elles")
        
        return {
            'success': True,
            'recommendations': recommendations,
            'count': len(recommendations),
            'data_source': 'real_user_history',
        }
    except Exception as e:
        logger.error(f"[API] âŒ POST /api/recommend - Error: {str(e)}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


@app.post('/api/analyze-progress', response_model=ProgressAnalysisResponse, tags=["analytics"])
@limiter.limit("20/minute")
async def analyze_progress(
    request: Request,
    user_id: int,
    current_user: UserTokenData = Depends(verify_token)
):
    """
    Analyse de progression avec VRAIES donnÃ©es de la BD
    INPUT: {user_id}
    OUTPUT: Analyse complÃ¨te basÃ©e sur Enrollments + LearningHistories rÃ©elles
    """
    try:
        logger.info(f"[API] ðŸ“ˆ POST /api/analyze-progress - user_id={user_id} - Analyse en cours")
        
        # Utiliser le service avec VRAIES donnÃ©es de progression
        analysis = performance_analyzer.analyze_user_progress(user_id)
        
        if not analysis.get('success', False):
            logger.warning(f"[API] âš ï¸ POST /api/analyze-progress - {analysis.get('message', 'Erreur inconnue')}")
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=analysis.get('message', 'Analysis not available')
            )
        
        logger.info(f"[API] âœ… POST /api/analyze-progress - Analyse complÃ¨tÃ©e pour user {user_id}")
        
        return {
            'success': True,
            'user_id': user_id,
            'overview': analysis.get('overview'),
            'analysis': analysis.get('analysis'),
            'recommendations': analysis.get('recommendations', []),
            'estimated_completion_date': analysis.get('estimated_completion_date')
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"[API] âŒ POST /api/analyze-progress - Error: {str(e)}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


# ==================== AI FEATURE ENDPOINTS ====================

@app.get('/api/learning-path/{user_id}', response_model=LearningPathResponse, tags=["ai"])
@limiter.limit("10/minute")
async def get_learning_path(
    request: Request,
    user_id: int,
    current_user: UserTokenData = Depends(verify_token)
):
    """
    Parcours d'apprentissage personnalisé basé sur les performances réelles.
    Temps de réponse typique : 1-3s (appels BD uniquement).
    """
    try:
        logger.info(f"[API] 🗺️ GET /api/learning-path/{user_id}")
        result = performance_analyzer.generate_learning_path(user_id)
        if not result.get('success'):
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=result.get('error', 'Données insuffisantes')
            )
        logger.info(f"[API] ✅ Learning path generated: {result.get('total_duration_days')} days")
        return result
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"[API] ❌ GET /api/learning-path/{user_id} - Error: {str(e)}")
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=str(e))


@app.post('/api/adaptive-quiz', response_model=AdaptiveQuizResponse, tags=["ai"])
@limiter.limit("5/minute")
async def generate_adaptive_quiz(
    request: Request,
    body: AdaptiveQuizRequest,
    current_user: UserTokenData = Depends(verify_token)
):
    """
    Génère un quiz adaptatif via DeepSeek, calibré sur les lacunes détectées.
    Temps de réponse typique : 8-20s (appel LLM).
    """
    try:
        logger.info(f"[API] 🧠 POST /api/adaptive-quiz user_id={body.user_id} subject={body.subject}")

        # Récupérer les lacunes réelles
        progress = performance_analyzer.analyze_user_progress(body.user_id)
        weak_areas: list = []
        if progress.get('success'):
            weak_areas = progress.get('analysis', {}).get('weak_areas', [])

        subject_label = body.subject.strip() or "les matières en cours"
        weak_context = f"lacunes en : {', '.join(weak_areas[:3])}" if weak_areas else "niveau intermédiaire"

        prompt = (
            f"Tu es un professeur expert en \"{subject_label}\".\n"
            f"Génère {body.count} questions QCM en français calibrées pour un étudiant avec des {weak_context}.\n\n"
            "Retourne UNIQUEMENT un tableau JSON valide (sans texte ni markdown):\n"
            "[\n"
            "  {\n"
            "    \"id\": 1,\n"
            "    \"question\": \"Énoncé de la question ?\",\n"
            "    \"options\": [\"Option A\", \"Option B\", \"Option C\", \"Option D\"],\n"
            "    \"correct_answer\": \"Texte exact de la bonne option\",\n"
            "    \"explanation\": \"Explication concise de la bonne réponse\",\n"
            "    \"difficulty\": \"moyen\",\n"
            "    \"topic\": \"sous-thème\"\n"
            "  }\n"
            "]"
        )

        deepseek_client = get_deepseek_client()
        result = deepseek_client.chat(
            messages=[{'role': 'user', 'content': prompt}],
            max_tokens=2500,
            temperature=0.6
        )

        content = result.get('content', '[]').strip()
        # Strip markdown code fences if present
        if content.startswith('```'):
            lines = content.split('\n')
            content = '\n'.join(lines[1:])
        if content.endswith('```'):
            content = content.rsplit('```', 1)[0].strip()

        questions = json.loads(content)

        logger.info(f"[API] ✅ Adaptive quiz generated: {len(questions)} questions")
        return {
            'success': True,
            'subject': subject_label,
            'weak_areas': weak_areas,
            'questions': questions,
            'count': len(questions),
            'generated_at': datetime.utcnow().isoformat()
        }
    except json.JSONDecodeError as e:
        logger.error(f"[API] ❌ JSON parse error in adaptive quiz: {str(e)}")
        raise HTTPException(status_code=500, detail='Erreur de génération des questions — réessayez.')
    except Exception as e:
        logger.error(f"[API] ❌ POST /api/adaptive-quiz - Error: {str(e)}")
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=str(e))


@app.post('/api/learning-style', response_model=LearningStyleResponse, tags=["ai"])
@limiter.limit("10/minute")
async def analyze_learning_style(
    request: Request,
    body: LearningStyleRequest,
    current_user: UserTokenData = Depends(verify_token)
):
    """
    Analyse le style d'apprentissage à partir des réponses VARK.
    Temps de réponse typique : <200ms (calcul local uniquement).
    """
    STYLE_META = {
        'V': {
            'key': 'visual',
            'label': 'Apprenant Visuel',
            'description': (
                "Tu retiens mieux l'information sous forme de schémas, graphiques, "
                "cartes mentales et couleurs. Ton cerveau encode les données visuellement "
                "et tu as une bonne mémoire photographique des pages et des slides."
            ),
            'tips': [
                "Utilise les schémas et illustrations du catalogue WinPlus en priorité",
                "Crée des fiches colorées pour chaque chapitre étudié",
                "Active les sous-titres et pause sur les tableaux dans les vidéos",
                "Dessine des cartes mentales pour relier les concepts entre eux",
            ]
        },
        'A': {
            'key': 'auditory',
            'label': 'Apprenant Auditif',
            'description': (
                "Tu assimiles mieux en écoutant et en parlant. Les explications orales, "
                "les discussions et la répétition à voix haute sont tes meilleurs alliés. "
                "Tu te souviens facilement de ce que tu as entendu."
            ),
            'tips': [
                "Priorise les cours en vidéo sur WinPlus plutôt que les PDFs",
                "Lis tes résumés à voix haute avant un examen",
                "Explique les notions à un ami — enseigner consolide ta mémoire",
                "Utilise WinAI pour te faire réexpliquer les concepts oralement",
            ]
        },
        'R': {
            'key': 'reading_writing',
            'label': 'Apprenant Lecteur/Scripteur',
            'description': (
                "Tu apprends par le texte : lire des cours structurés, prendre des notes "
                "détaillées, rédiger des résumés. Les listes, définitions et explications "
                "écrites te permettent de mémoriser efficacement."
            ),
            'tips': [
                "Télécharge tous les supports PDF disponibles sur WinPlus",
                "Rédige des résumés personnels après chaque chapitre",
                "Crée des listes de définitions et formules clés",
                "Demande à WinAI des explications écrites et structurées",
            ]
        },
        'K': {
            'key': 'kinesthetic',
            'label': 'Apprenant Kinesthésique',
            'description': (
                "Tu apprends par la pratique et l'expérimentation. Faire des exercices, "
                "résoudre des problèmes concrets et appliquer les notions directement "
                "est la méthode la plus efficace pour toi."
            ),
            'tips': [
                "Lance-toi directement sur les quiz et exercices pratiques de WinPlus",
                "Résous un maximum d'annales d'examens disponibles",
                "Demande à WinAI des problèmes d'application sur chaque notion",
                "Alterne courtes sessions de cours et exercices immédiats",
            ]
        },
    }

    scores: dict[str, int] = {'V': 0, 'A': 0, 'R': 0, 'K': 0}
    for ans in body.answers:
        tag = ans.style_tag.upper()
        if tag in scores:
            scores[tag] += 1

    dominant = max(scores, key=lambda k: scores[k])
    max_score = scores[dominant]
    # Mixed if two styles tied at max
    tied = [k for k, v in scores.items() if v == max_score]
    if len(tied) > 1:
        dominant = 'mixed'

    if dominant == 'mixed':
        return {
            'success': True,
            'style': 'mixed',
            'label': 'Apprenant Polyvalent',
            'description': (
                "Tu combines plusieurs styles d'apprentissage selon le contexte. "
                "Cette flexibilité est un atout : tu t'adaptes facilement aux différents "
                "formats de cours et peux alterner selon tes besoins du moment."
            ),
            'winplus_tips': [
                "Exploite tous les formats disponibles : vidéo, PDF, quiz et résumés",
                "Varie tes méthodes de révision selon la matière",
                "Utilise WinAI pour adapter les explications à ton humeur du moment",
                "Combine fiches visuelles et exercices pratiques pour consolider",
            ],
            'score_breakdown': scores
        }

    meta = STYLE_META[dominant]
    return {
        'success': True,
        'style': meta['key'],
        'label': meta['label'],
        'description': meta['description'],
        'winplus_tips': meta['tips'],
        'score_breakdown': scores
    }


@app.get('/api/success-prediction/{user_id}', response_model=SuccessPredictionResponse, tags=["ai"])
@limiter.limit("10/minute")
async def get_success_prediction(
    request: Request,
    user_id: int,
    current_user: UserTokenData = Depends(verify_token)
):
    """
    Prédit la probabilité de réussite basée sur les données réelles de progression.
    Temps de réponse typique : 2-4s (appels BD + calcul).
    """
    try:
        logger.info(f"[API] 🎯 GET /api/success-prediction/{user_id}")
        result = performance_analyzer.predict_success(user_id)
        if not result.get('success'):
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=result.get('error', 'Données insuffisantes pour la prédiction')
            )
        logger.info(f"[API] ✅ Success prediction: {result.get('probability')}% probability")
        return result
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"[API] ❌ GET /api/success-prediction/{user_id} - Error: {str(e)}")
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=str(e))


# ==================== ADMIN ENDPOINTS ==================
@app.post('/api/admin/init-db', tags=["admin"])
async def init_database(
    request: Request,
    admin_user: UserTokenData = Depends(require_role('admin'))
):
    """Initialiser la base de donnÃ©es (admin only)"""
    try:
        init_db()
        logger.info(f"[API] ðŸ—„ï¸ Database initialized by admin {admin_user.user_id}")
        return {
            'success': True,
            'message': 'Database initialized successfully'
        }
    except Exception as e:
        logger.error(f"[API] âŒ Database initialization failed: {str(e)}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


if __name__ == '__main__':
    import uvicorn
    uvicorn.run(
        app,
        host='0.0.0.0',
        port=int(os.getenv('PORT', 8000)),
        log_level=os.getenv('LOG_LEVEL', 'info')
    )
