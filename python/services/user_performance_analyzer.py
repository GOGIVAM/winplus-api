#!/usr/bin/env python3
"""
Service d'analyse de performance utilisateur intégrant les vrais données et modèles ML.
Utilise la BD pour extraire les vraies données d'utilisation et les passe aux modèles.
"""

import logging
from datetime import datetime, timedelta
from typing import Dict, List, Any
from database import Database
from models.nlp_analyzer import NLPAnalyzer
from models.recommender import RecommendationEngine

logger = logging.getLogger(__name__)


class UserPerformanceAnalyzer:
    """Analyse la vraie performance utilisateur avec modèles ML et données BD réelles"""
    
    def __init__(self):
        """Initialise l'analyseur avec les modèles"""
        self.db = Database()
        self.nlp_analyzer = NLPAnalyzer()
        self.recommender = RecommendationEngine()
        logger.info("[UserPerformanceAnalyzer] ✅ Initialisé avec NLP + Recommender")
    
    def analyze_user_progress(self, user_id: int) -> Dict[str, Any]:
        """
        Analyse complète de la progression réelle d'un utilisateur
        INPUT: Vraies données de BD (Enrollments, LearningHistories)
        OUTPUT: Analyse basée sur modèles
        """
        try:
            # 1. Récupérer les vraies données de progression
            stats = self.db.get_user_progress_stats(user_id)
            enrollments = self.db.get_user_enrollments(user_id)
            learning_history = self.db.get_user_learning_history(user_id, limit=50)
            
            if not enrollments:
                return {
                    'success': False,
                    'message': 'Aucun enrollment trouvé pour cet utilisateur',
                    'user_id': user_id
                }
            
            # 2. Calculer les métriques réelles
            completion_rate = stats['average_progress']
            learning_days = self._calculate_learning_days(learning_history)
            avg_session_duration = self._calculate_avg_session_duration(learning_history)
            learning_velocity = self._calculate_learning_velocity(learning_history)
            
            # 3. Identifier les domaines forts via analyse NLP
            strong_subjects = stats['strengths']
            weak_areas = stats['weak_areas']
            
            # 4. Analyser le contenu des sujets forts avec NLP
            subject_analyses = {}
            for subject in enrollments[:5]:  # Analyser top 5 sujets
                if subject['subject_description']:
                    nlp_result = self.nlp_analyzer.analyze(
                        subject['subject_description'],
                        {'subject_id': subject['subject_id'], 'category': subject['subject_category']}
                    )
                    subject_analyses[subject['subject_id']] = {
                        'title': subject['subject_title'],
                        'difficulty': nlp_result['difficulty_level'],
                        'estimated_duration': nlp_result['estimated_duration_minutes'],
                        'complexity_score': nlp_result['complexity_score']
                    }
            
            # 5. Calculer la date estimée de complétion
            estimated_completion_date = self._estimate_completion_date(
                stats['completed_subjects'],
                stats['total_enrolled_subjects'],
                completion_rate,
                learning_velocity
            )
            
            # 6. Générrer recommandations d'actions
            recommendations = self._generate_recommendations(
                stats,
                completion_rate,
                learning_velocity,
                weak_areas
            )
            
            analysis = {
                'success': True,
                'user_id': user_id,
                'overview': {
                    'total_enrolled_subjects': stats['total_enrolled_subjects'],
                    'completed_subjects': stats['completed_subjects'],
                    'completion_rate': round(completion_rate, 1),  # pourcentage
                    'enrolled_days': learning_days,
                    'total_learning_time_hours': round(stats['total_learning_time_minutes'] / 60, 1),
                    'average_session_duration_minutes': round(avg_session_duration, 1)
                },
                'analysis': {
                    'learning_velocity': round(learning_velocity, 2),  # % par jour
                    'strengths': strong_subjects,
                    'weak_areas': weak_areas,
                    'subject_difficulty_analysis': subject_analyses,
                    'learning_pattern': self._analyze_learning_pattern(learning_history)
                },
                'projections': {
                    'estimated_completion_date': estimated_completion_date,
                    'estimated_remaining_days': self._calculate_remaining_days(estimated_completion_date),
                    'recommended_daily_target': round(100 / (learning_days + 1) if learning_days >= 0 else 5, 1)  # % par jour
                },
                'recommendations': recommendations,
                'timestamp': datetime.utcnow().isoformat()
            }
            
            logger.info(f"[UserPerformanceAnalyzer] ✅ Analyse complète pour user {user_id}: {completion_rate}%")
            return analysis
            
        except Exception as e:
            logger.error(f"[UserPerformanceAnalyzer] ❌ Erreur d'analyse: {str(e)}")
            return {
                'success': False,
                'error': str(e),
                'user_id': user_id
            }
    
    def get_personalized_recommendations(self, user_id: int, limit: int = 5) -> List[Dict[str, Any]]:
        """
        Recommandations personnalisées via le ReccommendationEngine
        Utilise le vrai historique utilisateur de la BD
        """
        try:
            # 1. Obtenir les enrollments actuels
            enrollments = self.db.get_user_enrollments(user_id)
            enrolled_subject_ids = [e['subject_id'] for e in enrollments]
            
            # 2. Obtenir tous les sujets
            all_subjects = self.db.get_all_subjects()
            
            # 3. Filtrer sujects non-enrolled
            available_subjects = [s for s in all_subjects if s['id'] not in enrolled_subject_ids]
            
            if not available_subjects:
                logger.info(f"[UserPerformanceAnalyzer] ⚠️ Aucun sujet disponible pour user {user_id}")
                return []
            
            # 4. Obtenir les catégories de l'utilisateur (forces)
            user_categories = {}
            for e in enrollments:
                cat = e['subject_category']
                if cat:
                    user_categories[cat] = user_categories.get(cat, 0) + 1
            
            # 5. Utiliser le recommender avec données réelles
            recommendations = []
            for subject in available_subjects[:limit * 2]:  # Traiter 2x pour filtrer
                # Calculer score de similarité via le recommender
                similar_score = self.recommender.get_similarity_score(
                    subject_id=subject['id'],
                    category=subject.get('category'),
                    user_categories=user_categories
                ) if hasattr(self.recommender, 'get_similarity_score') else subject.get('averageRating', 0)
                
                # Analyser la description du sujet
                nlp_analysis = self.nlp_analyzer.analyze(
                    subject.get('description', ''),
                    {'subject_id': subject['id'], 'category': subject.get('category')'}
                )
                
                # Top N recommandations
                recommendations.append({
                    'subject_id': subject['id'],
                    'title': subject['title'],
                    'category': subject.get('category'),
                    'description': subject.get('description'),
                    'price': subject.get('price', 0),
                    'rating': subject.get('averageRating', 0),
                    'enrollment_count': subject.get('enrollmentCount', 0),
                    'difficulty_level': nlp_analysis['difficulty_level'],
                    'score': round(similar_score, 2)
                })
            
            # Trier par score
            recommendations.sort(key=lambda x: x['score'], reverse=True)
            
            logger.info(f"[UserPerformanceAnalyzer] ✅ {len(recommendations[:limit])} recommandations pour user {user_id}")
            return recommendations[:limit]
            
        except Exception as e:
            logger.error(f"[UserPerformanceAnalyzer] ❌ Erreur recommandations: {str(e)}")
            return []
    
    def generate_learning_path(self, user_id: int) -> Dict[str, Any]:
        """
        Génère un parcours d'apprentissage personnalisé basé sur les vraies données
        S'adapte à la velocité d'apprentissage réelle de l'utilisateur
        """
        try:
            # 1. Récupérer les données réelles
            stats = self.db.get_user_progress_stats(user_id)
            enrollments = self.db.get_user_enrollments(user_id)
            learning_history = self.db.get_user_learning_history(user_id, limit=100)
            
            if not enrollments:
                return {'success': False, 'error': 'Aucun enrollment trouvé'}
            
            # 2. Calculer la vélocité réelle d'apprentissage
            learning_velocity = self._calculate_learning_velocity(learning_history)
            avg_complexity = self._calculate_avg_complexity(enrollments)
            
            # 3. Déterminer les phases du parcours adapté
            phases = []
            
            # Phase 1: Renforcer les bases (sujets faibles)
            if stats['weak_areas']:
                phases.append({
                    'phase': 1,
                    'name': 'Phase de renforcement',
                    'duration_days': int(14 / (max(learning_velocity, 0.5))),  # Adapter à vélocité
                    'focus_areas': stats['weak_areas'][:2],
                    'difficulty': 'facile',
                    'target_completion': round(60, 1),  # % minimum
                    'actions': [
                        'Revoir les concepts fondamentaux',
                        f"Focus sur: {', '.join(stats['weak_areas'][:2])}",
                        'Compléter tous les quiz'
                    ]
                })
            
            # Phase 2: Approfondissement (zones de force)
            if stats['strengths']:
                phases.append({
                    'phase': 2,
                    'name': 'Phase d\'approfondissement',
                    'duration_days': int(21 / (max(learning_velocity, 0.5))),
                    'focus_areas': stats['strengths'][:2],
                    'difficulty': 'moyen',
                    'target_completion': round(85, 1),
                    'actions': [
                        'Aller plus loin dans vos domaines forts',
                        'Explorer des contenus avancés',
                        'Préparer des certifications'
                    ]
                })
            
            # Phase 3: Maîtrise et diversification
            phases.append({
                'phase': 3,
                'name': 'Phase de maîtrise',
                'duration_days': int(28 / (max(learning_velocity, 0.5))),
                'focus_areas': self._get_recommended_next_subjects(enrollments, stats),
                'difficulty': 'difficile',
                'target_completion': round(95, 1),
                'actions': [
                    'Maîtriser tous les sujets actuels',
                    'Diversifier vers de nouvelles compétences',
                    'Valider par des projets pratiques'
                ]
            })
            
            # 4. Calculer les jalons
            total_duration = sum(p['duration_days'] for p in phases)
            
            learning_path = {
                'success': True,
                'user_id': user_id,
                'learning_velocity': round(learning_velocity, 2),  # % par jour
                'total_duration_days': total_duration,
                'estimated_end_date': (datetime.utcnow() + timedelta(days=total_duration)).isoformat().split('T')[0],
                'phases': phases,
                'recommendations': {
                    'daily_study_time': self._recommend_study_time(learning_velocity),
                    'focus_areas': stats['weak_areas'],
                    'growth_areas': stats['strengths']
                },
                'generated_at': datetime.utcnow().isoformat()
            }
            
            logger.info(f"[UserPerformanceAnalyzer] ✅ Parcours généré pour user {user_id}: {total_duration} jours")
            return learning_path
            
        except Exception as e:
            logger.error(f"[UserPerformanceAnalyzer] ❌ Erreur génération parcours: {str(e)}")
            return {'success': False, 'error': str(e)}
    
    # ================== MÉTHODES UTILITAIRES ==================
    
    def _calculate_learning_days(self, learning_history: List[Dict]) -> int:
        """Calcule le nombre de jours d'apprentissage"""
        if not learning_history:
            return 0
        
        timestamps = [datetime.fromisoformat(h['timestamp'].replace('Z', '+00:00')) for h in learning_history if h['timestamp']]
        if not timestamps:
            return 0
        
        earliest = min(timestamps)
        latest = max(timestamps)
        return (latest - earliest).days + 1
    
    def _calculate_avg_session_duration(self, learning_history: List[Dict]) -> float:
        """Calcule la durée moyenne d'une session"""
        if not learning_history:
            return 0
        
        durations = [h['duration_seconds'] for h in learning_history if h['duration_seconds']]
        if not durations:
            return 0
        
        return sum(durations) / len(durations) / 60  # en minutes
    
    def _calculate_learning_velocity(self, learning_history: List[Dict]) -> float:
        """Calcule la vélocité d'apprentissage (% de progression par jour)"""
        if len(learning_history) < 2:
            return 0.5  # valeur par défaut
        
        # Nombre de jours
        timestamps = [datetime.fromisoformat(h['timestamp'].replace('Z', '+00:00')) for h in learning_history if h['timestamp']]
        if len(timestamps) < 2:
            return 0.5
        
        days = (max(timestamps) - min(timestamps)).days + 1
        if days == 0:
            return 0.5
        
        # Complétion estimée: nombre d'actions / jours
        action_count = len(learning_history)
        velocity = (action_count / days) * 5  # Normaliser à ~5% par jour
        
        return min(velocity, 15)  # Max 15% par jour
    
    def _analyze_learning_pattern(self, learning_history: List[Dict]) -> str:
        """Analyse le pattern d'apprentissage"""
        if not learning_history:
            return 'inconsistent'
        
        # Compter les actions par jour
        daily_actions = {}
        for h in learning_history:
            if h['timestamp']:
                date = h['timestamp'][:10]
                daily_actions[date] = daily_actions.get(date, 0) + 1
        
        # Vérifier la consistance
        if not daily_actions:
            return 'inconsistent'
        
        active_days = len(daily_actions)
        span_days = len(learning_history)
        frequency = active_days / max(span_days, 1)
        
        if frequency > 0.7:
            return 'very_consistent'
        elif frequency > 0.5:
            return 'consistent'
        elif frequency > 0.3:
            return 'moderate'
        else:
            return 'intermittent'
    
    def _estimate_completion_date(self, completed: int, total: int, avg_progress: float, velocity: float) -> str:
        """Estime la date de complétion"""
        if total == 0:
            return 'N/A'
        
        if completed == total:
            return 'Complété'
        
        remaining_percent = 100 - avg_progress
        days_remaining = remaining_percent / max(velocity, 0.5)
        
        completion_date = datetime.utcnow() + timedelta(days=days_remaining)
        return completion_date.strftime('%Y-%m-%d')
    
    def _calculate_remaining_days(self, estimated_date: str) -> int:
        """Calcule le nombre de jours restants"""
        try:
            if estimated_date == 'Complété' or estimated_date == 'N/A':
                return 0
            target = datetime.strptime(estimated_date, '%Y-%m-%d')
            days = (target - datetime.utcnow()).days
            return max(0, days)
        except:
            return 0
    
    def _calculate_avg_complexity(self, enrollments: List[Dict]) -> float:
        """Calcule la complexité moyenne des sujets"""
        complexities = []
        for e in enrollments:
            # Mapper progress à complexité: 0-40% = easy, 40-70% = medium, 70%+ = hard
            progress = e['progress_percentage']
            if progress < 40:
                complexities.append(1)  # Easy
            elif progress < 70:
                complexities.append(2)  # Medium
            else:
                complexities.append(3)  # Hard
        
        return sum(complexities) / len(complexities) if complexities else 2
    
    def _get_recommended_next_subjects(self, enrollments: List[Dict], stats: Dict) -> List[str]:
        """Recommande les suites logiques basées sur catégories"""
        categories = {}
        for e in enrollments:
            cat = e['subject_category']
            if cat:
                categories[cat] = categories.get(cat, 0) + 1
        
        # Suggérer d'autres sujets dans les mêmes catégories
        if categories:
            top_category = sorted(categories.items(), key=lambda x: x[1], reverse=True)[0][0]
            return [f"Sujets avancés en {top_category}", "Certifications connexes"] if top_category else []
        return ["Explorer de nouveaux domaines", "Cas d'étude pratiques"]
    
    def _generate_recommendations(self, stats: Dict, completion_rate: float, velocity: float, weak_areas: List[str]) -> List[str]:
        """Génère des recommandations d'actions"""
        recommendations = []
        
        # Basé sur taux de complétion
        if completion_rate < 25:
            recommendations.append("Accélérez votre progression - vous êtes en bas de la courbe")
        elif completion_rate > 80:
            recommendations.append("Excellent ! Vous approchez de la fin - continuez l'effort")
        
        # Basé sur domaines faibles
        if weak_areas:
            recommendations.append(f"Renforcez {', '.join(weak_areas[:2])} - ce sont vos points faibles")
        
        # Basé sur vélocité
        if velocity < 1:
            recommendations.append("Augmentez votre fréquence d'étude pour mieux progresser")
        elif velocity > 5:
            recommendations.append("Rythme excellent ! Maintenez cette dynamique")
        
        # Basé sur temps total
        if stats['total_learning_time_minutes'] < 60:
            recommendations.append("Augmentez votre temps d'étude quotidien")
        
        # Basé sur fréquence
        if stats['learning_frequency'] < 5:
            recommendations.append("Étudiez plus régulièrement pour une meilleure retention")
        
        return recommendations if recommendations else ["Continuez votre apprentissage régulier"]
    
    def _recommend_study_time(self, velocity: float) -> str:
        """Recommande le temps d'étude quotidien basé sur vélocité"""
        if velocity < 1:
            return "60-90 minutes par jour"
        elif velocity < 3:
            return "45-60 minutes par jour"
        elif velocity < 5:
            return "30-45 minutes par jour"
        else:
            return "20-30 minutes par jour (vous êtes très efficace)"


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    analyzer = UserPerformanceAnalyzer()
    
    # Test
    result = analyzer.analyze_user_progress(user_id=1)
    print(f"\n{result}")
