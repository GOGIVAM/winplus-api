#!/usr/bin/env python3
"""
Script de test automatique pour valider l'API IA éducative.
Teste tous les endpoints FastApi et .NET Gateway.
"""

import requests
import json
import time
from colorama import init, Fore, Style

# Initialiser colorama pour les couleurs dans le terminal
init(autoreset=True)

# Configuration
FLASK_BASE = "http://localhost:5000"
DOTNET_BASE = "http://localhost:5001/api/ai"

def print_header(text):
    print(f"\n{Fore.CYAN}{'='*60}")
    print(f"{Fore.CYAN}{text}")
    print(f"{Fore.CYAN}{'='*60}\n")

def print_success(text):
    print(f"{Fore.GREEN}✅ {text}")

def print_error(text):
    print(f"{Fore.RED}❌ {text}")

def print_info(text):
    print(f"{Fore.YELLOW}ℹ️  {text}")

def test_endpoint(name, method, url, data=None, expected_status=200):
    """Test générique d'un endpoint."""
    try:
        if method == "GET":
            response = requests.get(url, timeout=10)
        elif method == "POST":
            response = requests.post(url, json=data, timeout=10)
        else:
            raise ValueError(f"Méthode non supportée: {method}")
        
        if response.status_code == expected_status:
            print_success(f"{name}: {response.status_code}")
            return response.json()
        else:
            print_error(f"{name}: Status {response.status_code} (attendu {expected_status})")
            return None
    
    except requests.exceptions.ConnectionError:
        print_error(f"{name}: Connexion impossible - Service non démarré ?")
        return None
    except requests.exceptions.Timeout:
        print_error(f"{name}: Timeout - Service trop lent")
        return None
    except Exception as e:
        print_error(f"{name}: {str(e)}")
        return None

def test_fastapi_service():
    """Test du service FastApi Python."""
    print_header("🐍 TEST SERVICE FLASK (Python)")
    
    # 1. Health check
    result = test_endpoint(
        "Health Check",
        "GET",
        f"{FLASK_BASE}/health"
    )
    
    if result:
        print_info(f"Service: {result.get('service', 'Unknown')}")
        print_info(f"Version: {result.get('version', 'Unknown')}")
    
    # 2. Analyse NLP par ID
    print("\n--- Test NLP Analysis (par ID) ---")
    result = test_endpoint(
        "Analyze Content by ID",
        "POST",
        f"{FLASK_BASE}/api/v1/analyze_content",
        data={"content_id": 1}
    )
    
    if result:
        print_info(f"Difficulté: {result.get('difficulty_level', 'N/A')}")
        print_info(f"Score: {result.get('difficulty_score', 'N/A')}")
        print_info(f"Durée estimée: {result.get('estimated_duration_minutes', 'N/A')} min")
        print_info(f"Tags: {', '.join(result.get('tags', []))}")
    
    # 3. Analyse NLP par texte
    print("\n--- Test NLP Analysis (par texte) ---")
    result = test_endpoint(
        "Analyze Content by Text",
        "POST",
        f"{FLASK_BASE}/api/v1/analyze_content",
        data={
            "text": "Introduction avancée aux algorithmes de tri optimisés et complexité algorithmique.",
            "title": "Algorithmes de tri"
        }
    )
    
    if result:
        print_info(f"Difficulté: {result.get('difficulty_level', 'N/A')}")
        print_info(f"Mots: {result.get('complexity_metrics', {}).get('word_count', 'N/A')}")
    
    # 4. Recommandations basiques
    print("\n--- Test Recommendations ---")
    result = test_endpoint(
        "Get Recommendations",
        "GET",
        f"{FLASK_BASE}/api/v1/recommendations?user_id=1&limit=5"
    )
    
    if result:
        print_info(f"Nombre de recommandations: {result.get('count', 0)}")
        if result.get('recommendations'):
            print_info("Premières recommandations:")
            for rec in result['recommendations'][:3]:
                print(f"  • {rec['titre']} (score: {rec['score']:.3f})")
    
    # 5. Recommandations personnalisées
    print("\n--- Test Personalized Recommendations ---")
    result = test_endpoint(
        "Get Personalized Recommendations",
        "POST",
        f"{FLASK_BASE}/api/v1/recommendations/personalized",
        data={
            "user_id": 1,
            "theme": "mathématiques",
            "difficulty_range": [0.3, 0.7],
            "limit": 5
        }
    )
    
    if result:
        print_info(f"Recommandations filtrées: {result.get('count', 0)}")
    
    # 6. Statistiques utilisateur
    print("\n--- Test User Stats ---")
    result = test_endpoint(
        "Get User Stats",
        "GET",
        f"{FLASK_BASE}/api/v1/users/1/stats"
    )
    
    if result and 'statistics' in result:
        stats = result['statistics']
        print_info(f"Interactions: {stats.get('total_interactions', 'N/A')}")
        print_info(f"Taux de réussite: {stats.get('taux_reussite', 0)*100:.1f}%")
        print_info(f"Temps moyen: {stats.get('avg_temps_passe', 0):.0f}s")

