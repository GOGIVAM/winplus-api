# database.py
# Connexion et gestion de la base de données pour le service FastApi
#!/usr/bin/env python3
"""
Module de gestion de la base de données.
Utilise SQLAlchemy ORM pour PostgreSQL avec les schémas ASP.NET.
"""

import os
from sqlalchemy import create_engine, Column, Integer, String, Boolean, Numeric, DateTime, Text, ForeignKey, func, and_, or_, desc
from sqlalchemy.ext.declarative import declarative_base
from sqlalchemy.orm import sessionmaker, relationship
from datetime import datetime
import pandas as pd
import logging

logger = logging.getLogger(__name__)

# Base SQLAlchemy
Base = declarative_base()

# ================== MODÈLES MAPPÉS SUR LES TABLES ASP.NET ==================

class Subject(Base):
    """Modèle FastApi mappé sur la table Subjects (ASP.NET)"""
    __tablename__ = 'Subjects'
    
    Id = Column(Integer, primary_key=True)
    Title = Column(String(255), nullable=False)
    Description = Column(String(2000))
    Category = Column(String(100))
    ThumbnailUrl = Column(Text)
    Price = Column(Numeric, nullable=False, default=0)
    IsPublished = Column(Boolean, nullable=False, default=False)
    EnrollmentCount = Column(Integer, nullable=False, default=0)
    AverageRating = Column(Numeric, nullable=False, default=0)
    TotalRatings = Column(Integer, nullable=False, default=0)
    CreatedAt = Column(DateTime(timezone=True), nullable=False, default=datetime.utcnow)
    UpdatedAt = Column(DateTime(timezone=True))
    IsDeleted = Column(Boolean, nullable=False, default=False)
    IsFeatured = Column(Boolean, nullable=False, default=False)
    
    # Relation avec les contenus
    contents = relationship("CourseContent", back_populates="subject", foreign_keys="CourseContent.SubjectId")


class CourseContent(Base):
    """Modèle FastApi mappé sur la table CourseContents (ASP.NET)"""
    __tablename__ = 'CourseContents'
    
    Id = Column(Integer, primary_key=True)
    SubjectId = Column(Integer, ForeignKey('Subjects.Id'), nullable=False)
    Title = Column(String(255), nullable=False)
    Description = Column(String(2000))
    VideoUrl = Column(Text)
    DocumentUrl = Column(Text)
    OrderIndex = Column(Integer, nullable=False, default=0)
    DurationMinutes = Column(Integer, nullable=False, default=0)
    IsLocked = Column(Boolean, nullable=False, default=True)
    CreatedAt = Column(DateTime(timezone=True), nullable=False, default=datetime.utcnow)
    UpdatedAt = Column(DateTime(timezone=True))
    
    # Relation inverse avec Subject
    subject = relationship("Subject", back_populates="contents", foreign_keys=[SubjectId])


class User(Base):
    """Modèle FastApi mappé sur la table Users (ASP.NET)"""
    __tablename__ = 'Users'
    
    Id = Column(Integer, primary_key=True)
    Email = Column(String(255), unique=True, nullable=False)
    FirstName = Column(String(100))
    LastName = Column(String(100))
    Role = Column(String(50))
    CreatedAt = Column(DateTime(timezone=True), nullable=False, default=datetime.utcnow)


class Enrollment(Base):
    """Modèle FastApi mappé sur la table Enrollments (ASP.NET)"""
    __tablename__ = 'Enrollments'
    
    Id = Column(Integer, primary_key=True)
    UserId = Column(Integer, ForeignKey('Users.Id'), nullable=False)
    SubjectId = Column(Integer, ForeignKey('Subjects.Id'), nullable=False)
    EnrolledAt = Column(DateTime(timezone=True), nullable=False, default=datetime.utcnow)
    CompletedAt = Column(DateTime(timezone=True))
    ProgressPercentage = Column(Numeric, nullable=False, default=0)
    IsCompleted = Column(Boolean, nullable=False, default=False)
    IsDeleted = Column(Boolean, nullable=False, default=False)


class LearningHistory(Base):
    """Modèle FastApi mappé sur la table LearningHistories (ASP.NET)"""
    __tablename__ = 'LearningHistories'

    Id = Column(Integer, primary_key=True)
    UserId = Column(Integer, ForeignKey('Users.Id'), nullable=False)
    SubjectId = Column(Integer, ForeignKey('Subjects.Id'), nullable=False)
    ContentId = Column(Integer, ForeignKey('CourseContents.Id'))
    ActionType = Column(String(50))  # 'view', 'complete', 'quiz_attempt', etc.
    Duration = Column(Integer)  # seconds
    Timestamp = Column(DateTime(timezone=True), nullable=False, default=datetime.utcnow)
    Metadata = Column(Text)  # JSON metadata


