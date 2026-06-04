#!/usr/bin/env python3
"""
Schémas Pydantic pour validation des requêtes/réponses FastAPI
"""

from pydantic import BaseModel, Field, EmailStr
from typing import Optional, List, Dict, Any
from datetime import datetime
from enum import Enum


# ==================== ENUMS ====================
class UserRole(str, Enum):
    student = "student"
    teacher = "teacher"
    admin = "admin"


class LearningStyle(str, Enum):
    visual = "visual"
    auditory = "auditory"
    reading_writing = "reading_writing"
    kinesthetic = "kinesthetic"


# ==================== SUBJECT & CONTENT SCHEMAS ====================
class CourseContentBase(BaseModel):
    title: str = Field(..., min_length=1, max_length=255)
    description: Optional[str] = Field(None, max_length=2000)
    video_url: Optional[str] = None
    document_url: Optional[str] = None
    order_index: int = Field(default=0, ge=0)
    duration_minutes: int = Field(default=0, ge=0)
    is_locked: bool = Field(default=True)


class CourseContentResponse(CourseContentBase):
    id: int
    subject_id: int
    created_at: datetime
    updated_at: Optional[datetime] = None

    class Config:
        from_attributes = True


class SubjectBase(BaseModel):
    title: str = Field(..., min_length=1, max_length=255)
    description: Optional[str] = Field(None, max_length=2000)
    category: Optional[str] = Field(None, max_length=100)
    thumbnail_url: Optional[str] = None
    price: float = Field(default=0, ge=0)
    is_published: bool = Field(default=False)
    is_featured: bool = Field(default=False)


class SubjectCreate(SubjectBase):
    pass


class SubjectUpdate(BaseModel):
    title: Optional[str] = Field(None, min_length=1, max_length=255)
    description: Optional[str] = Field(None, max_length=2000)
    category: Optional[str] = Field(None, max_length=100)
    thumbnail_url: Optional[str] = None
    price: Optional[float] = Field(None, ge=0)
    is_published: Optional[bool] = None
    is_featured: Optional[bool] = None


class SubjectResponse(SubjectBase):
    id: int
    enrollment_count: int
    average_rating: float
    total_ratings: int
    created_at: datetime
    updated_at: Optional[datetime] = None
    is_deleted: bool
    contents: Optional[List[CourseContentResponse]] = []

    class Config:
        from_attributes = True


# ==================== USER & ENROLLMENT SCHEMAS ====================
class UserBase(BaseModel):
    email: EmailStr
    first_name: Optional[str] = Field(None, max_length=100)
    last_name: Optional[str] = Field(None, max_length=100)
    role: UserRole = UserRole.student


class UserCreate(UserBase):
    password: str = Field(..., min_length=8)


class UserResponse(UserBase):
    id: int
    created_at: datetime

    class Config:
        from_attributes = True


class UserProfileResponse(UserResponse):
    education_level: Optional[str] = None
    grade: Optional[str] = None
    learning_style: Optional[LearningStyle] = None
    objectives: Optional[List[str]] = []
    enrolled_subjects: Optional[List[SubjectResponse]] = []


class EnrollmentBase(BaseModel):
    user_id: int
    subject_id: int
    progress_percentage: float = Field(default=0, ge=0, le=100)
    is_completed: bool = Field(default=False)


class EnrollmentResponse(EnrollmentBase):
    id: int
    enrolled_at: datetime
    completed_at: Optional[datetime] = None

    class Config:
        from_attributes = True


# ==================== ANALYSIS & NLP SCHEMAS ====================
class AnalysisRequest(BaseModel):
    text: str = Field(..., min_length=1)
    metadata: Optional[Dict[str, Any]] = None


class AnalysisResponse(BaseModel):
    text: str
    difficulty_level: str  # facile, moyen, difficile
    complexity_score: float
    estimated_duration_minutes: int
    key_concepts: List[str]
    vocabulary_complexity: Dict[str, Any]


class RecommendationRequest(BaseModel):
    user_id: int
    limit: int = Field(default=5, ge=1, le=20)


class RecommendationResponse(BaseModel):
    id: int
    title: str
    description: Optional[str]
    category: str
    price: float
    average_rating: float
    enrollment_count: int
    similarity_score: Optional[float] = None


class RecommendationsListResponse(BaseModel):
    success: bool
    count: int
    recommendations: List[RecommendationResponse]
    data_source: str = "content_based"
    models_used: List[str] = []


# ==================== PROGRESS ANALYSIS SCHEMAS ====================
class ProgressStatsResponse(BaseModel):
    total_enrolled_subjects: int
    completed_subjects: int
    completion_rate: float
    enrolled_days: int
    total_learning_time_hours: float
    average_session_duration_minutes: float


class ProgressAnalysisResponse(BaseModel):
    success: bool
    user_id: int
    overview: ProgressStatsResponse
    analysis: Dict[str, Any]
    recommendations: List[str]
    estimated_completion_date: Optional[str] = None


# ==================== CHATBOT SCHEMAS ====================
class AttachmentBase(BaseModel):
    type: str  # "image", "equation", "file", etc.
    data: Optional[str] = None
    file_name: Optional[str] = None


class ChatMessage(BaseModel):
    role: str = Field(..., pattern="^(user|assistant|system)$")
    content: str = Field(..., min_length=1)
    attachments: Optional[List[AttachmentBase]] = []


class ChatbotContextRequest(BaseModel):
    education_level: Optional[str] = None
    grade: Optional[str] = None
    enrolled_subjects: Optional[List[SubjectResponse]] = []
    objectives: Optional[List[str]] = []
    learning_style: Optional[str] = None


class ChatRequest(BaseModel):
    messages: List[ChatMessage]
    system_prompt: Optional[str] = None
    user_context: Optional[ChatbotContextRequest] = None
    max_tokens: int = Field(default=2000, ge=100, le=4000)
    temperature: float = Field(default=0.7, ge=0, le=1.0)


class ChatResponse(BaseModel):
    content: str
    tokens_used: int
    generation_time_ms: int
    model: str
    success: bool
    error: Optional[str] = None


# ==================== HEALTH CHECK SCHEMAS ====================
class HealthResponse(BaseModel):
    status: str
    service: str
    version: str
    database: str


class ChatbotHealthResponse(BaseModel):
    status: str
    service: str
    deepseek: Dict[str, Any]


# ==================== API RESPONSE SCHEMAS ====================
class ApiResponse(BaseModel):
    success: bool
    message: Optional[str] = None
    data: Optional[Dict[str, Any]] = None
    error: Optional[str] = None


class PaginatedResponse(BaseModel):
    success: bool
    count: int
    data: List[Dict[str, Any]]
