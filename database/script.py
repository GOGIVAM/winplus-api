#!/usr/bin/env python3
"""
Générateur de données éducatives (local).
Produit : data/users.csv, data/contents.csv, data/interactions.csv
Optionnel : fichiers Parquet si pandas / pyarrow sont installés.
"""

import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime, timedelta
import os
from pathlib import Path

# ---------- Configuration ----------
class Config:
    OUTPUT_DIR = Path("data")
    NUM_USERS = 100
    NUM_CONTENTS = 50
    NUM_INTERACTIONS = 2000
    SEED = 42  # pour reproductibilité (None si tu veux aléatoire)
    SAVE_CSV = True
    SAVE_PARQUET = True  # nécessite pyarrow ou fastparquet installés

# ---------- Initialisation ----------
if Config.SEED is not None:
    random.seed(Config.SEED)
    np.random.seed(Config.SEED)

fake = Faker('fr_FR')

# ---------- Générateurs ----------
def generate_users(n=100):
    """Génère n utilisateurs réalistes (DataFrame sans user_id encore)."""
    niveaux = ['débutant', 'intermédiaire', 'avancé']
    objectifs = [
        'maîtriser les matrices', 'améliorer logique', 'parler anglais',
        'mieux coder', 'réussir examens', 'comprendre les algorithmes',
        'apprendre le français', 'développer en Python', 'réussir le bac',
        'maîtriser les statistiques'
    ]
    users = []
    for _ in range(n):
        users.append({
            'nom': fake.last_name(),
            'prenom': fake.first_name(),
            'age': random.randint(15, 65),
            'niveau': random.choice(niveaux),
            'objectif': random.choice(objectifs)
        })
    return pd.DataFrame(users)

def generate_contents(n=50):
    """Génère n contenus éducatifs (DataFrame sans content_id encore)."""
    themes = ['mathématiques', 'programmation', 'langues', 'culture générale', 'intelligence artificielle']
    titres_par_theme = {
        'mathématiques': [
            'Introduction aux matrices', 'Algèbre linéaire', 'Calcul intégral',
            'Géométrie analytique', 'Probabilités avancées', 'Statistiques descriptives'
        ],
        'programmation': [
            'Python pour débutants', 'Structures de données', 'Algorithmes de tri',
            'POO en Python', 'Django Framework', 'APIs REST'
        ],
        'langues': [
            'Grammaire anglaise', 'Vocabulaire espagnol', 'Conversation française',
            'Allemand B1', 'Chinois mandarin', 'Italien débutant'
        ],
        'culture générale': [
            'Histoire contemporaine', 'Géographie mondiale', 'Philosophie grecque',
            'Art moderne', 'Littérature classique', 'Sciences politiques'
        ],
        'intelligence artificielle': [
            'Machine Learning basics', 'Réseaux de neurones', 'NLP introduction',
            'Deep Learning', 'Computer Vision', 'IA éthique'
        ]
    }
    contents = []
    for _ in range(n):
        theme = random.choice(themes)
        titre = random.choice(titres_par_theme[theme])
        difficulte = round(random.uniform(0.1, 1.0), 2)
        contents.append({
            'titre': titre,
            'theme': theme,
            'difficulte': difficulte,
            'description': f"Cours complet sur {titre.lower()}. {fake.sentence(nb_words=15)}"
        })
    return pd.DataFrame(contents)

