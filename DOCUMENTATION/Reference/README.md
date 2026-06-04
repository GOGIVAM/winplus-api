# 🤖 Chatbot WinPlus - Solution Complète

## 📦 CONTENU DU PACKAGE

Ce package contient **TOUS LES FICHIERS** nécessaires pour intégrer le chatbot IA dans votre application WinPlus.

```
chatbot/
├── 📁 backend/              # Backend ASP.NET (.NET 8)
│   ├── Conversation.cs
│   ├── Message.cs
│   ├── ChatbotContext.cs
│   ├── ChatbotDTOs.cs
│   ├── ChatbotRepository.cs
│   ├── ChatbotService.cs
│   └── ChatbotController.cs
│
├── 📁 frontend/             # Frontend React TypeScript
│   ├── types/
│   │   └── chatbot.ts
│   ├── services/
│   │   └── chatbotService.ts
│   ├── hooks/
│   │   └── useChatbot.ts
│   ├── components/
│   │   ├── ChatWindow.tsx
│   │   ├── MessageBubble.tsx
│   │   ├── Composer.tsx
│   │   ├── ImageUploader.tsx
│   │   ├── MathEditor.tsx
│   │   └── TypingIndicator.tsx
│   └── styles/
│       ├── ChatWindow.module.css
│       ├── MessageBubble.module.css
│       ├── Composer.module.css
│       └── ImageUploader.module.css
│
├── 📁 fastapi/                # Service FastApi + DeepSeek
│   ├── chatbot_routes.py
│   └── deepseek_client.py
│
├── 📁 migrations/           # Migrations PostgreSQL
│   └── 20260202_AddChatbotTables.cs
│
└── 📁 docs/                 # Documentation
    ├── INTEGRATION_GUIDE.md
    └── DEEPSEEK_EC2_INSTALLATION.md
```

---

## 🎯 VUE D'ENSEMBLE

### Fonctionnalités Complètes

✅ **Conversations intelligentes**
- Historique persistant en base de données
- Support multi-utilisateurs
- Gestion des sessions actives/archivées

✅ **Personnalisation avancée**
- Contexte utilisateur (niveau, objectifs, cours inscrits)
- Historique d'activité et navigation
- Réponses adaptées au profil

✅ **Support multimédia**
- Upload d'images (drag & drop)
- Éditeur d'équations LaTeX
- Fichiers attachés

✅ **Interface moderne**
- Design professionnel avec CSS Modules
- Responsive (mobile + desktop)
- Animations fluides
- Feedback visuel

✅ **Backend robuste**
- API RESTful complète
- Gestion des erreurs
- Authentification Cognito
- Retry logic

---

## 🚀 INSTALLATION RAPIDE

### 1. Backend (ASP.NET)

```bash
# 1. Copier les fichiers backend dans votre projet
cp -r backend/* /path/to/your/Backend/

# 2. Appliquer la migration
cd /path/to/your/Backend
dotnet ef migrations add AddChatbotTables
dotnet ef database update

# 3. Ajouter les services dans Program.cs (voir docs)
```

### 2. Frontend (React)

```bash
# 1. Copier les fichiers frontend
cp -r frontend/* /path/to/your/src/

# 2. Importer et utiliser ChatWindow
# Exemple : src/App.tsx
import ChatWindow from '@/components/Chatbot/ChatWindow';
```

### 3. FastApi

```bash
# 1. Copier les fichiers FastApi
cp -r fastapi/* /path/to/your/fastapi/

# 2. Enregistrer les routes dans app.py
from routes.chatbot_routes import chatbot_bp
app.register_blueprint(chatbot_bp)

# 3. Installer dépendances
pip install requests python-dotenv --break-system-packages
```

### 4. DeepSeek EC2

Suivre le guide complet : **`docs/DEEPSEEK_EC2_INSTALLATION.md`**

---

## ⚙️ CONFIGURATION

### Backend (`appsettings.json`)

```json
{
  "FastApi": {
    "BaseUrl": "http://localhost:5001"
  }
}
```

### FastApi (`.env`)

```bash
DEEPSEEK_BASE_URL=http://your-ec2-ip:8000
DEEPSEEK_API_KEY=your-api-key
DEEPSEEK_MODEL=deepseek-chat
```

### Frontend (`.env`)

```bash
VITE_API_BASE_URL=http://localhost:5000/api
```

---

## 📚 DOCUMENTATION

### Guides Complets

1. **`docs/INTEGRATION_GUIDE.md`**
   - Installation détaillée
   - Configuration
   - Intégration backend/frontend
   - Tests
   - Troubleshooting

2. **`docs/DEEPSEEK_EC2_INSTALLATION.md`**
   - Configuration instance EC2
   - Installation DeepSeek
   - Systemd service
   - Nginx reverse proxy
   - Sécurité et monitoring

---

## 🏗️ ARCHITECTURE

```
┌──────────────┐
│   React UI   │  (Port 5173)
└──────┬───────┘
       │ HTTP/JSON
       ▼
┌──────────────┐
│  ASP.NET API │  (Port 5000)
└──────┬───────┘
       │ HTTP/JSON
       ▼
┌──────────────┐
│    FastApi     │  (Port 5001)
└──────┬───────┘
       │ HTTP/JSON
       ▼
┌──────────────┐
│   DeepSeek   │  (Port 8000 - EC2)
└──────────────┘

┌──────────────┐
│  PostgreSQL  │  (Port 5432)
└──────────────┘
```

---

## 🗄️ SCHÉMA BASE DE DONNÉES

### Table `Conversations`
- Stocke les sessions de chat
- Liens vers `Users`
- Soft delete activé

