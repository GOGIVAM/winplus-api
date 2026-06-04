# Guide d'Installation DeepSeek sur AWS EC2

## 📋 PRÉ-REQUIS

### Instance EC2 Recommandée
- **Type**: `g4dn.xlarge` ou supérieur (avec GPU)
- **AMI**: Deep Learning AMI (Ubuntu 22.04) ou Ubuntu 22.04 LTS
- **Storage**: Au moins 100 GB SSD
- **RAM**: Minimum 16 GB
- **vCPUs**: Minimum 4

### Ports à Ouvrir (Security Group)
- **8000**: DeepSeek API (ou port personnalisé)
- **22**: SSH
- **443**: HTTPS (si reverse proxy)

---

## 🚀 INSTALLATION ÉTAPE PAR ÉTAPE

### 1. Connexion à l'Instance EC2

```bash
ssh -i your-key.pem ubuntu@your-ec2-ip
```

### 2. Mise à Jour du Système

```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y build-essential curl git python3-pip python3-venv
```

### 3. Installation de CUDA et cuDNN (Si GPU)

```bash
# Vérifier si GPU est disponible
nvidia-smi

# Si pas de GPU, installer les drivers
sudo apt install -y nvidia-driver-535

# Redémarrer si nécessaire
sudo reboot
```

### 4. Installation de Python et Dépendances

```bash
# Installer Python 3.10+
sudo apt install -y python3.10 python3.10-venv python3.10-dev

# Créer environnement virtuel
mkdir -p ~/deepseek
cd ~/deepseek
python3.10 -m venv venv
source venv/bin/activate

# Mettre à jour pip
pip install --upgrade pip setuptools wheel
```

### 5. Installation de DeepSeek

```bash
# Option 1: Via API officielle (Recommandé)
pip install openai  # DeepSeek utilise une API compatible OpenAI

# Option 2: Hébergement local du modèle avec vLLM (Advanced)
pip install vllm

# Installer les dépendances supplémentaires
pip install fastapi uvicorn gunicorn python-dotenv pydantic
```

### 6. Configuration de l'API DeepSeek

Créer un fichier de configuration `config.env`:

```bash
cat > ~/deepseek/config.env << 'EOF'
# DeepSeek Configuration
DEEPSEEK_API_KEY=your-deepseek-api-key-here
DEEPSEEK_MODEL=deepseek-chat
DEEPSEEK_BASE_URL=https://api.deepseek.com
DEEPSEEK_MAX_TOKENS=2000
DEEPSEEK_TEMPERATURE=0.7

# Server Configuration
HOST=0.0.0.0
PORT=8000
WORKERS=4
EOF
```

**Note**: Obtenir une clé API sur https://platform.deepseek.com/

### 7. Création du Serveur API

Créer `~/deepseek/server.py`:

```python
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import List, Optional
import openai
import os
from dotenv import load_dotenv

load_dotenv('config.env')

app = FastAPI(title="DeepSeek API Server")

# CORS Configuration
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # À restreindre en production
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Configure OpenAI client for DeepSeek
openai.api_key = os.getenv('DEEPSEEK_API_KEY')
openai.api_base = os.getenv('DEEPSEEK_BASE_URL', 'https://api.deepseek.com/v1')

class Message(BaseModel):
    role: str
    content: str

class ChatRequest(BaseModel):
    model: str = os.getenv('DEEPSEEK_MODEL', 'deepseek-chat')
    messages: List[Message]
    max_tokens: int = int(os.getenv('DEEPSEEK_MAX_TOKENS', 2000))
    temperature: float = float(os.getenv('DEEPSEEK_TEMPERATURE', 0.7))
    stream: bool = False

@app.post("/v1/chat/completions")
async def chat_completions(request: ChatRequest):
    try:
        response = openai.ChatCompletion.create(
            model=request.model,
            messages=[msg.dict() for msg in request.messages],
            max_tokens=request.max_tokens,
            temperature=request.temperature,
            stream=request.stream
        )
        return response
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/health")
async def health_check():
    return {"status": "healthy", "service": "deepseek-api"}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        app,
        host=os.getenv('HOST', '0.0.0.0'),
        port=int(os.getenv('PORT', 8000)),
        workers=int(os.getenv('WORKERS', 4))
    )
```

### 8. Configuration Systemd Service

Créer `/etc/systemd/system/deepseek.service`:

