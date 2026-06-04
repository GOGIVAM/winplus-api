#!/usr/bin/env python3
"""
Système de recommandation hybride pour les Subjects (ASP.NET schema).
Combine : content-based + popularité + catégorie.
"""

import numpy as np
import pandas as pd
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.metrics.pairwise import cosine_similarity
import logging

logger = logging.getLogger(__name__)


class Recommender:
    def __init__(self, database):
        """
        Initialise le système de recommandation.
        
        Args:
            database: instance de la classe Database
        """
        self.db = database
        self.subjects_df = None
        self.subject_vectors = None
        self.vectorizer = TfidfVectorizer(max_features=100)
    
    def load_subjects(self):
        """Charge les épreuves depuis la table Subjects"""
        try:
            subjects = self.db.get_all_subjects()
            
            if not subjects:
                logger.warning("[Recommender] ⚠️ Aucune épreuve trouvée")
                self.subjects_df = pd.DataFrame()
                return
            
            # Conversion en DataFrame
            self.subjects_df = pd.DataFrame(subjects)
            
            # Vectorisation des descriptions + catégories
            text_features = (
                self.subjects_df['title'].fillna('') + ' ' + 
                self.subjects_df['description'].fillna('') + ' ' + 
                self.subjects_df['category'].fillna('')
            )
            self.subject_vectors = self.vectorizer.fit_transform(text_features)
            
            logger.info(f"[Recommender] ✅ {len(self.subjects_df)} sujets chargés")
        except Exception as e:
            logger.error(f"[Recommender] ❌ Erreur lors du chargement des sujets: {e}")
            self.subjects_df = pd.DataFrame()
    
    def get_similar_subjects(self, subject_id: int, top_n: int = 5) -> list:
        """Recommande des épreuves similaires basées sur le contenu"""
        try:
            if self.subjects_df is None or self.subjects_df.empty:
                self.load_subjects()
            
            if self.subjects_df.empty:
                logger.warning(f"[Recommender] ⚠️ Aucun sujet similaire trouvé pour {subject_id}")
                return []
            
            # Trouver l'index du sujet
            subject_idx = self.subjects_df[self.subjects_df['id'] == subject_id].index
            if len(subject_idx) == 0:
                logger.warning(f"[Recommender] ⚠️ Sujet {subject_id} introuvable")
                return []
            
            subject_idx = subject_idx[0]
            
            # Calculer les similarités cosinus
            similarities = cosine_similarity(
                self.subject_vectors[subject_idx:subject_idx+1],
                self.subject_vectors
            ).flatten()
            
            # Top N (exclure le sujet lui-même)
            similar_indices = similarities.argsort()[::-1][1:top_n+1]
            
            result = self.subjects_df.iloc[similar_indices]['id'].tolist()
            logger.info(f"[Recommender] 🔄 {len(result)} sujets similaires trouvés pour {subject_id}")
            return result
        except Exception as e:
            logger.error(f"[Recommender] ❌ Erreur get_similar_subjects: {e}")
            return []
    
    def get_popular_subjects(self, category: str = None, limit: int = 10) -> list:
        """Retourne les épreuves populaires"""
        try:
            popular = self.db.get_popular_subjects(limit)
            
            if category:
                popular = [s for s in popular if s.get('category') == category]
            
            result = [s['id'] for s in popular[:limit]]
            logger.info(f"[Recommender] ⭐ {len(result)} sujets populaires trouvés")
            return result
        except Exception as e:
            logger.error(f"[Recommender] ❌ Erreur get_popular_subjects: {e}")
            return []
    
    def get_personalized_recommendations(self, user_id: int, limit: int = 10) -> list:
        """Recommandations personnalisées basées sur l'historique"""
        try:
            # TODO: Intégrer avec la table Enrollments pour l'historique utilisateur
            # Pour l'instant, retourne les épreuves populaires
            result = self.get_popular_subjects(limit=limit)
            logger.info(f"[Recommender] 👤 {len(result)} recommandations personnalisées pour user {user_id}")
            return result
        except Exception as e:
            logger.error(f"[Recommender] ❌ Erreur get_personalized_recommendations: {e}")
            return []
    
    def recommend_by_category(self, category: str, limit: int = 10) -> list:
        """Recommande des épreuves d'une catégorie spécifique"""
        try:
            if self.subjects_df is None or self.subjects_df.empty:
                self.load_subjects()
            
            category_subjects = self.subjects_df[
                self.subjects_df['category'] == category
            ].sort_values('enrollmentCount', ascending=False)
            
            result = category_subjects['id'].head(limit).tolist()
            logger.info(f"[Recommender] 📂 {len(result)} sujets trouvés pour catégorie '{category}'")
            return result
        except Exception as e:
            logger.error(f"[Recommender] ❌ Erreur recommend_by_category: {e}")
            return []
    
    def get_trending_subjects(self, limit: int = 10) -> list:
        """Retourne les sujets en tendance (trending)"""
        try:
            if self.subjects_df is None or self.subjects_df.empty:
                self.load_subjects()
            
            trending = self.subjects_df.nlargest(limit, 'enrollmentCount')
            result = trending['id'].tolist()
            logger.info(f"[Recommender] 🔥 {len(result)} sujets en tendance")
            return result
        except Exception as e:
            logger.error(f"[Recommender] ❌ Erreur get_trending_subjects: {e}")
            return []


if __name__ == "__main__":
    # Tests unitaires
    from database import Database
    
    logging.basicConfig(level=logging.INFO)
    
    db = Database()
    recommender = Recommender(db)
    
    # Test chargement des sujets
    print("Chargement des sujets:")
    recommender.load_subjects()
    
    # Test recommandations similaires pour subject_id=1
    if not recommender.subjects_df.empty:
        print("\nSujets similaires à subject_id=1:")
        similar = recommender.get_similar_subjects(subject_id=1, top_n=5)
        print(f"  IDs trouvés: {similar}")
        
        # Test sujets populaires
        print("\nSujets populaires:")
        popular = recommender.get_popular_subjects(limit=5)
        print(f"  IDs trouvés: {popular}")
        
        # Test sujets en tendance
        print("\nSujets en tendance:")
        trending = recommender.get_trending_subjects(limit=5)
        print(f"  IDs trouvés: {trending}")