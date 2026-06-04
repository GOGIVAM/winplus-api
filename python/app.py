#!/usr/bin/env python3
"""
Service IA FastAPI avec authentification
Utilise les tables ASP.NET (Subjects, CourseContents)
"""

from fastapi import FastAPI, Query, HTTPException, Depends, status
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
from schemas import (
    HealthResponse, SubjectResponse, RecommendationResponse, 
    RecommendationsListResponse, AnalysisRequest, AnalysisResponse,
    ProgressAnalysisResponse, ApiResponse, PaginatedResponse
)

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


# ==================== HEALTH CHECK (Public) ====================
@app.get('/health', response_model=HealthResponse, tags=["health"])
@limiter.limit("30/minute")
async def health(request):
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
    request,
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
async def get_subject_detail(request, subject_id: int):
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
async def get_categories(request):
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
    request,
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
    request,
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
                'models_used': ['recommender.py (TF-IDF)']
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
    request,
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
    request,
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
            'models_used': ['recommender.py (TF-IDF)', 'nlp_analyzer.py (CamemBERT)']
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
    request,
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


# ==================== ADMIN ENDPOINTS ==================
@app.post('/api/admin/init-db', tags=["admin"])
async def init_database(
    request,
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
