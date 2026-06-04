# 🤖 Guide d'Intégration Chatbot WinPlus

## 📋 TABLE DES MATIÈRES

1. [Vue d'ensemble](#vue-densemble)
2. [Architecture](#architecture)
3. [Installation](#installation)
4. [Configuration](#configuration)
5. [Intégration Backend](#intégration-backend)
6. [Intégration Frontend](#intégration-frontend)
7. [Tests](#tests)
8. [Déploiement](#déploiement)
9. [Troubleshooting](#troubleshooting)

---

## 🎯 VUE D'ENSEMBLE

Le chatbot WinPlus est un assistant IA spécialisé qui aide les étudiants dans leurs révisions et préparation aux concours.

### Fonctionnalités Principales
- ✅ Conversations contextuelles avec historique
- ✅ Personnalisation basée sur le profil utilisateur
- ✅ Support des images et équations LaTeX
- ✅ Feedback sur les réponses
- ✅ Synchronisation du contexte utilisateur
- ✅ Interface moderne et responsive

### Technologies
- **Frontend**: React TypeScript + CSS Modules
- **Backend**: ASP.NET Core (.NET 8)
- **AI Service**: FastApi + DeepSeek
- **Database**: PostgreSQL
- **Auth**: AWS Cognito

---

## 🏗️ ARCHITECTURE

```
┌─────────────────────────────────────────────────────────────┐
│                      ARCHITECTURE                           │
└─────────────────────────────────────────────────────────────┘

┌─────────────┐                                    
│  React      │ ──HTTP──> ┌──────────────┐        
│  Frontend   │           │  ASP.NET     │        
│  (Port 5173)│ <──JSON── │  Backend     │        
└─────────────┘           │  (Port 5000) │        
                          └──────┬───────┘        
                                 │ HTTP/JSON      
                                 ▼                
                          ┌──────────────┐        
                          │   FastApi      │        
                          │   (Port 5001)│        
                          └──────┬───────┘        
                                 │ HTTP/JSON      
                                 ▼                
                          ┌──────────────┐        
                          │  DeepSeek    │        
                          │  (Port 8000) │        
                          └──────────────┘        
                                 
                          ┌──────────────┐        
                          │ PostgreSQL   │        
                          │  (Port 5432) │        
                          └──────────────┘        
```

---

## 🛠️ INSTALLATION

### 1. Migrations PostgreSQL

```bash
cd /path/to/backend
dotnet ef migrations add AddChatbotTables
dotnet ef database update
```

**Fichiers de migration** :
- `20260202_AddChatbotTables.cs`

**Tables créées** :
- `Conversations` - Sessions de conversation
- `Messages` - Messages de chat
- `ChatbotContexts` - Contexte utilisateur

### 2. Backend ASP.NET

**Copier les fichiers dans votre projet** : dotnet

```
dotnet/
├── Models/
│   ├── Entities/
│   │   ├── Conversation.cs
│   │   ├── Message.cs
│   │   └── ChatbotContext.cs
│   └── DTOs/
│       └── ChatbotDTOs.cs
├── Repositories/
│   └── ChatbotRepository.cs
├── Services/
│   └── ChatbotService.cs
└── Controllers/
    └── ChatbotController.cs
```

**Enregistrer les services dans `Program.cs`** :

```csharp
// Add DbSets to ApplicationDbContext
public DbSet<Conversation> Conversations => Set<Conversation>();
public DbSet<Message> Messages => Set<Message>();
public DbSet<ChatbotContext> ChatbotContexts => Set<ChatbotContext>();

// Register services
builder.Services.AddScoped<IChatbotRepository, ChatbotRepository>();
builder.Services.AddScoped<IChatbotService, ChatbotService>();

// Configure HttpClient for FastApi
builder.Services.AddHttpClient("FastApiClient", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["FastApi:BaseUrl"] ?? "http://localhost:5001");
    client.Timeout = TimeSpan.FromSeconds(120);
});
```

### 3. FastApi Service

**Copier les fichiers** : backend/

```
fastapi_api/
├── routes/
│   └── chatbot_routes.py
└── services/
    └── deepseek_client.py
```

**Intégrer dans votre `app.py`** :

```python
from routes.chatbot_routes import chatbot_bp

# Register blueprint
app.register_blueprint(chatbot_bp)
```

**Installer les dépendances** :

```bash
pip install requests python-dotenv --break-system-packages
```

### 4. DeepSeek EC2

Suivre le guide d'installation complet : 
- 📄 `DEEPSEEK_EC2_INSTALLATION.md`

### 5. Frontend React

**Copier les fichiers dans votre projet** : frontend

```
src/
├── types/
│   └── chatbot.ts
├── services/
│   └── chatbotService.ts
├── hooks/
│   └── useChatbot.ts
├── components/
│   └── Chatbot/
│       ├── ChatWindow.tsx
│       ├── MessageBubble.tsx
│       ├── Composer.tsx
│       ├── ImageUploader.tsx
│       ├── MathEditor.tsx
│       └── TypingIndicator.tsx
└── styles/
    └── Chatbot/
        ├── ChatWindow.module.css
        ├── MessageBubble.module.css
        ├── Composer.module.css
        └── ImageUploader.module.css
```

---

## ⚙️ CONFIGURATION

### Backend ASP.NET (`appsettings.json`) vérifie juste si elle est déjà faite et bonne

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=winplus;Username=postgres;Password=yourpassword"
  },
  "FastApi": {
    "BaseUrl": "http://localhost:5001"
  },
  "Jwt": {
    "Issuer": "your-cognito-issuer",
    "Audience": "your-client-id"
  }
}
```

### FastApi (`.env` ou `config.py`) 

```bash
# DeepSeek Configuration
DEEPSEEK_BASE_URL=http://your-ec2-ip:8000
DEEPSEEK_API_KEY=your-api-key
DEEPSEEK_MODEL=deepseek-chat
DEEPSEEK_TIMEOUT=60
DEEPSEEK_MAX_TOKENS=2000
DEEPSEEK_TEMPERATURE=0.7

# FastApi Configuration
FLASK_PORT=5001
FLASK_DEBUG=False
```

### Frontend (`.env`)

```bash
VITE_API_BASE_URL=http://localhost:5000/api
VITE_BACKEND_URL=http://localhost:5000
```

---

## 🔌 INTÉGRATION BACKEND

### 1. Ajouter les Entités au DbContext

Dans `ApplicationDbContext.cs` :

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    
    // Configure Conversation
    modelBuilder.Entity<Conversation>(entity =>
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
        entity.Property(e => e.Tags).HasColumnType("jsonb");
        entity.Property(e => e.Metadata).HasColumnType("jsonb");
        entity.HasIndex(e => e.UserId);
        entity.HasIndex(e => e.IsActive);
        entity.HasIndex(e => e.LastMessageAt);
        
        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    });
    
    // Configure Message
    modelBuilder.Entity<Message>(entity =>
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Content).IsRequired();
        entity.Property(e => e.Attachments).HasColumnType("jsonb");
        entity.HasIndex(e => e.ConversationId);
        entity.HasIndex(e => e.CreatedAt);
        
        entity.HasOne(e => e.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    });
    
    // Configure ChatbotContext
    modelBuilder.Entity<ChatbotContext>(entity =>
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.UserObjectives).HasColumnType("jsonb");
        entity.Property(e => e.EnrolledSubjects).HasColumnType("jsonb");
        entity.Property(e => e.RecentActivity).HasColumnType("jsonb");
        entity.Property(e => e.NavigationHistory).HasColumnType("jsonb");
        entity.Property(e => e.Preferences).HasColumnType("jsonb");
        entity.HasIndex(e => e.UserId).IsUnique();
        
        entity.HasOne(e => e.User)
            .WithOne()
            .HasForeignKey<ChatbotContext>(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    });
}
```

### 2. Créer et Appliquer la Migration

```bash
dotnet ef migrations add AddChatbotTables
dotnet ef database update
```

---

## 🎨 INTÉGRATION FRONTEND

### 1. Utiliser le Composant ChatWindow

```tsx
import ChatWindow from '@/components/Chatbot/ChatWindow';

function App() {
  const [showChat, setShowChat] = useState(false);

  return (
    <div>
      {/* Votre application */}
      
      {/* Chatbot */}
      {showChat && (
        <div style={{
          position: 'fixed',
          bottom: '20px',
          right: '20px',
          width: '400px',
          height: '600px',
          zIndex: 1000
        }}>
          <ChatWindow onClose={() => setShowChat(false)} />
        </div>
      )}

      {/* Bouton pour ouvrir le chat */}
      <button
        onClick={() => setShowChat(true)}
        style={{
          position: 'fixed',
          bottom: '20px',
          right: '20px',
          width: '60px',
          height: '60px',
          borderRadius: '50%',
          background: 'linear-gradient(135deg, #667eea, #764ba2)',
          border: 'none',
          color: 'white',
          fontSize: '24px',
          cursor: 'pointer',
          boxShadow: '0 4px 12px rgba(0,0,0,0.2)'
        }}
      >
        💬
      </button>
    </div>
  );
}
```

### 2. Intégrer dans une Page Dédiée

```tsx
import { useChatbot } from '@/hooks/useChatbot';
import ChatWindow from '@/components/Chatbot/ChatWindow';

function ChatPage() {
  const [conversationId, setConversationId] = useState<number>();

  return (
    <div style={{ height: '100vh', display: 'flex' }}>
      {/* Sidebar avec liste des conversations */}
      <ConversationSidebar onSelectConversation={setConversationId} />
      
      {/* Fenêtre de chat */}
      <div style={{ flex: 1 }}>
        <ChatWindow conversationId={conversationId} />
      </div>
    </div>
  );
}
```

---

## 🧪 TESTS

### Backend Tests

```bash
# Tests unitaires
dotnet test

# Tests d'intégration
dotnet test --filter Category=Integration
```

### Tests API avec cURL

```bash
# Créer une conversation
curl -X POST http://localhost:5000/api/chatbot/conversations \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"title": "Test Conversation"}'

# Envoyer un message
curl -X POST http://localhost:5000/api/chatbot/message \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "content": "Explique-moi les équations du second degré",
    "includeContext": true
  }'

# Récupérer les conversations
curl -X GET "http://localhost:5000/api/chatbot/conversations?page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN"
```

---

## 🚀 DÉPLOIEMENT

### Production Checklist

- [ ] Migrations appliquées en production
- [ ] Variables d'environnement configurées
- [ ] DeepSeek EC2 opérationnel
- [ ] HTTPS configuré
- [ ] Rate limiting activé
- [ ] Logs configurés
- [ ] Monitoring en place
- [ ] Tests E2E passés

### Configuration Production indique moi juste ce que je doitr ajoutyer à l'actuel

**Backend (`appsettings.Production.json`)** :
```json
{
  "FastApi": {
    "BaseUrl": "https://your-fastapi-domain.com"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

**Frontend (`.env.production`)** :
```bash
VITE_API_BASE_URL=https://api.winplus.com/api
```

---

## 🔧 TROUBLESHOOTING

### Problèmes Courants

#### 1. "Failed to reach AI service"

**Causes** :
- FastApi n'est pas démarré
- DeepSeek EC2 est down
- Mauvaise configuration URL

**Solutions** :
```bash
# Vérifier FastApi
curl http://localhost:5001/api/chatbot/health

# Vérifier DeepSeek
curl http://your-ec2-ip:8000/health

# Vérifier les logs
tail -f /var/log/deepseek/error.log
```

#### 2. "Conversation not found"

**Cause** : ID de conversation invalide ou supprimé

**Solution** : Vérifier que l'ID existe en base

```sql
SELECT * FROM "Conversations" WHERE "Id" = 123;
```

#### 3. Timeout lors de l'envoi de messages

**Causes** :
- DeepSeek prend trop de temps
- Timeout trop court

**Solutions** :
```csharp
// Augmenter le timeout ASP.NET
builder.Services.AddHttpClient("FastApiClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(180);
});
```

```python
# Augmenter le timeout FastApi
self.timeout = 120  # DeepSeekClient
```

---

## 📞 SUPPORT

- **Documentation**: Cette page
- **Issues Backend**: Vérifier les logs ASP.NET
- **Issues FastApi**: `/var/log/winplus-fastapi/`
- **Issues DeepSeek**: `/var/log/deepseek/`
- **DB Issues**: Vérifier PostgreSQL logs

---

## ✅ VALIDATION POST-INTÉGRATION

- [ ] Migrations appliquées sans erreur
- [ ] Backend démarre sans erreur
- [ ] FastApi répond sur `/api/chatbot/health`
- [ ] DeepSeek répond sur `/health`
- [ ] Création de conversation fonctionnelle
- [ ] Envoi de message fonctionnel
- [ ] Historique de conversation affiché
- [ ] Feedback enregistré correctement
- [ ] Contexte utilisateur synchronisé
- [ ] Tests frontend passés
- [ ] Tests backend passés

---

**Date de dernière mise à jour** : 02 Février 2026
**Version** : 1.0.0
