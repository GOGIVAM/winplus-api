**WINPLUS**

Plateforme Educative Camerounaise

**PLAN D'ABONNEMENT COMPLET**

Boutique Libre + 4 Types de Comptes

| **Version** | 1.0 |
| --- | --- |
| **Date** | 3 Mars 2026 |
| **Statut** | Document de référence - Stratégie de monétisation |
| **Contexte** | Cameroun / Afrique francophone |
| **Devise** | XAF (Franc CFA CEMAC) |
| **TVA applicable** | 19,25% (Cameroun) |

**TABLE DES MATIERES**

[1\. Vue d'ensemble 4](#_Toc223453982)

[1.1 Philosophie de monetisation 4](#_Toc223453983)

[1.2 Les deux modes d'acces 4](#_Toc223453984)

[2\. Boutique Libre - Vente a l'unite 5](#_Toc223453985)

[2.1 Principe de fonctionnement 5](#_Toc223453986)

[2.2 Types de contenus disponibles a l'unite 5](#_Toc223453987)

[2.3 Grille tarifaire par niveau d'examen 5](#_Toc223453988)

[2.4 Contenus gratuits 5](#_Toc223453989)

[2.5 Modes de paiement Boutique Libre 6](#_Toc223453990)

[3\. Architecture des 4 types de comptes 7](#_Toc223453991)

[3.1 Compte Eleve (Student) 7](#_Toc223453992)

[3.2 Compte Parent 7](#_Toc223453993)

[3.3 Compte Professeur (Teacher) 7](#_Toc223453994)

[3.4 Compte Institution 7](#_Toc223453995)

[4\. Fonctionnalites gratuites (tous comptes) 8](#_Toc223453996)

[Catalogue et Decouverte 8](#_Toc223453997)

[Gestion du Compte 8](#_Toc223453998)

[Fonctionnalites Sociales 8](#_Toc223453999)

[Apprentissage Basique 8](#_Toc223454000)

[IA et Support 8](#_Toc223454001)

[5\. Plans d'abonnement Eleves 9](#_Toc223454002)

[Details des plans Eleves 9](#_Toc223454003)

[6\. Plans d'abonnement Parents 11](#_Toc223454004)

[Details des plans Parents 11](#_Toc223454005)

[7\. Plans d'abonnement Professeurs 13](#_Toc223454006)

[Details des plans Professeurs 13](#_Toc223454007)

[8\. Plans d'abonnement Institutions 15](#_Toc223454008)

[Tarification indicative Institutions 15](#_Toc223454009)

[9\. Matrice de fonctionnalites par type de compte 16](#_Toc223454010)

[10\. Bundles, reductions et engagements 17](#_Toc223454011)

[10.1 Modeles d'engagement 17](#_Toc223454012)

[10.2 Bundles speciaux 17](#_Toc223454013)

[Bundle 'Family Pack' 17](#_Toc223454014)

[Bundle 'School' 17](#_Toc223454015)

[Bundle 'Collectif Enseignants' 17](#_Toc223454016)

[11\. Etat d'implementation actuel 18](#_Toc223454017)

[11.1 Backend (ASP.NET Core) 18](#_Toc223454018)

[11.2 Frontend (React + TypeScript) 18](#_Toc223454019)

[12\. Ameliorations et fonctionnalites a venir 19](#_Toc223454020)

[12.1 Court terme (1-3 mois) 19](#_Toc223454021)

[Priorite 1 : Boutique Libre 19](#_Toc223454022)

[Priorite 2 : Compte Institution 19](#_Toc223454023)

[Priorite 3 : Paiements 19](#_Toc223454024)

[Priorite 4 : Fonctionnalites Eleves 19](#_Toc223454025)

[Priorite 5 : Fonctionnalites Parents 19](#_Toc223454026)

[Priorite 6 : Fonctionnalites Professeurs 19](#_Toc223454027)

[12.2 Moyen terme (3-6 mois) 19](#_Toc223454028)

[12.3 Long terme (6-12 mois) 19](#_Toc223454029)

[13\. Projections financieres (Annee 1 - conservateur) 20](#_Toc223454030)

[14\. Notes d'implementation technique 21](#_Toc223454031)

[14.1 Base de donnees 21](#_Toc223454032)

[14.2 API Endpoints 21](#_Toc223454033)

[14.3 Roadmap technique 21](#_Toc223454034)

[15\. Conclusion 22](#_Toc223454035)

[Prochaines etapes 22](#_Toc223454036)

# 1\. Vue d'ensemble

WinPlus est une plateforme éducative numérique spécialement conçue pour les étudiants camerounais et d'Afrique francophone. Elle centralise les annales corrigées des principaux concours et examens, tout en offrant un accompagnement pédagogique intelligent base sur l'IA.

La plateforme propose deux modes d'accès complémentaires : une boutique libre accessible à tous (avec ou sans compte) pour l'achat a l'unité d'épreuves et de documents, ainsi que des abonnements mensuels adaptes à 4 types de comptes distincts.

## 1.1 Philosophie de monétisation

La stratégie repose sur 5 principes fondamentaux adaptes au contexte camerounais :

**Accessibilité :** Gratuit pour découvrir, prix adaptes au pouvoir d'achat local (XAF)

**Flexibilité :** Achat a l'unité sans engagement OU abonnement selon les besoins

**Extensibilité :** Prix augmente avec les fonctionnalités, pas de surprise

**Paiement local :** MTN Mobile Money et Orange Money comme moyens principaux

**Valeur réelle :** Chaque franc dépense apporte un bénéfice mesurable

## 1.2 Les deux modes d'accès

**MODE 1 : Boutique Libre (sans compte ou compte gratuit)**

Achat a l'unité d'épreuves, quiz, livres et packs. Accessible à tous, paiement par Mobile Money. Aucun abonnement requis. Idéal pour les achats ponctuels.

**MODE 2 : Abonnements par type de compte**

Plans mensuels/annuels adaptes aux Elevés, Parents, Professeurs et Institutions. Accès illimite au contenu, outils avances, suivi de progression, chabot IA, et fonctionnalités collaboratives.

# 2\. Boutique Libre - Vente à l'unité

La Boutique Libre est le point d'entrée principal de WinPlus. Elle permet à n'importe qui d'acheter du contenu éducatif a l'unité, sans obligation de créer un compte ou de s'abonner. C'est le mode d'achat le plus simple et le plus accessible.

## 2.1 Principe de fonctionnement

L'utilisateur parcourt le catalogue, sélectionne le contenu souhaite, paie via Mobile Money (MTN MoMo ou Orange Money) et reçoit immédiatement le document par téléchargement ou par email. Un compte gratuit peut être créé pour conserver l'historique des achats.

## 2.2 Types de contenus disponibles à l'unité

| **Type de contenu** | **Fourchette de prix** | **Format** | **Exemples** |
| --- | --- | --- | --- |
| Epreuve seule (sujet) | 500 - 1 500 XAF | PDF | BAC Maths C, MATHS BEAC, Maths ISSEA 2012 |
| Epreuve + corrige | 1 000 - 3 000 XAF | PDF | BAC Physique D + corrige détaillée |
| Pack concours (5-10 épreuves) | 3 000 - 8 000 XAF | PDF (ZIP) | Pack ENSP 2020-2023, Pack Polytechnique |
| Quiz interactif | 500 - 1 500 XAF | En ligne | Quiz Chimie BAC, QCM FMSB |
| Livre/Guide de préparation | 2 000 - 10 000 XAF | PDF/EPUB | Guide BAC C, Method dissertations, Board AE ENSPD |
| Document complet (tout-en-un) | 5 000 - 15 000 XAF | PDF + Quiz | Kit complet BAC D 2024 |
| Fiches de révision | 500 - 2 000 XAF | PDF | Fiches Maths terminale C |

## 2.3 Grille tarifaire par niveau d'examen

| **Examen / Concours** | **Epreuve seule** | **Epreuve + corrige** | **Pack complet (5+ épreuves)** |
| --- | --- | --- | --- |
| **BEPC** | 500 XAF | 1 000 XAF | 3 000 XAF |
| **Probatoire** | 1 000 XAF | 2 000 XAF | 5 000 XAF |
| **Baccalauréat (A/C/D)** | 1 000 XAF | 2 500 - 3 000 XAF | 6 000 - 8 000 XAF |
| **ENSP** | 1 500 XAF | 3 500 - 4 000 XAF | 8 000 XAF |
| **Polytechnique** | 1 500 XAF | 4 000 - 5 000 XAF | 10 000 XAF |
| **ESSEC** | 1 500 XAF | 3 500 - 4 000 XAF | 8 000 XAF |
| **IUT** | 1 000 XAF | 3 000 - 3 500 XAF | 7 000 XAF |
| **FMSB (Médecine)** | 1 500 XAF | 4 000 - 5 000 XAF | 10 000 XAF |
| **ENAM** | 1 500 XAF | 3 500 XAF | 8 000 XAF |
| **ENS** | 1 000 XAF | 3 000 XAF | 7 000 XAF |
| **CONCOURS DE RECRUTEMENTS** | 1500 XAF | 3 000 XAF | 10 000 XAF |

## 2.4 Contenus gratuits

Pour attirer les utilisateurs et démontrer la qualité du catalogue, certains contenus sont accessibles gratuitement :

- Epreuves BEPC des 2 dernières années (sujet uniquement, sans corrige)
- Aperçu de 2-3 pages pour tout contenu payant
- 1 quiz d'essai gratuit par matière
- Fiches méthodologiques générales (comment préparer un examen, etc.)
- Contenus identifies comme 'gratuit' par les professeurs contributeurs

## 2.5 Modes de paiement Boutique Libre

Les paiements sont adaptés au contexte camerounais :

**MTN Mobile Money (MoMo) :** Paiement par USSD ou via l'application MTN MoMo. Le client reçoit un SMS de confirmation et le téléchargement se déclenche automatiquement.

**Orange Money:** Meme processes via Orange Money. Confirmation par SMS + téléchargement immédiat.

**Visa/Mastercard (Phase 2) :** Paiement par carte bancaire pour les utilisateurs disposant de cartes internationales.

**Achat pour un tiers :** Un parent ou un ami peut acheter un contenu et l'envoyer par email a un tiers (code de téléchargement).

**Avantage clé de la Boutique Libre**

Pas de friction : l'utilisateur achète en 30 secondes via Mobile Money, sans obligation de créer un compte. C'est le moyen le plus rapide et accessible pour les étudiants camerounais d'accéder a du contenu de qualité.

# 3\. Architecture des 4 types de comptes

En complément de la Boutique Libre, WinPlus propose 4 types de comptes avec des abonnements adaptes à chaque profil d'utilisateur.

## 3.1 Compte Élève (Student)

**Public cible :** Collégiens, lycéens, étudiants universitaires, candidats aux concours (15-30 ans)

**Besoin principal :** Accéder aux épreuves, s'entrainer, suivre sa progression, préparer ses examens avec l'aide de l'IA

## 3.2 Compte Parent

**Public cible :** Parents de collégiens/lycéens, tuteurs éducatifs

**Besoin principal :** Suivre les progrès de ses enfants, acheter du contenu pour eux, recevoir des alertes de progression

## 3.3 Compte Professeur (Teacher)

**Public cible :** Enseignants, formateurs indépendants, créateurs de contenu académique

**Besoin principal :** Créer et publier du contenu, gérer des classes, monétiser son savoir, analyser les performances étudiantes

## 3.4 Compte Institution

**Public cible :** Ecoles privées/publiques, universités, académies de formation, centres d'excellence

**Besoin principal :** Gérer un établissement, licences en masse, SSO, tableau de bord d'établissement, rapports consolides

# 4\. Fonctionnalités gratuites (tous comptes)

Ces fonctionnalités sont disponibles pour tout utilisateur inscrit, quel que soit le type de compte :

### Catalogue et Découverte

- Parcourir le catalogue complet d'épreuves
- Filtrer par examen, matière, niveau, année
- Voir les prérequis et sujets relatifs
- Consulter les avis et notes des autres utilisateurs
- Aperçu du contenu
- Recherche avancée multicritères
- Recommandations basées sur l'activité

### Gestion du Compte

- Inscription / Connexion (Email)
- Authentification sécurisée (2FA optionnel)
- Vérification email
- Récupération de mot de passe
- Profil utilisateur de base + avatar
- Préférences de compte

### Fonctionnalités Sociales

- Ajouter des favoris (limite : 5 gratuits)
- Créer 1 collection de favoris
- Laisser des avis et notes sur les contenus
- Voir les statistiques globales de la plateforme

### Apprentissage Basique

- Accès limite aux exercices gratuits
- Visualiser les corrections taguées 'gratuit'
- Historique basique d'activité (30 jours)
- Notifications email basiques

### IA et Support

- Chat IA limite (5 messages/jour)
- FAQ et documentation
- Support par email

# 5\. Plans d'abonnement élèves

| **Fonctionnalité** | **Gratuit** | **Standard 2 500 XAF/mois** | **Premium 5 000 XAF/mois** | **Ultime 7 500 XAF/mois** |
| --- | --- | --- | --- | --- |
| **ACCES CONTENU** |     |     |     |     |
| **Epreuves téléchargeables** | 5/mois | Illimite | Illimite | Illimite |
| **Corrections complètes** | 5/mois | 30/mois | Illimite | Illimite |
| **Exercices pratiques** | Limite | 50/mois | Illimite | Illimite |
| **Codes promo** | \--- | 10% rabais | 15% rabais | 20% rabais |
| **OUTILS D'APPRENTISSAGE** |     |     |     |     |
| **Plans d'études personnalises** | \--- | Basique | Avance | Premium |
| **Quiz IA dynamiques** | 3/jour | 10/jour | Illimite | Illimite |
| **Chabot IA messages** | 5/jour | 50/jour | 200/jour | Illimite |
| **Analyse des lacunes** | \--- | Oui | Oui | Oui |
| **Recommandations adaptées** | \--- | Oui | Oui | Oui |
| **SUIVI ET RESULTATS** |     |     |     |     |
| **Tableau de bord** | Basic | Avance | Premium | Premium |
| **Historique d'apprentissage** | 30 jours | 90 jours | 1 an | Illimite |
| **Rapports de progression** | \--- | PDF | PDF + Excel | Tous formats |
| **Certificats de complétion** | \--- | PDF | PDF + Email | Signe |
| **FONCTIONNALITES SOCIALES** |     |     |     |     |
| **Favoris** | 5   | 50  | 200 | Illimite |
| **Collections de favoris** | 1   | 5   | 10  | Illimite |
| **Notes et tags de révision** | \--- | Oui | Oui | Oui |
| **Partage avec amis** | \--- | 3 amis | 10 amis | Illimite |
| **Groupes d'étude** | \--- | Créer 1 | Créer jusqu'à 5 | Créer 5+ |
| **SUPPORT** |     |     |     |     |
| **Email support (délai)** | 24h | 12h | 4h  | 1h  |
| **Chat support** | \--- | Bureau | 24/7 | 24/7 Priorité |
| **Session coaching** | \--- | \--- | 1/mois | 4/mois |

### Détails des plans élèves

**Plan GRATUIT :** Découverte de la plateforme. Accès limite au contenu. Idéal pour tester avant de s'engager.

**Plan STANDARD (2 500 XAF/mois ~ 4 EUR) :** Accès complet aux épreuves, outils d'apprentissage basiques, 1 groupe d'études. Meilleur rapport qualité/prix pour les élevés engagés.

**Plan PREMIUM (5 000 XAF/mois ~ 7,60 EUR) :** Toutes les corrections complètes, outils IA avances, groupes d'étude. Idéal pour la préparation aux concours.

**Plan ULTIME (7 500 XAF/mois ~ 11,40 EUR) :** Tout illimite, coaching premium, support prioritaire 24/7, certificats signes. Pour les candidats aux concours les plus exigeants.

**Options de réduction élèves**

Engagement annuel : -20% | Engagement trimestriel : -10% | Pack famille (3-5 enfants) : -15%

# 6\. Plans d'abonnement Parents

| **Fonctionnalité** | **Gratuit** | **Basique 5 000 XAF/mois** | **Complet 8 000 XAF/mois** | **Famille 12 000 XAF/mois** |
| --- | --- | --- | --- | --- |
| **GESTION ENFANTS** |     |     |     |     |
| **Enfants suivis** | 0   | 1   | 2-3 | 4-5 |
| **Voir tableau de bord enfant** | \--- | Oui | Oui | Oui |
| **Voir résultats détailles** | \--- | Oui | Oui | Oui |
| **Historique activité enfant** | \--- | 30 jours | 1 an | Illimite |
| **ACHAT ET RESSOURCES** |     |     |     |     |
| **Crédits mensuels enfants** | \--- | 10 000 XAF | 20 000 XAF | 40 000 XAF |
| **Acheter contenu pour enfants** | \--- | Oui | Oui | Oui |
| **Rabais groupe famille** | \--- | \--- | 5%  | 10% |
| **Partage entre enfants** | \--- | \--- | Oui | Oui |
| **SUIVI PEDAGOGIQUE** |     |     |     |     |
| **Alertes de progression** | \--- | Hebdo | Quotidien | Temps réel |
| **Rapports de synthèse** | \--- | Email | Email + PDF | Tous formats |
| **Recommandations personnalisées** | \--- | \--- | Oui | Oui |
| **Objectifs pour enfants** | \--- | 3   | Illimite | Illimite |
| **COMMUNICATION** |     |     |     |     |
| **Messagerie intra-plateforme** | \--- | \--- | Auto | Pro |
| **Contacter enseignants** | \--- | \--- | Oui | Oui |
| **Commentaires révision** | \--- | Oui | Oui | Oui |
| **SUPPORT** |     |     |     |     |
| **Support email** | \--- | 24h | 12h | 4h  |
| **Support chat** | \--- | Limite | Bureau | 24/7 |
| **Chat IA (questions enfants)** | \--- | 5/jour | 50/jour | Illimite |

### Détails des plans Parents

**Plan GRATUIT :** Suivi basique de son propre profil uniquement. Exploration du catalogue.

**Plan BASIQUE (5 000 XAF/mois ~ 7,60 EUR) :** Suivi d'1 enfant, tableau de bord, crédits 10 000 XAF/mois pour acheter du contenu.

**Plan COMPLET (8 000 XAF/mois ~ 12,20 EUR) :** Suivi de 2-3 enfants, rapports détaillés, alertes quotidiennes, messagerie avec enseignants.

**Plan FAMILLE (12 000 XAF/mois ~ 18,30 EUR) :** Suivi de 4-5 enfants, tout illimite, crédits 40 000 XAF/mois, support prioritaire 24/7.

**Options supplémentaires Parents**

Enfant additionnel : +2 000 XAF/mois | Coaching spécialisé : +1 500 XAF/session | Engagement annuel : -15%

# 7\. Plans d'abonnement Professeurs

| **Fonctionnalité** | **Gratuit** | **Fondateur 5 000 XAF/mois** | **Pro 10 000 XAF/mois** | **Expert 15 000 XAF/mois** |
| --- | --- | --- | --- | --- |
| **PUBLICATION DE CONTENU** |     |     |     |     |
| **Epreuves publiables/mois** | 0   | 5   | 20  | Illimite |
| **Corrections créables** | \--- | 5   | 20  | Illimite |
| **Quiz générés par IA** | \--- | 2/mois | 10/mois | Illimite |
| **Parcours d'apprentissage** | \--- | 1   | 5   | Illimite |
| **GESTION DE CLASSE** |     |     |     |     |
| **Classes gerees** | 0   | 1 (30 élèves) | 3 (jusqu'à 100 élèves) | Illimite |
| **Attribution ressources** | \--- | Manuel | Auto | IA  |
| **Suivi élèves** | \--- | Basic | Avance | Premium |
| **Evaluations créables** | \--- | 5/mois | Illimite | Illimite |
| **MONETISATION** |     |     |     |     |
| **Revenus de ventes** | \--- | 70% (toi) | 75% | 80% |
| **Commission plateforme** | \--- | 30% | 25% | 20% |
| **Paiements** | \--- | Sur demande | Automatique | Automatique |
| **OUTILS DE CREATION** |     |     |     |     |
| **Générateur de contenu IA** | \--- | Basique | Avance | Expert |
| **Editeur riche document** | \--- | Oui | Oui | Oui |
| **Uploadé fichiers (limite)** | \--- | 5 Go | 50 Go | Illimite |
| **Auto-publish calendrier** | \--- | \--- | Oui | Oui |
| **ANALYSES** |     |     |     |     |
| **Dashboard analytique** | \--- | Oui | Oui | Oui |
| **Rapports de performance** | \--- | Basique | Avance | Expert |
| **Exportation données** | \--- | CSV | Tous formats | Tous formats |
| **Insights pédagogiques** | \--- | \--- | Oui | Oui |
| **SUPPORT** |     |     |     |     |
| **Support email** | \--- | 24h | 12h | 4h  |
| **Support chat** | \--- | \--- | Bureau | 24/7 Priorité |
| **Webinaires formation** | \--- | Trimestriel | Mensuel | Live |

### Détails des plans Professeurs

**Plan GRATUIT :** Parcourir le contenu existant, consulter données pédagogiques. Evaluation de la plateforme.

**Plan FONDATEUR (5 000 XAF/mois ~ 7,60 EUR) :** Créer et vendre du contenu, 1 classe de 30 élèves, 70% des revenus, outils IA basiques.

**Plan PROFESSIONNEL (10 000 XAF/mois ~ 15,20 EUR) :** 3 classes, 75% des revenus, Marketplace, analytiques avancées, outils IA avances.

**Plan EXPERT (15 000 XAF/mois ~ 22,80 EUR) :** Classes illimitées, 80% des revenus (meilleur ratio), outils IA premium, Co création, support prioritaire 24/7.

**Options supplémentaires Professeurs**

Classes supplémentaires : +1 000 XAF/mois | Co-enseignants : +500 XAF/mois | Storage +100 Go : +500 XAF | Engagement annuel : -15%

# 8\. Plans d'abonnement Institutions

Les plans Institutions sont personnalises selon la taille et les besoins de chaque établissement. Les prix sont négociés directement avec l'équipe commerciale.

| **Fonctionnalité** | **Starter Gratuit** | **Etablissement Personnalise** | **Réseau Personnalise** | **Enterprise Personnalise** |
| --- | --- | --- | --- | --- |
| **SCOPE** |     |     |     |     |
| **Licences élèves** | 0   | 50-500 | 500-2000 | 2000+ |
| **Etablissements** | Test | 1 école | 2-5 écoles | National |
| **INTEGRATION** |     |     |     |     |
| **SSO (SAML/OAuth)** | \--- | SAML | SAML + OAuth | Tous protocoles |
| **API d'intégration** | \--- | Basique | Complete | Expert |
| **Intégration LMS** | \--- | \--- | Oui | Oui |
| **Sync annuaire (LDAP)** | \--- | \--- | Oui | Oui |
| **GESTION** |     |     |     |     |
| **Tableau de bord admin** | \--- | Oui | Oui | Oui |
| **Gestion élèves en masse** | \--- | CSV upload | CSV + API | Temps-réel |
| **Rôles et permissions** | \--- | 3 rôles | 5+ rôles | Custom |
| **Attribution ressources** | \--- | Manuel | Semi-auto | Full auto |
| **RAPPORTS** |     |     |     |     |
| **Rapports de base** | \--- | PDF | Tous formats | Temps-réel |
| **Analytics par classe** | \--- | Basique | Avance | Expert |
| **Dashboard personnalise** | \--- | \--- | Oui | Oui |
| **SUPPORT** |     |     |     |     |
| **Support email** | \--- | 12h | 8h  | 2h  |
| **Support téléphone** | \--- | \--- | Limite | 24/7 |
| **Account manager** | \--- | \--- | Oui | Oui (Senior) |
| **Formation staff** | \--- | A la demande | Incluse | Continue |

### Tarification indicative Institutions

**Plan STARTER (Gratuit) :** Test et évaluation. 10 comptes maximum. Démonstration uniquement.

**Plan ETABLISSEMENT :** 1 école, 50-500 élèves. Base : 50 000 XAF/mois + 1 000 XAF/élève. Exemple : 200 élèves = 70 000 XAF/mois (~107 EUR).

**Plan RESEAU :** 2-5 établissements, 500-2000 élèves. Base : 150 000 XAF/mois + 30 000 XAF/établissement + 500 XAF/élève au-delà de 150.

**Plan ENTERPRISE :** Déploiement national/international, 2000+ élèves. Tarification négociée. Volume discount 30-50%. Contrats annuels. SLA garanti 99,9%.

# 9\. Matrice de fonctionnalités par type de compte

Légende : Oui = Inclus dans tous les plans | Selon plan = Dépend du niveau d'abonnement | --- = Non disponible

| **Fonctionnalité** | **Élève** | **Parent** | **Professeur** | **Institution** |
| --- | --- | --- | --- | --- |
| **ACCES CONTENU** |     |     |     |     |
| **Consultation catalogue** | **Oui** | **Oui** | **Oui** | **Oui** |
| **Télécharger épreuves** | Selon plan | Selon plan | **Oui** | **Oui** |
| **Voir corrections complètes** | Selon plan | Selon plan | **Oui** | **Oui** |
| **Exercices pratiques** | Selon plan | Selon plan | **Oui** | **Oui** |
| **CREATION DE CONTENU** |     |     |     |     |
| **Créer/Publier épreuves** | \--- | \--- | Selon plan | Selon plan |
| **Créer corrections** | \--- | \--- | Selon plan | **Oui** |
| **Créer quiz** | \--- | \--- | Selon plan | **Oui** |
| **APPRENTISSAGE** |     |     |     |     |
| **Plan d'études personnalise** | Selon plan | \--- | \--- | **Oui** |
| **Quiz IA dynamiques** | Selon plan | \--- | **Oui** | **Oui** |
| **Chabot IA** | Selon plan | Selon plan | **Oui** | **Oui** |
| **Suivi de progression** | Selon plan | Selon plan | \--- | **Oui** |
| **Certificats** | Selon plan | \--- | \--- | **Oui** |
| **GESTION CLASSE/ENFANT** |     |     |     |     |
| **Créer classes/groupes** | \--- | \--- | Selon plan | **Oui** |
| **Suivre élèves/enfants** | \--- | Selon plan | Selon plan | **Oui** |
| **Alertes de progression** | **Oui** | Selon plan | \--- | **Oui** |
| **MONETISATION** |     |     |     |     |
| **Vendre du contenu** | \--- | \--- | Selon plan | **Oui** |
| **Recevoir revenus** | \--- | \--- | Selon plan | **Oui** |
| **ADMINISTRATION** |     |     |     |     |
| **Dashboard admin complet** | \--- | \--- | \--- | **Oui** |
| **Gestion en masse** | \--- | \--- | \--- | **Oui** |
| **SSO** | \--- | \--- | \--- | Selon plan |

# 10\. Bundles, réductions et engagements

## 10.1 Modèles d'engagement

**Mensuel :** Prix standard, sans engagement, résiliation a tout moment

**Trimestriel :** \-10% de réduction sur le prix mensuel

**Annuel :** \-20% de réduction (meilleur rapport qualité/prix)

**Multi annuel (3 ans) :** \-35% de réduction (institutions uniquement)

## 10.2 Bundles spéciaux

### Bundle 'Family Pack'

1 compte Parent + 3 comptes élèves. Economies groupées de -15%. Prix : 17 500 XAF/mois (au lieu de 22 500 XAF séparément).

### Bundle 'School'

1 compte Institution + 5 classes (200-300 élèves). Licence illimitée 1 an. Support prioritaire + formation staff incluse. Tarification négociée.

### Bundle 'Collectif Enseignants'

5 comptes Professeur regroupes. Revenue pool partage. Outils de collaboration inclus. Réductions sur les Co-inscrits.

# 11\. Etat d'implémentation actuel

## 11.1 Backend (ASP.NET Core)

| **Module** | **Détails** | **Statut** |
| --- | --- | --- |
| **Architecture générale** | ASP.NET Core 8, PostgreSQL, | **Fait** |
| **Gestion utilisateurs** | Inscription, connexion, 2FA, profil, avatar, email sécurisé | **Fait** |
| **Catalogue et contenu** | Subject model, 11 types d'examens, filtrage, pagination, sorting | **Fait** |
| **Système commercial** | Pricing Plan, Subscription, Cart, Order, Payment, Promo Code | **A faire** |
| **Apprentissage** | Enrollment, Course Progress, Learning History, Quiz Attempt, Certificate | **A faire** |
| **Chabot IA** | Messages, Conversations, Déposée API, historique persistant | **A faire** |
| **Favoris et social** | Favoris, Collections, Notifications, Analytics Events | **Fait** |
| **Rôles et autorisation** | Student, Teacher, Parent, Admin | **Fait** |
| **Compte Institution** | Rôle institution, plans spécifiques, SSO, bulk | **A faire** |

## 11.2 Frontend (React + TypeScript)

| **Module** | **Détails** | **Statut** |
| --- | --- | --- |
| **Authentification** | Login, Signup, email vérification, password reset, 2FA, logout | **Fait** |
| **Catalogue** | Catalog page, filters, search, details, pagination, favors, ratings, previews | **Fait** |
| **Panier et paiement** | Cart Context, Cart Service, AddToCart, Checkout, Payment Service | **Fait** |
| **Pricing** | Pricing page dynamique, 3 categories, plans depuis API, carousel responsive | **Fait** |
| **Dashboard et profil** | Dashboard par role, Profile, Edit, Preferences, Privacy, Notifications | **A faire** |
| **Apprentissage** | Study Progress, Study Streak, Upcoming Exams, History, Learning paths | **A faire** |
| **Chatbot** | Chatbot Window, Message Bubble, Composer, Image Upload, Math Editor, LaTeX | **A faire** |
| **Admin** | Admin Dashboard, User management, Content management, Analytics | **A faire** |
| **Boutique Libre (sans compte)** | Achat a l'unite, checkout invite, email delivery | **A faire** |
| **Compte Institution** | Institution dashboard, bulk creation, SSO frontend | **A faire** |

# 12\. Améliorations et fonctionnalités à venir

## 12.1 Court terme (1-3 mois)

### Priorité 1 : Boutique Libre

Implémentation du checkout sans compte, paiement invite Mobile Money, livraison par email, codes de téléchargement.

### Priorité 2 : Compte Institution

Rôle institution dans User, plans spécifiques dans PricingPlan, SSO SAML, création en masse d'élèves, admin Dashboard institution.

### Priorité 3 : Paiements

Intégration complète MTN Mobile Money et Orange Money. Strippe/Visa en Phase 2. 3D Secure. Facturation automatique conforme TVA 19,25% Cameroun.

### Priorité 4 : Fonctionnalités élèves

Groupes d'étude collaboratifs, sessions de coaching (calendrier + réservation), certificats signes numériquement, export rapports Excel.

### Priorité 5 : Fonctionnalités Parents

Dashboard multi-enfants complet, messagerie parent-professeur, alertes en temps réel, coaching enfants.

### Priorité 6 : Fonctionnalités Professeurs

UI de création de contenu détaillée, gestion de classe complète, auto-publics Schedule, co-teaching, marketplace.

## 12.2 Moyen terme (3-6 mois)

- Classe virtuelle live (video, partage d'ecran, whiteboard)
- Parcours d'apprentissage adaptatifs par IA
- Application mobile native (iOS + Android + mode hors ligne)
- Marketplace avancées (professeurs vendent directement)
- Compétitions inter-écoles et benchmark ING
- Forum communautaire par matière/concours

## 12.3 Long terme (6-12 mois)

- Gamification avancé (badges, leaderboards, points, rewards)
- Tuteur IA personnalise par matière (méthode socratique)
- Expansion internationale (multi-langue, multidevise, curricula régionaux)
- Solution white-label pour institutions
- Outils de recherche éducative et rapports anonymises

# 13\. Projections financières (Année 1 - conservateur)

| **Segment / Plan** | **Utilisateurs** | **Prix unitaire** | **CA annuel (XAF)** | **CA annuel (EUR)** |
| --- | --- | --- | --- | --- |
| **ELEVES** |     |     |     |     |
| **Boutique Libre (achats unitaires)** | 10 000 | ~500 XAF moyen | ~5M XAF | ~7 600 EUR |
| **Plan Standard (2 500 XAF)** | 500 | 2 500 XAF/mois | 15M XAF | 22 900 EUR |
| **Plan Premium (5 000 XAF)** | 200 | 5 000 XAF/mois | 12M XAF | 18 300 EUR |
| **Plan Ultime (7 500 XAF)** | 50  | 7 500 XAF/mois | 4,5M XAF | 6 900 EUR |
| **PARENTS** |     |     |     |     |
| **Plan Basique** | 100 | 5 000 XAF/mois | 6M XAF | 9 100 EUR |
| **Plan Complet** | 50  | 8 000 XAF/mois | 4,8M XAF | 7 300 EUR |
| **Plan Famille** | 10  | 12 000 XAF/mois | 1,4M XAF | 2 100 EUR |
| **PROFESSEURS** |     |     |     |     |
| **Plan Fondateur** | 50  | 5 000 XAF/mois | 3M XAF | 4 600 EUR |
| **Plan Pro** | 30  | 10 000 XAF/mois | 3,6M XAF | 5 500 EUR |
| **Plan Expert** | 10  | 15 000 XAF/mois | 1,8M XAF | 2 700 EUR |
| **Commissions ventes** | \--- | 20-30% du CA | ~3,3M XAF | ~5 000 EUR |
| **INSTITUTIONS** |     |     |     |     |
| **5 petites écoles** | 5   | ~55 000 XAF/mois | 3,3M XAF | 5 000 EUR |

**Estimation CA Année 1 (conservateur)**

Total estime : 63,7M - 70M XAF soit environ 97 000 - 107 000 EUR. Ce chiffre inclut la Boutique Libre qui représente un canal d'acquisition majeur.

# 14\. Notes d'implémentation technique

## 14.1 Base de données

**Plans de tarification :** Table PricingPlan (5-15 lignes par catégorie : Student, teachers, parents, institutions). Features stockées en JSON.

**Abonnements :** Table Suscription (user + plan + dates début/fin + statut + auto-renew).

**Boutique Libre :** Table Order avec type 'guest' pour les achats sans compte. Email de livraison stocke dans la commande.

**Facturation :** TVA 19,25% (Cameroun) appliquée automatiquement. Factures générées en PDF conforme CEMAC.

## 14.2 API Endpoints

Endpoints principaux du système de pricing et d'abonnement :

| **Endpoint** | **Description** |
| --- | --- |
| **GET /api/pricing/plans?category=X** | Liste des plans par catégorie |
| **POST /api/suscriptions** | Créer un abonnement |
| **GET /api/suscriptions/me** | Abonnement actuel de l'utilisateur |
| **POST /api/suscriptions/{id}/cancel** | Annuler un abonnement |
| **POST /api/suscriptions/{id}/upgrade** | Upgrader un abonnement |
| **POST /api/orders/guest** | Achat Boutique Libre (sans compte) |
| **POST /api/payments/mobile-money** | Paiement Mobile Money |
| **GET /api/subjects? Free=true** | Contenus gratuits |
| **GET /api/subjects/{id}/preview** | Preview d'un contenu |

## 14.3 Architecture DNS et domaines

### Infrastructure complète - Domaines et sous-domaines

Wupayinplus utilise une architecture multi-domaine optimisée pour performance et clarté. Tous les sous-domaines utilisent un certificat wildcard SSL unique pour simplifier la gestion.

**Certificat SSL :** `*.winplus.com` (Let's Encrypt - gratuit, auto-renouvelable)

#### Domaine Principal et Frontend

| **Domaine** | **Rôle** | **Service** | **Port interne** | **APIs principales** |
| --- | --- | --- | --- | --- |
| `winplus.com` | Accueil + Boutique Libre | React Frontend | 3000 | GET /catalog, POST /orders/guest, GET /payments |
| `www.winplus.com` | Alias accueil | React Frontend | 3000 | (même que winplus.com) |
| `winplus.com/app` | Dashboard utilisateurs | React Frontend | 3000 | GET /v1/dashboard, GET /v1/users/me |
| `winplus.com/admin` | Panel administrateur | React Frontend | 3000 | GET /v1/admin/*, POST /v1/admin/* |
| `winplus.com/teach` | Espace professeurs | React Frontend | 3000 | GET /v1/teacher/*, POST /v1/teacher/* |

#### Domaine API Backend

| **Domaine** | **Rôle** | **Service** | **Port interne** | **APIs principales** |
| --- | --- | --- | --- | --- |
| `api.winplus.com/v1/auth` | Authentification JWT | ASP.NET Core | 5001 | POST /login, POST /register, POST /refresh-token, POST /logout |
| `api.winplus.com/v1/subjects` | Catalogue épreuves/matières | ASP.NET Core | 5001 | GET /subjects, GET /subjects/{id}, GET /subjects?filter=exam, POST /subjects (teachers) |
| `api.winplus.com/v1/orders` | Commandes + panier | ASP.NET Core | 5001 | POST /orders, GET /orders/{id}, POST /orders/guest, GET /cart |
| `api.winplus.com/v1/payments` | Intégration paiements | ASP.NET Core | 5001 | POST /payments/mtn/initiate, GET /payments/{id}/status, POST /payments/webhook |
| `api.winplus.com/v1/users` | Profils et comptes | ASP.NET Core | 5001 | GET /users/me, PUT /users/{id}, GET /users/{id}/profile |
| `api.winplus.com/v1/subscriptions` | Abonnements | ASP.NET Core | 5001 | POST /subscriptions, GET /subscriptions/me, PUT /subscriptions/{id}, POST /subscriptions/{id}/cancel |
| `api.winplus.com/v1/pricing` | Plans tarifaires | ASP.NET Core | 5001 | GET /pricing/plans, GET /pricing/plans?category=student |
| `api.winplus.com/v1/analytics` | Rapports et statistiques | ASP.NET Core | 5001 | GET /analytics/dashboard, GET /analytics/user/{id}, POST /analytics/export |
| `api.winplus.com/v1/ai` | Deepseek IA | ASP.NET Core → Deepseek | 5001 | POST /ai/chat, GET /ai/quiz, POST /ai/generate-content |
| `api.winplus.com/v1/files` | Métadonnées fichiers | ASP.NET Core | 5001 | GET /files/{id}, POST /files/upload, GET /files/list |
| `api.winplus.com/health` | Health check | ASP.NET Core | 5001 | GET (response: 200 OK) |

#### Services spécialisés

| **Domaine** | **Rôle** | **Service** | **Port interne** | **APIs principales** |
| --- | --- | --- | --- | --- |
| `files.winplus.com` | Stockage documents PDF/images | Minio S3 | 9000 | GET /uploads/*, PUT /uploads/*, DELETE /uploads/* |
| `cdn.winplus.com` | Assets statiques frontend | Nginx + Cache | 80/443 | GET /js/*, GET /css/*, GET /images/* |
| `deepseek.winplus.com` | Service IA Deepseek | FastApi Python | 5000 | POST /chat, POST /quiz, POST /generate, GET /health |
| `analytics.winplus.com` | Dashboard reporting | ASP.NET Core | 5001 | GET /v1/analytics/dashboard, POST /v1/analytics/export |
| `mail.winplus.com` | Webhooks SendGrid | ASP.NET Core | 5001 | POST /webhooks/email/bounced, POST /webhooks/email/delivered |

### Routing Nginx (reverse proxy)

Le serveur Nginx (IP publique: 44.200.166.163) route les requêtes vers les services internes du VPC :

```
Utilisateur → HTTPS (port 443)
    ↓
Nginx Reverse Proxy (44.200.166.163)
    ├─ winplus.com → React Frontend (10.0.1.10:3000)
    ├─ api.winplus.com/* → ASP.NET Backend (10.0.1.20:5001)
    ├─ files.winplus.com → Minio Storage (10.0.1.40:9000)
    ├─ cdn.winplus.com → Static Cache (local or CDN)
    ├─ deepseek.winplus.com → Deepseek API (10.0.1.30:5000)
    ├─ analytics.winplus.com → Backend Analytics (10.0.1.20:5001/v1/analytics)
    └─ mail.winplus.com → Backend Webhooks (10.0.1.20:5001/webhooks)
```

### Instances EC2 requises (VPC privé)

| **Instance** | **Rôle** | **IP privée** | **Taille** | **Coût mensuel** |
| --- | --- | --- | --- | --- |
| win-frontend-ec2 | React app | 10.0.1.10 | t3.small | 10 EUR |
| win-backend-ec2 | ASP.NET Core + PostgreSQL | 10.0.1.20 | t3.large | 30 EUR |
| win-deepseek-ec2 | Deepseek FastApi API | 10.0.1.30 | t3.small | 10 EUR |
| win-storage-ec2 | Minio (S3-compatible) | 10.0.1.40 | t3.small | 10 EUR |
| win-nginx-ec2 | Reverse proxy + SSL | 10.0.1.50 (public: 44.200.166.163) | t3.micro | 5 EUR |

**Coût infrastructure mensuel total : ~65 EUR**

### Configuration DNS à créer

Ajouter ces entrées dans votre registrar (ex: GoDaddy, Namecheap) :

```
Type: A
Name: winplus.com
Value: 44.200.166.163

Type: A
Name: www.winplus.com
Value: 44.200.166.163

Type: A
Name: api.winplus.com
Value: 44.200.166.163

Type: A
Name: files.winplus.com
Value: 44.200.166.163

Type: A
Name: cdn.winplus.com
Value: 44.200.166.163

Type: A
Name: deepseek.winplus.com
Value: 44.200.166.163

Type: A
Name: analytics.winplus.com
Value: 44.200.166.163

Type: A
Name: mail.winplus.com
Value: 44.200.166.163

Type: MX (si besoin email)
Name: winplus.com
Value: mail.protonmail.com (ou autre provider)
```

### Configuration appsettings.Production.json à mettre à jour

```json
{
  "AppSettings": {
    "FrontendUrl": "https://winplus.com",
    "ApiBaseUrl": "https://api.winplus.com/v1",
    "FileStorageUrl": "https://files.winplus.com",
    "CdnUrl": "https://cdn.winplus.com",
    "DeepseekUrl": "https://deepseek.winplus.com",
    "AnalyticsUrl": "https://analytics.winplus.com"
  },
  "FileStorage": {
    "Provider": "Minio",
    "Endpoint": "http://win-storage-ec2:9000",
    "PublicUrl": "https://files.winplus.com"
  },
  "Deepseek": {
    "BaseUrl": "https://deepseek.winplus.com",
    "InternalUrl": "http://win-deepseek-ec2:5000"
  },
  "Cors": {
    "AllowedOrigins": [
      "https://winplus.com",
      "https://www.winplus.com",
      "https://api.winplus.com"
    ]
  }
}
```

### Checklist de création

- [ ] Domaines DNS pointés vers 44.200.166.163
- [ ] Certificat wildcard SSL généré (Let's Encrypt)
- [ ] Instance Nginx configurée avec reverse proxy
- [ ] Instance Frontend React déployée
- [ ] Instance Backend ASP.NET déployée
- [ ] Instance Deepseek FastApi déployée
- [ ] Instance Minio S3 déployée
- [ ] Sécurité groupes VPC configurée
- [ ] MongoDB/PostgreSQL backups configurés
- [ ] Monitoring + alertes CloudWatch

## 14.4 Roadmap technique

**Phase 1 (Mois 1-2) :** Boutique Libre + Institution Account type + Mobile Money

**Phase 2 (Mois 2-3) :** Strippe/Visa + Facturation automatique + Forum communautaire

**Phase 3 (Mois 3-4) :** Groupes d'étude + Coaching + Alertes temps réel

**Phase 4 (Mois 4-6) :** Analytics avancées + Marketplace + Rapports

**Phase 5+ (Mois 6+) :** App mobile + Classe virtuelle + Gamification

# 15\. Conclusion

Ce document présente la stratégie complète et évolutive de monétisation de WinPlus, articulée autour de deux piliers : la Boutique Libre pour l'accessibilité maximale et les abonnements pour la fidélisation et la valeur ajoutée.

**Accessibilité maximale :** La Boutique Libre permet à tout camerounais d'acheter une épreuve en 30 secondes via Mobile Money, sans compte.

**Flexibilité :** Chaque utilisateur paie uniquement pour ce qu'il utilise, que ce soit à l'unite ou par abonnement.

**Scalabilité :** Plans extensibles sans changement d'architecture. Du gratuit à l'Enterprise.

**Revenus diversifies :** Multiples flux (ventes unitaires, abonnements, commissions, licences institutions).

**Adapte au Cameroun :** Prix en XAF, paiement Mobile Money, TVA 19,25% CEMAC, contexte local.

_Document créé : 3 Mars 2026_

_Prochaine révision : 3 Juillet 2026 (Q3 review)_

_Mainteneur : Equipe Support & Product - WinPlus_