```bash
sudo tee /etc/systemd/system/deepseek.service << 'EOF'
[Unit]
Description=DeepSeek API Server
After=network.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/home/ubuntu/deepseek
Environment="PATH=/home/ubuntu/deepseek/venv/bin"
EnvironmentFile=/home/ubuntu/deepseek/config.env
ExecStart=/home/ubuntu/deepseek/venv/bin/gunicorn server:app \
    --bind 0.0.0.0:8000 \
    --workers 4 \
    --worker-class uvicorn.workers.UvicornWorker \
    --timeout 120 \
    --access-logfile /var/log/deepseek/access.log \
    --error-logfile /var/log/deepseek/error.log
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF

# Créer répertoire de logs
sudo mkdir -p /var/log/deepseek
sudo chown ubuntu:ubuntu /var/log/deepseek

# Activer et démarrer le service
sudo systemctl daemon-reload
sudo systemctl enable deepseek
sudo systemctl start deepseek

# Vérifier le statut
sudo systemctl status deepseek
```

### 9. Configuration Nginx (Optionnel - Reverse Proxy)

```bash
sudo apt install -y nginx certbot python3-certbot-nginx

# Configuration Nginx
sudo tee /etc/nginx/sites-available/deepseek << 'EOF'
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass http://127.0.0.1:8000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # Timeouts pour les longues requêtes
        proxy_connect_timeout 120s;
        proxy_send_timeout 120s;
        proxy_read_timeout 120s;
    }
}
EOF

# Activer le site
sudo ln -s /etc/nginx/sites-available/deepseek /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx

# Configurer SSL avec Certbot
sudo certbot --nginx -d your-domain.com
```

### 10. Tests de Validation

```bash
# Test local
curl http://localhost:8000/health

# Test de génération
curl -X POST http://localhost:8000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "deepseek-chat",
    "messages": [
      {"role": "user", "content": "Hello, how are you?"}
    ],
    "max_tokens": 100
  }'

# Test depuis l'extérieur
curl http://your-ec2-ip:8000/health
```

---

## 🔒 SÉCURITÉ

### 1. Firewall UFW

```bash
sudo ufw allow 22/tcp
sudo ufw allow 8000/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
```

### 2. Protection de la Clé API

```bash
# Permissions strictes sur config.env
chmod 600 ~/deepseek/config.env

# Ne jamais commiter config.env dans Git
echo "config.env" >> ~/deepseek/.gitignore
```

### 3. Rate Limiting (Nginx)

Ajouter dans `/etc/nginx/sites-available/deepseek`:

```nginx
limit_req_zone $binary_remote_addr zone=deepseek_limit:10m rate=10r/s;

server {
    # ...
    location / {
        limit_req zone=deepseek_limit burst=20 nodelay;
        # ... reste de la configuration
    }
}
```

---

## 📊 MONITORING

### 1. Logs

```bash
# Logs du service
sudo journalctl -u deepseek -f

# Logs applicatifs
tail -f /var/log/deepseek/access.log
tail -f /var/log/deepseek/error.log
```

### 2. Ressources

```bash
# CPU et RAM
htop

# GPU
nvidia-smi -l 1

# Disque
df -h
```

---

## 🔧 MAINTENANCE

### Redémarrer le Service

```bash
sudo systemctl restart deepseek
```

### Mettre à Jour

```bash
cd ~/deepseek
source venv/bin/activate
pip install --upgrade openai fastapi uvicorn
sudo systemctl restart deepseek
```

### Sauvegardes

```bash
# Backup configuration
tar -czf deepseek-backup-$(date +%Y%m%d).tar.gz \
  ~/deepseek/config.env \
  ~/deepseek/server.py
```

---

## 📞 SUPPORT

- Documentation DeepSeek: https://platform.deepseek.com/docs
- Issues: Contacter le support WinPlus
- Logs: `/var/log/deepseek/`

---

## ✅ CHECKLIST POST-INSTALLATION

- [ ] Instance EC2 configurée et accessible
- [ ] Python 3.10+ installé
- [ ] DeepSeek API configurée avec clé valide
- [ ] Service systemd actif et fonctionnel
- [ ] Tests API réussis (local et externe)
- [ ] Firewall configuré
- [ ] Nginx reverse proxy (si applicable)
- [ ] SSL/TLS configuré (si applicable)
- [ ] Monitoring en place
- [ ] Documentation des credentials sécurisée