class Conversation(Base):
    """Modèle FastApi mappé sur la table Conversations (ASP.NET chatbot)"""
    __tablename__ = 'Conversations'

    Id = Column(Integer, primary_key=True)
    UserId = Column(Integer, ForeignKey('Users.Id'), nullable=False)
    Title = Column(String(255), nullable=False, default='Nouvelle conversation')
    Tags = Column(Text)
    IsActive = Column(Boolean, nullable=False, default=True)
    LastMessageAt = Column(DateTime(timezone=True))
    MessageCount = Column(Integer, nullable=False, default=0)
    CreatedAt = Column(DateTime(timezone=True), nullable=False, default=datetime.utcnow)
    UpdatedAt = Column(DateTime(timezone=True), nullable=False, default=datetime.utcnow)
    IsDeleted = Column(Boolean, nullable=False, default=False)

    messages = relationship("ChatMessage", back_populates="conversation")


class ChatMessage(Base):
    """Modèle FastApi mappé sur la table Messages (ASP.NET chatbot)"""
    __tablename__ = 'Messages'

    Id = Column(Integer, primary_key=True)
    ConversationId = Column(Integer, ForeignKey('Conversations.Id'), nullable=False)
    Role = Column(String(20), nullable=False)
    Content = Column(Text, nullable=False)
    TokensUsed = Column(Integer)
    GenerationTimeMs = Column(Integer)
    CreatedAt = Column(DateTime(timezone=True), nullable=False, default=datetime.utcnow)
    IsDeleted = Column(Boolean, nullable=False, default=False)

    conversation = relationship("Conversation", back_populates="messages")


# ================== INITIALISATION DB ==================