def test_dotnet_gateway():
    """Test de l'API Gateway .NET."""
    print_header("🔷 TEST API GATEWAY (.NET)")
    
    # 1. Health check
    result = test_endpoint(
        "Health Check",
        "GET",
        f"{DOTNET_BASE}/health"
    )
    
    if result and result.get('success'):
        data = result.get('data', {})
        print_info(f"Service: {data.get('service', 'Unknown')}")
    
    # 2. Analyse via Gateway
    print("\n--- Test Analysis via Gateway ---")
    result = test_endpoint(
        "Analyze via Gateway",
        "POST",
        f"{DOTNET_BASE}/analyze",
        data={
            "contentId": 5,
            "computeEmbedding": False
        }
    )
    
    if result and result.get('success'):
        data = result.get('data', {})
        print_info(f"Difficulté: {data.get('difficultyLevel', 'N/A')}")
    
    # 3. Recommandations via Gateway
    print("\n--- Test Recommendations via Gateway ---")
    result = test_endpoint(
        "Recommendations via Gateway",
        "GET",
        f"{DOTNET_BASE}/recommendations/1?limit=5"
    )
    
    if result and result.get('success'):
        data = result.get('data', {})
        print_info(f"Recommandations: {data.get('count', 0)}")
    
    # 4. Stats via Gateway
    print("\n--- Test User Stats via Gateway ---")
    result = test_endpoint(
        "User Stats via Gateway",
        "GET",
        f"{DOTNET_BASE}/users/1/stats"
    )
    
    if result and result.get('success'):
        data = result.get('data', {})
        if 'statistics' in data:
            stats = data['statistics']
            print_info(f"Interactions: {stats.get('totalInteractions', 'N/A')}")
            print_info(f"Taux réussite: {stats.get('tauxReussite', 0)*100:.1f}%")

def test_error_handling():
    """Test de la gestion des erreurs."""
    print_header("⚠️  TEST GESTION D'ERREURS")
    
    # 1. User inexistant
    print("--- Test User Not Found ---")
    result = test_endpoint(
        "User Not Found",
        "GET",
        f"{FLASK_BASE}/api/v1/users/99999/stats",
        expected_status=404
    )
    
    # 2. Content ID invalide
    print("\n--- Test Invalid Content ID ---")
    result = test_endpoint(
        "Invalid Content ID",
        "POST",
        f"{FLASK_BASE}/api/v1/analyze_content",
        data={"content_id": 99999},
        expected_status=404
    )
    
    # 3. Requête mal formée
    print("\n--- Test Malformed Request ---")
    result = test_endpoint(
        "Malformed Request",
        "POST",
        f"{FLASK_BASE}/api/v1/analyze_content",
        data={},  # Manque content_id ou text
        expected_status=400
    )

def run_performance_test():
    """Test simple de performance."""
    print_header("⚡ TEST PERFORMANCE")
    
    num_requests = 10
    url = f"{FLASK_BASE}/api/v1/recommendations?user_id=1&limit=5"
    
    print_info(f"Envoi de {num_requests} requêtes...")
    
    start_time = time.time()
    successes = 0
    
    for i in range(num_requests):
        try:
            response = requests.get(url, timeout=5)
            if response.status_code == 200:
                successes += 1
        except:
            pass
    
    elapsed = time.time() - start_time
    avg_time = elapsed / num_requests
    
    print_info(f"Requêtes réussies: {successes}/{num_requests}")
    print_info(f"Temps total: {elapsed:.2f}s")
    print_info(f"Temps moyen: {avg_time*1000:.0f}ms par requête")
    
    if avg_time < 0.5:
        print_success("Performance: EXCELLENTE (<500ms)")
    elif avg_time < 1.0:
        print_success("Performance: BONNE (<1s)")
    else:
        print_error("Performance: À AMÉLIORER (>1s)")

def main():
    """Exécute tous les tests."""
    print(f"\n{Fore.MAGENTA}{Style.BRIGHT}")
    print("╔════════════════════════════════════════════════════════════╗")
    print("║     🎓 SUITE DE TESTS - SERVICE IA ÉDUCATIVE 🎓           ║")
    print("╚════════════════════════════════════════════════════════════╝")
    print(Style.RESET_ALL)
    
    print_info("Démarrage des tests...")
    print_info(f"FastApi Service: {FLASK_BASE}")
    print_info(f".NET Gateway: {DOTNET_BASE}")
    
    try:
        # Tests FastApi
        test_fastapi_service()
        
        # Tests .NET Gateway
        test_dotnet_gateway()
        
        # Tests d'erreurs
        test_error_handling()
        
        # Tests de performance
        run_performance_test()
        
        # Résumé
        print_header("📊 RÉSUMÉ")
        print_success("Tous les tests ont été exécutés")
        print_info("Vérifiez les résultats ci-dessus pour les détails")
        
    except KeyboardInterrupt:
        print_error("\nTests interrompus par l'utilisateur")
    except Exception as e:
        print_error(f"Erreur inattendue: {e}")

if __name__ == "__main__":
    main()