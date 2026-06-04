# nlp_analyzer.py
# Analyseur NLP pour le service FastApi
#!/usr/bin/env python3
"""
Module NLP pour l'analyse de contenu éducatif.
Utilise sentence-transformers pour les embeddings et calcule la difficulté.
"""

import numpy as np
from transformers import CamembertTokenizer, CamembertModel, pipeline
import torch
import re
import logging
import os

logger = logging.getLogger(__name__)

class NLPAnalyzer:
    def __init__(self, model_name=None):
        """
        Initialise l'analyseur NLP avec modèles lourds (BERT).
        model_name: camembert-base (défaut) ou flaubert/base-cased
        """
        # Utiliser modèle français lourd
        self.model_name = model_name or os.getenv('NLP_MODEL', 'camembert-base')
        self.tokenizer = None
        self.model = None
        self.classifier = None
        self.device = 'cuda' if torch.cuda.is_available() else 'cpu'
        logger.info(f"Using device: {self.device}")
        self.difficulty_keywords = self._load_difficulty_keywords()
    
    def load_model(self):
        """Charge le modèle BERT lourd avec cache S3 si disponible."""
        if self.model is None:
            logger.info(f"Chargement du modèle NLP lourd: {self.model_name}")
            
            # Chemin de cache (peut être monté depuis S3 via EFS)
            cache_dir = os.getenv('MODEL_CACHE_DIR', './models_cache')
            os.makedirs(cache_dir, exist_ok=True)
            
            try:
                # Charger CamemBERT (BERT français)
                self.tokenizer = CamembertTokenizer.from_pretrained(
                    self.model_name, 
                    cache_dir=cache_dir
                )
                self.model = CamembertModel.from_pretrained(
                    self.model_name,
                    cache_dir=cache_dir
                ).to(self.device)
                
                # Pipeline de classification pour difficulté (optionnel)
                # Peut être fine-tuné sur vos données
                self.classifier = pipeline(
                    "text-classification",
                    model=self.model_name,
                    tokenizer=self.tokenizer,
                    device=0 if self.device == 'cuda' else -1
                )
                
                logger.info(f"✅ Modèle {self.model_name} chargé sur {self.device}")
                logger.info(f"   Taille du modèle: ~{self._get_model_size():.1f} MB")
                
            except Exception as e:
                logger.error(f"Erreur lors du chargement du modèle: {e}")
                # Fallback vers un modèle plus léger si échec
                logger.warning("Tentative de fallback vers distilbert...")
                self.model_name = "distilbert-base-multilingual-cased"
                self.load_model()  # Retry
    
    def _get_model_size(self):
        """Estime la taille du modèle en MB."""
        if self.model is None:
            return 0
        param_size = sum(p.nelement() * p.element_size() for p in self.model.parameters())
        buffer_size = sum(b.nelement() * b.element_size() for b in self.model.buffers())
        return (param_size + buffer_size) / (1024**2)
    
    def _load_difficulty_keywords(self):
        """
        Définit des mots-clés pour estimer la difficulté.
        Plus de mots complexes = difficulté plus élevée.
        """
        return {
            'facile': [
                'introduction', 'base', 'simple', 'débutant', 'initiation',
                'premier', 'élémentaire', 'fondamental'
            ],
            'moyen': [
                'intermédiaire', 'pratique', 'application', 'exercice',
                'développement', 'approfondir'
            ],
            'difficile': [
                'avancé', 'complexe', 'expert', 'théorique', 'abstrait',
                'algorithmique', 'optimisation', 'maîtrise', 'perfectionnement'
            ]
        }
    
    def analyze(self, text, metadata=None):
        """
        Analyse un texte éducatif.
        
        Args:
            text (str): le contenu à analyser
            metadata (dict): métadonnées optionnelles (theme, title, etc.)
        
        Returns:
            dict: {
                'difficulty_score': float (0-1),
                'difficulty_level': str (facile/moyen/difficile),
                'estimated_duration': int (minutes),
                'tags': list[str],
                'embedding': list[float] (optionnel),
                'complexity_metrics': dict
            }
        """
        if self.model is None:
            self.load_model()
        
        metadata = metadata or {}
        
        # 1. Calcul de la difficulté
        difficulty_score = self._compute_difficulty(text)
        difficulty_level = self._score_to_level(difficulty_score)
        
        # 2. Extraction de tags sémantiques
        tags = self._extract_tags(text, metadata)
        
        # 3. Métriques de complexité
        complexity = self._compute_complexity_metrics(text)
        
        # 4. Durée estimée (basée sur la longueur et complexité)
        duration = self._estimate_duration(text, complexity)
        
        # 5. Embedding vectoriel avec BERT (contextualisé)
        embedding = None
        if metadata.get('compute_embedding', False):
            embedding = self._compute_bert_embedding(text)
        
        return {
            'difficulty_score': round(difficulty_score, 2),
            'difficulty_level': difficulty_level,
            'estimated_duration_minutes': duration,
            'tags': tags,
            'complexity_metrics': complexity,
            'embedding': embedding
        }
    
    def _compute_difficulty(self, text):
        """
        Calcule un score de difficulté (0=facile, 1=difficile).
        Basé sur : mots-clés, longueur des phrases, vocabulaire.
        """
        text_lower = text.lower()
        
        # Comptage des mots-clés par catégorie
        facile_count = sum(1 for kw in self.difficulty_keywords['facile'] if kw in text_lower)
        moyen_count = sum(1 for kw in self.difficulty_keywords['moyen'] if kw in text_lower)
        difficile_count = sum(1 for kw in self.difficulty_keywords['difficile'] if kw in text_lower)
        
        # Score pondéré
        total = facile_count + moyen_count + difficile_count
        if total == 0:
            score = 0.5  # par défaut moyen
        else:
            score = (
                facile_count * 0.2 +
                moyen_count * 0.5 +
                difficile_count * 0.9
            ) / total
        
        # Ajustement basé sur la longueur moyenne des phrases
        sentences = re.split(r'[.!?]+', text)
        avg_sentence_length = np.mean([len(s.split()) for s in sentences if s.strip()])
        
        # Phrases longues = plus difficile
        if avg_sentence_length > 20:
            score += 0.1
        elif avg_sentence_length < 10:
            score -= 0.1
        
        return np.clip(score, 0.0, 1.0)
    
    def _score_to_level(self, score):
        """Convertit un score numérique en niveau textuel."""
        if score < 0.4:
            return 'facile'
        elif score < 0.7:
            return 'moyen'
        else:
            return 'difficile'
    
    def _extract_tags(self, text, metadata):
        """
        Extrait des tags sémantiques du texte.
        Combine mots-clés + thème du metadata.
        """
        tags = set()
        
        # Tags depuis metadata
        if 'theme' in metadata:
            tags.add(metadata['theme'].lower())
        
        # Tags depuis le texte (mots importants)
        text_lower = text.lower()
        
        # Domaines mathématiques
        if any(kw in text_lower for kw in ['matrice', 'algèbre', 'équation', 'calcul']):
            tags.add('mathématiques')
        
        # Programmation
        if any(kw in text_lower for kw in ['python', 'code', 'algorithme', 'fonction']):
            tags.add('programmation')
        
        # Langues
        if any(kw in text_lower for kw in ['vocabulaire', 'grammaire', 'conversation']):
            tags.add('langues')
        
        # IA
        if any(kw in text_lower for kw in ['machine learning', 'neural', 'deep learning']):
            tags.add('intelligence artificielle')
        
        return list(tags)
    
    def _compute_complexity_metrics(self, text):
        """
        Calcule des métriques de complexité du texte.
        """
        words = text.split()
        sentences = re.split(r'[.!?]+', text)
        
        return {
            'word_count': len(words),
            'sentence_count': len([s for s in sentences if s.strip()]),
            'avg_word_length': np.mean([len(w) for w in words]) if words else 0,
            'avg_sentence_length': np.mean([len(s.split()) for s in sentences if s.strip()]) if sentences else 0
        }
    
    def _estimate_duration(self, text, complexity):
        """
        Estime la durée de lecture/compréhension en minutes.
        Basé sur le nombre de mots et la complexité.
        """
        word_count = complexity['word_count']
        
        # Vitesse de lecture moyenne : 200-250 mots/min
        # Ajusté par la longueur moyenne des phrases (complexité)
        reading_speed = 200
        if complexity['avg_sentence_length'] > 20:
            reading_speed = 150  # plus lent si complexe
        
        minutes = word_count / reading_speed
        return max(1, int(np.ceil(minutes)))
    
    def _compute_bert_embedding(self, text):
        """
        Calcule l'embedding BERT contextualisé du texte.
        Retourne un vecteur de dimension 768 (camembert-base).
        """
        if self.model is None or self.tokenizer is None:
            self.load_model()
        
        # Tokenisation
        inputs = self.tokenizer(
            text,
            return_tensors='pt',
            truncation=True,
            max_length=512,
            padding=True
        ).to(self.device)
        
        # Forward pass
        with torch.no_grad():
            outputs = self.model(**inputs)
        
        # Pooling: moyenne des hidden states (CLS token ou mean pooling)
        # Utiliser le token [CLS] (première position)
        cls_embedding = outputs.last_hidden_state[:, 0, :].squeeze()
        
        # Convertir en liste Python
        return cls_embedding.cpu().numpy().tolist()
    
    def compute_similarity(self, text1, text2):
        """
        Calcule la similarité cosinus entre deux textes avec BERT.
        Retourne un score entre 0 (différent) et 1 (identique).
        """
        if self.model is None:
            self.load_model()
        
        # Embeddings BERT
        emb1 = self._compute_bert_embedding(text1)
        emb2 = self._compute_bert_embedding(text2)
        
        emb1 = np.array(emb1)
        emb2 = np.array(emb2)
        
        # Similarité cosinus
        similarity = np.dot(emb1, emb2) / (np.linalg.norm(emb1) * np.linalg.norm(emb2))
        return float(similarity)
    
    def batch_encode(self, texts, batch_size=8):
        """
        Encode plusieurs textes en batch avec BERT (plus efficace).
        Retourne une matrice numpy (n_texts, 768).
        """
        if self.model is None:
            self.load_model()
        
        all_embeddings = []
        
        for i in range(0, len(texts), batch_size):
            batch = texts[i:i + batch_size]
            
            # Tokenisation du batch
            inputs = self.tokenizer(
                batch,
                return_tensors='pt',
                truncation=True,
                max_length=512,
                padding=True
            ).to(self.device)
            
            # Forward pass
            with torch.no_grad():
                outputs = self.model(**inputs)
            
            # CLS embeddings
            cls_embeddings = outputs.last_hidden_state[:, 0, :].cpu().numpy()
            all_embeddings.append(cls_embeddings)
        
        return np.vstack(all_embeddings)


if __name__ == "__main__":
    # Tests unitaires
    logging.basicConfig(level=logging.INFO)
    
    analyzer = NLPAnalyzer()
    
    # Test 1 : texte simple
    text1 = "Introduction aux matrices. Ce cours présente les bases de l'algèbre linéaire."
    result1 = analyzer.analyze(text1, {'theme': 'mathématiques'})
    print("Test 1 (facile):", result1)
    
    # Test 2 : texte complexe
    text2 = "Optimisation algorithmique avancée des réseaux de neurones profonds avec backpropagation."
    result2 = analyzer.analyze(text2, {'theme': 'intelligence artificielle'})
    print("\nTest 2 (difficile):", result2)
    
    # Test 3 : similarité
    sim = analyzer.compute_similarity(text1, text2)
    print(f"\nSimilarité entre texte1 et texte2: {sim:.3f}")