class Database:
    def __init__(self):
        """Initialise la connexion à la base de données."""
        # Configuration PostgreSQL
        user = os.getenv('DB_USER', 'gogivam')
        password = os.getenv('DB_PASSWORD', 'Admin001')
        host = os.getenv('DB_HOST', '172.31.20.230')
        port = os.getenv('DB_PORT', '5432')
        database = os.getenv('DB_NAME', 'winplus_db')
        
        connection_string = f"postgresql://{user}:{password}@{host}:{port}/{database}"
        
        self.engine = create_engine(connection_string, echo=False)
        self.SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=self.engine)
        logger.info(f"[Database] ✅ Connecté à PostgreSQL: {database}")
    
    # ================== MÉTHODES SUBJECTS ==================
    def get_subject_by_id(self, subject_id: int) -> dict:
        """Récupère une épreuve par son ID"""
        session = self.SessionLocal()
        try:
            subject = session.query(Subject).filter(
                and_(
                    Subject.Id == subject_id,
                    Subject.IsPublished == True,
                    Subject.IsDeleted == False
                )
            ).first()
            
            if not subject:
                return None
            
            return {
                'id': subject.Id,
                'title': subject.Title,
                'description': subject.Description,
                'category': subject.Category,
                'thumbnailUrl': subject.ThumbnailUrl,
                'price': float(subject.Price),
                'averageRating': float(subject.AverageRating),
                'totalRatings': subject.TotalRatings,
                'enrollmentCount': subject.EnrollmentCount,
                'isFeatured': subject.IsFeatured
            }
        finally:
            session.close()
    
    def get_all_subjects(self, filters: dict = None) -> list:
        """Récupère toutes les épreuves avec filtres optionnels"""
        session = self.SessionLocal()
        try:
            query = session.query(Subject).filter(
                and_(
                    Subject.IsPublished == True,
                    Subject.IsDeleted == False
                )
            )
            
            if filters:
                if 'category' in filters:
                    query = query.filter(Subject.Category == filters['category'])
                if 'search' in filters:
                    search_pattern = f"%{filters['search']}%"
                    query = query.filter(
                        or_(
                            Subject.Title.ilike(search_pattern),
                            Subject.Description.ilike(search_pattern)
                        )
                    )
                if 'featured' in filters and filters['featured']:
                    query = query.filter(Subject.IsFeatured == True)
            
            subjects = query.all()
            
            return [{
                'id': s.Id,
                'title': s.Title,
                'description': s.Description,
                'category': s.Category,
                'thumbnailUrl': s.ThumbnailUrl,
                'price': float(s.Price),
                'averageRating': float(s.AverageRating),
                'totalRatings': s.TotalRatings,
                'enrollmentCount': s.EnrollmentCount,
                'isFeatured': s.IsFeatured
            } for s in subjects]
        finally:
            session.close()
    
    def get_subjects_by_category(self, category: str) -> list:
        """Récupère les épreuves d'une catégorie"""
        return self.get_all_subjects({'category': category})
    
    def get_popular_subjects(self, limit: int = 10) -> list:
        """Récupère les épreuves populaires"""
        session = self.SessionLocal()
        try:
            subjects = session.query(Subject).filter(
                and_(
                    Subject.IsPublished == True,
                    Subject.IsDeleted == False
                )
            ).order_by(
                desc(Subject.EnrollmentCount),
                desc(Subject.AverageRating)
            ).limit(limit).all()
            
            return [{
                'id': s.Id,
                'title': s.Title,
                'description': s.Description,
                'category': s.Category,
                'price': float(s.Price),
                'enrollmentCount': s.EnrollmentCount,
                'averageRating': float(s.AverageRating)
            } for s in subjects]
        finally:
            session.close()
    
    # ================== MÉTHODES COURSE CONTENTS ==================
    def get_course_contents(self, subject_id: int) -> list:
        """Récupère les contenus d'une épreuve"""
        session = self.SessionLocal()
        try:
            contents = session.query(CourseContent).filter(
                CourseContent.SubjectId == subject_id
            ).order_by(CourseContent.OrderIndex).all()
            
            return [{
                'id': c.Id,
                'title': c.Title,
                'description': c.Description,
                'videoUrl': c.VideoUrl,
                'documentUrl': c.DocumentUrl,
                'orderIndex': c.OrderIndex,
                'durationMinutes': c.DurationMinutes,
                'isLocked': c.IsLocked
            } for c in contents]
        finally:
            session.close()
    
    # ================== MÉTHODES CATÉGORIES ==================
    def get_categories(self) -> list:
        """Récupère toutes les catégories uniques"""
        session = self.SessionLocal()
        try:
            categories = session.query(
                Subject.Category,
                func.count(Subject.Id).label('count')
            ).filter(
                and_(
                    Subject.Category.isnot(None),
                    Subject.IsPublished == True,
                    Subject.IsDeleted == False
                )
            ).group_by(Subject.Category).all()
            
            return [{
                'name': cat.Category,
                'count': cat.count
            } for cat in categories]
        finally:
            session.close()
    
    # ================== MÉTHODES UTILISATEURS & PROGRESSION ==================
    def get_user_enrollments(self, user_id: int) -> list:
        """Récupère tous les enrollments d'un utilisateur avec données complètes"""
        session = self.SessionLocal()
        try:
            enrollments = session.query(
                Enrollment,
                Subject.Title,
                Subject.Category,
                Subject.Description,
                func.count(CourseContent.Id).label('total_contents')
            ).join(Subject, Enrollment.SubjectId == Subject.Id)\
            .outerjoin(CourseContent, Subject.Id == CourseContent.SubjectId)\
            .filter(
                and_(
                    Enrollment.UserId == user_id,
                    Enrollment.IsDeleted == False
                )
            ).group_by(
                Enrollment.Id,
                Subject.Id
            ).all()
            
            result = []
            for e in enrollments:
                result.append({
                    'enrollment_id': e.Enrollment.Id,
                    'subject_id': e.Enrollment.SubjectId,
                    'subject_title': e.Title,
                    'subject_category': e.Category,
                    'subject_description': e.Description,
                    'progress_percentage': float(e.Enrollment.ProgressPercentage or 0),
                    'is_completed': e.Enrollment.IsCompleted,
                    'enrolled_at': e.Enrollment.EnrolledAt.isoformat() if e.Enrollment.EnrolledAt else None,
                    'completed_at': e.Enrollment.CompletedAt.isoformat() if e.Enrollment.CompletedAt else None,
                    'total_contents': e.total_contents or 0
                })
            return result
        finally:
            session.close()
    
    def get_user_learning_history(self, user_id: int, limit: int = 100) -> list:
        """Récupère l'historique d'apprentissage d'un utilisateur"""
        session = self.SessionLocal()
        try:
            histories = session.query(
                LearningHistory,
                Subject.Title,
                CourseContent.Title
            ).outerjoin(Subject, LearningHistory.SubjectId == Subject.Id)\
            .outerjoin(CourseContent, LearningHistory.ContentId == CourseContent.Id)\
            .filter(LearningHistory.UserId == user_id)\
            .order_by(desc(LearningHistory.Timestamp))\
            .limit(limit).all()
            
            result = []
            for h in histories:
                result.append({
                    'id': h.LearningHistory.Id,
                    'subject_id': h.LearningHistory.SubjectId,
                    'subject_title': h.Title,
                    'content_id': h.LearningHistory.ContentId,
                    'content_title': h.LearningHistory.__dict__.get('content_title'),
                    'action_type': h.LearningHistory.ActionType,
                    'duration_seconds': h.LearningHistory.Duration or 0,
                    'timestamp': h.LearningHistory.Timestamp.isoformat()
                })
            return result
        finally:
            session.close()
    
    def get_user_progress_stats(self, user_id: int) -> dict:
        """Calcule les statistiques de progression d'un utilisateur"""
        session = self.SessionLocal()
        try:
            # Stats des enrollments
            enrollments = session.query(Enrollment).filter(
                and_(
                    Enrollment.UserId == user_id,
                    Enrollment.IsDeleted == False
                )
            ).all()
            
            if not enrollments:
                return {
                    'total_enrolled_subjects': 0,
                    'completed_subjects': 0,
                    'average_progress': 0.0,
                    'total_learning_time_minutes': 0,
                    'learning_frequency': 0,
                    'strengths': [],
                    'weak_areas': []
                }
            
            total_enrolled = len(enrollments)
            completed = sum(1 for e in enrollments if e.IsCompleted)
            avg_progress = sum(float(e.ProgressPercentage or 0) for e in enrollments) / total_enrolled if total_enrolled > 0 else 0
            
            # Stats de l'historique d'apprentissage
            learning_history = session.query(LearningHistory).filter(
                LearningHistory.UserId == user_id
            ).all()
            
            total_learning_time = sum(h.Duration or 0 for h in learning_history) // 60  # en minutes
            learning_frequency = len(learning_history)
            
            # Sujets avec la meilleure progression (forces)
            top_subjects = sorted(
                enrollments,
                key=lambda e: float(e.ProgressPercentage or 0),
                reverse=True
            )[:3]
            
            strengths = [
                session.query(Subject).get(e.SubjectId).Title 
                for e in top_subjects 
                if session.query(Subject).get(e.SubjectId)
            ]
            
            # Sujets avec la moins bonne progression (faiblesses)
            weak_subjects = sorted(
                enrollments,
                key=lambda e: float(e.ProgressPercentage or 0)
            )[:3]
            
            weak_areas = [
                session.query(Subject).get(e.SubjectId).Title 
                for e in weak_subjects 
                if float(e.ProgressPercentage or 0) < 80
                and session.query(Subject).get(e.SubjectId)
            ]
            
            return {
                'total_enrolled_subjects': total_enrolled,
                'completed_subjects': completed,
                'average_progress': round(avg_progress, 2),
                'total_learning_time_minutes': total_learning_time,
                'learning_frequency': learning_frequency,
                'strengths': strengths,
                'weak_areas': weak_areas
            }
        finally:
            session.close()
    
    def get_subject_completion_data(self, subject_id: int) -> dict:
        """Récupère les données de complétion d'un sujet"""
        session = self.SessionLocal()
        try:
            subject = session.query(Subject).get(subject_id)
            if not subject:
                return None
            
            enrollments = session.query(Enrollment).filter(
                and_(
                    Enrollment.SubjectId == subject_id,
                    Enrollment.IsDeleted == False
                )
            ).all()
            
            total_enrollments = len(enrollments)
            completions = sum(1 for e in enrollments if e.IsCompleted)
            avg_progress = sum(float(e.ProgressPercentage or 0) for e in enrollments) / total_enrollments if total_enrollments > 0 else 0
            
            return {
                'subject_id': subject_id,
                'subject_title': subject.Title,
                'total_enrollments': total_enrollments,
                'completions': completions,
                'completion_rate': round(completions / total_enrollments * 100 if total_enrollments > 0 else 0, 2),
                'average_progress': round(avg_progress, 2),
                'category': subject.Category
            }
        finally:
            session.close()
    
    # ================== UTILITY ==================
    def get_session(self):
        """Retourne une session SQLAlchemy"""
        return self.SessionLocal()


def get_db():
    """Dépendance pour obtenir une session DB"""
    db = Database()
    session = db.get_session()
    try:
        yield session
    finally:
        session.close()


def init_db():
    """Initialise les tables (migrations)"""
    Base.metadata.create_all(bind=Database().engine)
    logger.info("[Database] ✅ Tables initialisées")


if __name__ == "__main__":
    # Test de connexion
    logging.basicConfig(level=logging.INFO)
    init_db()