### Table `Messages`
- Tous les messages (user + assistant)
- Support images, LaTeX, attachments
- Métriques AI (tokens, temps)
- Feedback utilisateur

### Table `ChatbotContexts`
- Profil utilisateur
- Cours inscrits
- Activité récente
- Historique de navigation
- Préférences

---

## 🎨 INTERFACE UTILISATEUR

### Composants Principaux

1. **ChatWindow**
   - Container principal
   - Header avec actions
   - Zone de messages
   - Zone de saisie

2. **MessageBubble**
   - Affichage des messages
   - Support multimédia
   - Boutons de feedback

3. **Composer**
   - Zone de saisie extensible
   - Upload d'images
   - Éditeur LaTeX
   - Compteur de caractères

---

## 🔌 API ENDPOINTS

### Conversations

```
POST   /api/chatbot/conversations        # Créer
GET    /api/chatbot/conversations        # Lister
GET    /api/chatbot/conversations/:id    # Récupérer
PATCH  /api/chatbot/conversations/:id    # Modifier
DELETE /api/chatbot/conversations/:id    # Supprimer
```

### Messages

```
POST   /api/chatbot/message              # Envoyer
POST   /api/chatbot/messages/:id/feedback # Feedback
```

### Contexte

```
GET    /api/chatbot/context              # Récupérer
POST   /api/chatbot/context/sync         # Synchroniser
```

---

## 🧪 TESTS

### Tests Backend

```bash
# Créer une conversation
curl -X POST http://localhost:5000/api/chatbot/conversations \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"title": "Test"}'

# Envoyer un message
curl -X POST http://localhost:5000/api/chatbot/message \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content": "Bonjour", "includeContext": true}'
```

### Tests Frontend

```tsx
import { render, screen } from '@testing-library/react';
import ChatWindow from '@/components/Chatbot/ChatWindow';

test('renders chat window', () => {
  render(<ChatWindow />);
  expect(screen.getByText(/Assistant WinPlus/i)).toBeInTheDocument();
});
```

---

## 🔒 SÉCURITÉ

- ✅ Authentification Cognito requise
- ✅ Validation des entrées (DTOs)
- ✅ Rate limiting (Nginx)
- ✅ Soft delete (pas de suppression physique)
- ✅ HTTPS en production
- ✅ CORS configuré

---

## 🚀 DÉPLOIEMENT

### Production Checklist

- [ ] Migrations appliquées
- [ ] Variables d'environnement configurées
- [ ] DeepSeek EC2 opérationnel
- [ ] HTTPS configuré
- [ ] Logs configurés
- [ ] Monitoring en place
- [ ] Tests E2E passés

---

## 📊 MONITORING

### Logs à Surveiller

```bash
# ASP.NET
tail -f /var/log/winplus-backend/app.log

# FastApi
tail -f /var/log/winplus-fastapi/app.log

# DeepSeek
tail -f /var/log/deepseek/access.log
tail -f /var/log/deepseek/error.log
```

### Métriques Clés

- Temps de réponse AI (processingTimeMs)
- Tokens utilisés (tokensUsed)
- Taux d'erreur
- Taux de satisfaction (feedback positif/négatif)

---

## 🔧 MAINTENANCE

### Mise à Jour DeepSeek

```bash
cd ~/deepseek
source venv/bin/activate
pip install --upgrade openai
sudo systemctl restart deepseek
```

### Nettoyage Base de Données

```sql
-- Supprimer conversations archivées > 90 jours
UPDATE "Conversations"
SET "IsDeleted" = true, "DeletedAt" = NOW()
WHERE "IsActive" = false 
  AND "LastMessageAt" < NOW() - INTERVAL '90 days';
```

---

## 🆘 SUPPORT

### Problèmes Courants

**"Failed to reach AI service"**
- Vérifier que FastApi est démarré
- Vérifier que DeepSeek EC2 est accessible
- Vérifier les variables d'environnement

**"Conversation not found"**
- Vérifier que l'ID existe en DB
- Vérifier les permissions utilisateur

**Timeout lors de l'envoi**
- Augmenter le timeout dans ASP.NET et FastApi
- Vérifier les performances DeepSeek

### Ressources

- 📖 Guide d'intégration : `docs/INTEGRATION_GUIDE.md`
- 🚀 Installation DeepSeek : `docs/DEEPSEEK_EC2_INSTALLATION.md`
- 📧 Support : Contacter l'équipe WinPlus

---

## ✅ VALIDATION

### Checklist Post-Installation

- [ ] Backend démarre sans erreur
- [ ] Migrations appliquées
- [ ] FastApi répond sur `/health`
- [ ] DeepSeek répond sur `/health`
- [ ] Création de conversation ✓
- [ ] Envoi de message ✓
- [ ] Historique affiché ✓
- [ ] Feedback enregistré ✓
- [ ] Contexte synchronisé ✓
- [ ] Tests passés ✓

---

## 📝 NOTES IMPORTANTES

1. **Clé API DeepSeek** : À obtenir sur https://platform.deepseek.com/
2. **Instance EC2** : Recommandé g4dn.xlarge minimum
3. **PostgreSQL** : Version 14+ requise pour JSONB
4. **Node.js** : Version 18+ pour React
5. **.NET** : Version 8.0+ pour ASP.NET Core

---

## 🎉 PRÊT À DÉMARRER !

Suivez les étapes du **`docs/INTEGRATION_GUIDE.md`** pour une installation complète et détaillée.

Pour toute question, consultez la documentation ou contactez le support WinPlus.

---

**Version** : 1.0.0  
**Date** : 02 Février 2026  
**Auteur** : Claude (Anthropic)  
**Projet** : WinPlus Chatbot Integration