def generate_interactions(n, user_ids, content_ids, contents_df):
    """
    Génère n interactions en utilisant les vrais IDs fournis.
    contents_df doit contenir les colonnes ['content_id','difficulte'].
    """
    if user_ids is None or content_ids is None or contents_df is None:
        raise ValueError("user_ids, content_ids et contents_df sont requis")
    interactions = []
    start_date = datetime.now() - timedelta(days=365)
    # créer un mapping content_id -> difficulte pour accès rapide
    diff_map = dict(zip(contents_df['content_id'], contents_df['difficulte']))

    for _ in range(n):
        user_id = random.choice(user_ids)
        content_id = random.choice(content_ids)
        difficulte = diff_map[content_id]

        # probabilité de réussite liée à la difficulté
        proba_reussite = 1 - (difficulte * 0.7)
        proba_reussite = max(0.1, min(0.95, proba_reussite))
        reussite = random.random() < proba_reussite

        # temps passé corrélé à la difficulté (en secondes)
        temps_base = int(difficulte * 1800)  # échelle 0..30 minutes
        temps_passe = temps_base + random.randint(-300, 600)
        temps_passe = max(30, temps_passe)

        # clics corrélés
        clics = random.randint(1, int(10 + difficulte * 20))

        # timestamp aléatoire dans l'année passée
        random_days = random.randint(0, 365)
        random_seconds = random.randint(0, 86400)
        timestamp = start_date + timedelta(days=random_days, seconds=random_seconds)

        interactions.append({
            'user_id': user_id,
            'content_id': content_id,
            'clics': clics,
            'temps_passe': temps_passe,
            'reussite': reussite,
            'timestamp': timestamp
        })
    return pd.DataFrame(interactions)

# ---------- Sauvegarde ----------
def save_df(df, path_base: Path, name: str):
    """Sauvegarde df en CSV et (si activé) Parquet."""
    csv_path = path_base / f"{name}.csv"
    df.to_csv(csv_path, index=False)
    print(f"✔️  {name}.csv écrit ({csv_path})")

    if Config.SAVE_PARQUET:
        try:
            pq_path = path_base / f"{name}.parquet"
            df.to_parquet(pq_path, index=False)
            print(f"✔️  {name}.parquet écrit ({pq_path})")
        except Exception as e:
            print(f"⚠️  Impossible d'écrire Parquet pour {name} : {e}")

# ---------- Main ----------
def main():
    print("🚀 Générateur local de datasets éducatifs")
    Config.OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    # Génération users & contents
    users_df = generate_users(Config.NUM_USERS)
    users_df.insert(0, 'user_id', range(1, len(users_df) + 1))  # user_id commence à 1

    contents_df = generate_contents(Config.NUM_CONTENTS)
    contents_df.insert(0, 'content_id', range(1, len(contents_df) + 1))

    print(f"✅ {len(users_df)} utilisateurs générés")
    print(f"✅ {len(contents_df)} contenus générés")

    # sauvegarder users & contents
    save_df(users_df, Config.OUTPUT_DIR, "users")
    save_df(contents_df, Config.OUTPUT_DIR, "contents")

    # Préparer IDs pour interactions
    user_ids = users_df['user_id'].tolist()
    content_ids = contents_df['content_id'].tolist()
    contents_with_ids = contents_df[['content_id', 'difficulte']].copy()

    # Génération interactions
    interactions_df = generate_interactions(
        Config.NUM_INTERACTIONS,
        user_ids,
        content_ids,
        contents_with_ids
    )

    interactions_df.insert(0, 'interaction_id', range(1, len(interactions_df) + 1))

    # sauvegarder interactions
    save_df(interactions_df, Config.OUTPUT_DIR, "interactions")

    # Résumé & stats
    print("\n" + "="*60)
    print("✅ EXPORT LOCAL RÉUSSI")
    print("="*60)
    print(f"👥 Utilisateurs : {len(users_df)}")
    print(f"📚 Contenus : {len(contents_df)}")
    print(f"🔗 Interactions : {len(interactions_df)}")
    taux_reussite = (interactions_df['reussite'].sum() / len(interactions_df)) * 100
    print(f"📊 Taux de réussite global : {taux_reussite:.2f}%")
    print(f"⏱️  Temps moyen passé : {interactions_df['temps_passe'].mean():.0f} secondes")
    print(f"🖱️  Clics moyens : {interactions_df['clics'].mean():.2f}")
    print("="*60)

if __name__ == "__main__":
    main()
