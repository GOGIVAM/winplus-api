# 🔧 GUIDE COMPLET : CONFIGURATION SENDGRID

## 🚨 PROBLÈME ACTUEL
```
Status: Unauthorized (401)
Clé API invalide: [REDACTED]
```

---

## ✅ SOLUTION 1 : GÉNÉRER UNE NOUVELLE CLÉ SENDGRID

### Étape 1 : Accéder à SendGrid
1. Va sur https://app.sendgrid.com
2. Connecte-toi avec tes identifiants SendGrid
3. (Si tu n'as pas de compte SendGrid, crée-le sur https://sendgrid.com/pricing/)

### Étape 2 : Générer une API Key
1. Clique sur **Settings** (⚙️) en bas à gauche
2. Sélectionne **API Keys**
3. Clique sur **Create API Key**
4. Donne-lui un nom : `winplus-backend-production`
5. Permissions recommandées :
   - ✅ Mail Send
   - ✅ Mail Send (Read Only)
6. Clique **Create & Copy**
7. **COPIE LA CLÉ IMMÉDIATEMENT** (tu ne pourras pas la revoir après !)

La clé ressemblera à : `SG.xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`

### Étape 3 : Vérifier le domaine expéditeur
1. Va sur **Settings** → **Sender Authentication**
2. Clique sur **Verify a Domain** OU **Verify Single Sender**
3. Utilise : `support@gogivam.com`
4. Suivez les instructions (ajout de DNS records)

---

## 🔐 SOLUTION 2 : CONFIGURER DE MANIÈRE SÉCURISÉE (Production)

### ⚠️ PROBLÈME ACTUEL
La clé est en **clair dans le code** : `appsettings.Production.json`

```json
"SendGrid": {
  "ApiKey": "[REDACTED]"  // ❌ DANGEREUX
}
```

### ✅ SOLUTION : Utiliser les variables d'environnement

#### Sur EC2 (Linux)
```bash
# 1. Éditer le fichier d'environnement
sudo nano /etc/environment

# 2. Ajouter à la fin du fichier
SENDGRID_API_KEY="YOUR_NEW_API_KEY_HERE"
SENDGRID_FROM_EMAIL="support@gogivam.com"
SENDGRID_FROM_NAME="WinPlus Support"

# 3. Sauvegarder (Ctrl+X, Y, Enter)

# 4. Appliquer les changements
source /etc/environment

# 5. Vérifier
echo $SENDGRID_API_KEY
```

#### Dans le systemd service
```bash
# Éditer le service winplus-backend
sudo nano /etc/systemd/system/winplus-backend.service

# Dans la section [Service], ajouter :
Environment="SENDGRID_API_KEY=YOUR_NEW_API_KEY_HERE"
Environment="SENDGRID_FROM_EMAIL=support@gogivam.com"
Environment="SENDGRID_FROM_NAME=WinPlus Support"

# Recharger et redémarrer
sudo systemctl daemon-reload
sudo systemctl restart winplus-backend
```

#### Modifier `appsettings.Production.json`
```json
"SendGrid": {
  "ApiKey": "${SENDGRID_API_KEY}",
  "FromEmail": "${SENDGRID_FROM_EMAIL}",
  "FromName": "${SENDGRID_FROM_NAME}"
}
```

Ou mieux encore, laisser vide et modifier le code C# :

```csharp
var apiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY") 
    ?? configuration["SendGrid:ApiKey"]
    ?? throw new InvalidOperationException("SendGrid API key not configured");
```

---

## 🧪 SOLUTION 3 : TESTER LA CLÉ AVANT DÉPLOIEMENT

### Test direct avec curl
```bash
# Remplace YOUR_API_KEY
curl -X GET https://api.sendgrid.com/v3/api_keys \
  -H "Authorization: Bearer YOUR_API_KEY" \
  -H "Content-Type: application/json"

# Résultat esperé : 200 OK avec liste de clés
# Résultat erreur : 401 Unauthorized = clé invalide
```

### Test avec le backend .NET (local)
```bash
# 1. Mets à jour appsettings.Development.json avec la nouvelle clé
"SendGrid": {
  "ApiKey": "YOUR_NEW_API_KEY",
  "FromEmail": "support@gogivam.com",
  "FromName": "WinPlus Support"
}

# 2. Lance le backend en local
dotnet run

# 3. Fais une demande signup
# Les logs devraient afficher :
# ✅ "Successfully sent email to..."
# ❌ "Failed to send email... Status: Unauthorized" = clé invalide
```

---

## 📋 CHECKLIST DE MISE EN PLACE

- [ ] J'ai créé une nouvelle API Key sur SendGrid
- [ ] J'ai copié la clé (SG.xxxxx...)
- [ ] J'ai vérifié le domaine expéditeur (support@gogivam.com)
- [ ] J'ai mis à jour la clé secrètement (variables d'environnement ou AWS Secrets Manager)
- [ ] J'ai testé avec curl
- [ ] J'ai testé le signup localement
- [ ] Les emails s'envoient correctement ✅
- [ ] J'ai redéployé sur EC2
- [ ] Les emails s'envoient en production ✅

---

## 🚀 DÉPLOIEMENT SUR EC2

```bash
# 1. SSH sur EC2
ssh -i your-key.pem ubuntu@44.200.166.163

# 2. Définir les variables d'environnement
sudo nano /etc/systemd/system/winplus-backend.service
# Ajouter les 3 lignes Environment

# 3. Recharger systemd
sudo systemctl daemon-reload

# 4. Redémarrer le service
sudo systemctl restart winplus-backend

# 5. Vérifier les logs
sudo journalctl -u winplus-backend -f

# 6. Tester un signup
# Les logs devraient montrer : "Successfully sent email..."
```

---

## 🔒 SÉCURITÉ : AWS Secrets Manager (Optionnel mais recommandé)

```bash
# 1. Créer le secret dans AWS
aws secretsmanager create-secret \
  --name winplus/sendgrid-api-key \
  --secret-string "YOUR_API_KEY"

# 2. Dans Program.cs, lire depuis Secrets Manager
var sendgridKey = builder.Configuration["SendGrid:ApiKey"];
if (string.IsNullOrEmpty(sendgridKey))
{
    var secretsClient = new SecretsManagerClient();
    var response = await secretsClient.GetSecretValueAsync(
        new GetSecretValueRequest { SecretId = "winplus/sendgrid-api-key" });
    sendgridKey = response.SecretString;
}
```

---

## 📞 EN CAS DE PROBLÈME

### Erreur "Email address not verified"
```
SendGrid n'accepte les envois que depuis des adresses vérifiées.
Solution : Vérifier support@gogivam.com dans SendGrid Dashboard
```

### Erreur "API Key invalid or revoked"
```
La clé n'existe pas ou a été révoquée.
Solution : Générer une nouvelle clé
```

### Erreur "Domain not verified"
```
Le domaine gogivam.com doit être vérifié.
Solution : Ajouter les DNS records fournis par SendGrid
```

---

## ✨ RÉSUMÉ FINAL

| Étape | Action | Statut |
|-------|--------|--------|
| 1 | Générer nouvelle clé SendGrid | ⏳ À faire |
| 2 | Vérifier domaine expéditeur | ⏳ À faire |
| 3 | Tester avec curl | ⏳ À faire |
| 4 | Mettre en env. variables du systèm | ⏳ À faire |
| 5 | Redéployer sur EC2 | ⏳ À faire |
| 6 | Tester signup en production | ⏳ À faire |

Une fois ces étapes complètes, les emails fonctionneront ! ✅

