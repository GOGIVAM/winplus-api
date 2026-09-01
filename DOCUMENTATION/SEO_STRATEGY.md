# WinPlus  Stratégie SEO & ASO

> Dernière mise à jour : 2026-08-31

---

## 1. Vue d'ensemble

WinPlus est positionné sur un marché de niche à fort potentiel : la préparation aux concours et examens camerounais. La compétition SEO locale reste **faible** (peu d'acteurs structurés), ce qui représente une fenêtre d'opportunité majeure pour dominer les premières positions en 12 à 18 mois.

### Objectifs SEO

| Horizon | Objectif |
|---|---|
| 3 mois | Top 3 sur les requêtes de marque + 5 requêtes cibles longue traîne |
| 6 mois | Top 5 sur toutes les requêtes concours camerounais |
| 12 mois | Extraits enrichis (FAQ, Sitelinks) + Knowledge Panel Google |
| 18 mois | Domination totale sur `[concours] cameroun [année]` |

---

## 2. Architecture SEO web

### 2.1 Signaux techniques (Core Web Vitals 2026)

Google utilise **LCP, INP et CLS** comme facteur de départage entre pages de même pertinence.

| Métrique | Seuil "Bon" | Seuil critique | Priorité |
|---|---|---|---|
| **LCP** (Largest Contentful Paint) | < 2,5 s | > 4 s | Critique |
| **INP** (Interaction to Next Paint) | < 200 ms | > 500 ms | Haute  43 % des sites échouent |
| **CLS** (Cumulative Layout Shift) | < 0,1 | > 0,25 | Haute |

**Actions à appliquer sur WinPlus :**
- Précharger les fonts critiques avec `<link rel="preload">` + `font-display: swap`
- Ajouter `width` et `height` explicites sur toutes les images/iframes (évite le CLS)
- Activer la compression Gzip/Brotli sur le serveur EC2
- Servir les images en format WebP avec lazy loading (`loading="lazy"`)
- Inliner le CSS critique (above-the-fold)
- Utiliser `<link rel="prefetch">` pour les pages de concours les plus visitées

**Outils de mesure :**
- [PageSpeed Insights](https://pagespeed.web.dev/)  benchmark mensuel obligatoire
- Google Search Console → Rapport "Expérience de la page"
- [web.dev/measure](https://web.dev/measure/)

---

### 2.2 Structured Data (Schema.org)

Le structured data n'est **pas** un facteur de classement direct, mais il conditionne :
- Les **rich results** (extraits enrichis) qui augmentent le CTR de 20 à 40 %
- Les **AI Overviews** (Google SGE)  expérience Searchengine Land 2025 : seules les pages avec schema valide ont été citées
- Le **Knowledge Graph** (entité reconnue par Google)

#### Types de schema implémentés sur WinPlus

| Page | Schema | Rich Result activé |
|---|---|---|
| Toutes les pages | `EducationalOrganization` | Knowledge Panel |
| Accueil | `WebSite` + `SearchAction` | Sitelinks Search Box |
| Accueil | `MobileApplication` | App rich result |
| `/concours` | `ItemList` (CourseList) | Carrousel de cours |
| `/subject/{id}` | `Course` | Carrousel de cours |
| `/faq` | `FAQPage` | Extraits FAQ (accordéon SERP) |
| `/concours/*/comment-reussir` | `Article` | Article enrichi |
| Pages de concours | `BreadcrumbList` | Fil d'Ariane SERP |

**Note (juin 2025)** : Google a retiré le rich result "course-info" individuel, mais le **Course List Carousel** reste actif. Continuer à implémenter `Course` pour les AI Overviews.

#### Règles de qualité Google
- Le schema doit correspondre **exactement** au contenu visible de la page
- Pas de markup sur du contenu caché ou invisible
- Valider avec [Rich Results Test](https://search.google.com/test/rich-results) après chaque déploiement

---

### 2.3 Balises meta  Standards 2026

| Balise | Limite | Bonne pratique |
|---|---|---|
| `<title>` | 50–60 caractères | Mot-clé principal en tête, marque en queue |
| `<meta description>` | 150–160 caractères | Inclure un appel à l'action et l'année |
| `og:title` | 60–90 caractères | Peut être légèrement plus long que le title |
| `og:image` | 1200×630 px | Format WebP, taille < 200 ko |
| `canonical` | 1 par page | Toujours présent, même sur la page canonique |

**Balises critiques manquantes à ajouter :**
- `theme-color`  pour Chrome Android (cohérence marque)
- `apple-touch-icon`  indexation iOS
- `site.webmanifest`  PWA + SEO mobile
- `hreflang`  version française / anglaise / camerounaise
- `og:locale` + `og:locale:alternate`

---

### 2.4 Sitemap XML

**Structure cible :**
```
sitemap-index.xml
├── sitemap-static.xml       (pages statiques)
├── sitemap-subjects.xml     (épreuves  générées dynamiquement)
└── sitemap-blog.xml         (articles/guides  à créer)
```

**Règles :**
- `<priority>` : 1.0 = accueil, 0.9 = pages concours, 0.8 = sous-pages, 0.6 = institutionnel, 0.3 = légal
- `<changefreq>` : `weekly` pour les pages de contenu, `monthly` pour les pages statiques
- Mettre à jour `<lastmod>` à chaque déploiement (format `YYYY-MM-DD`)
- Ajouter les balises `<image:image>` pour l'indexation Google Images (trafic supplémentaire)

---

### 2.5 Netlinking & autorité

La compétition SEO au Cameroun reste faible mais croît. Stratégie recommandée :

| Action | Impact | Effort |
|---|---|---|
| Backlink depuis journaux camerounais (Cameroon Tribune, La Nouvelle Expression) | Très fort | Moyen |
| Partenariat avec lycées et universités (lien depuis leur site) | Fort | Moyen |
| Articles invités sur 237actu, Camerpost, Investir au Cameroun | Fort | Faible |
| Google Business Profile  catégorie "Établissement d'enseignement" | Moyen | Très faible |
| Citations dans annuaires africains (Jumia, 237pages.com) | Faible | Très faible |
| Forum SEO communautaire (Facebook groupes parents/élèves) | Indirect | Faible |

**Priorité absolue** : Google Business Profile  gratuit, impact immédiat sur la recherche locale "winplus cameroun".

---

### 2.6 Mots-clés prioritaires

#### Requêtes à fort volume (Cameroun)

| Requête | Volume estimé | Difficulté | Page cible |
|---|---|---|---|
| `bac cameroun 2026` | Très fort | Faible | `/concours/bac` |
| `annales bac série c cameroun` | Fort | Faible | `/concours/bac/serie-c` |
| `concours ens yaoundé` | Fort | Faible | `/concours/ens` |
| `polytechnique cameroun annales` | Fort | Faible | `/concours/polytechnique` |
| `bepc cameroun 2026` | Fort | Faible | `/concours/bepc` |
| `concours fmsb médecine` | Moyen | Faible | `/concours/fmsb` |
| `résultats concours cameroun` | Très fort | Moyen | `/resultats-etudiants` |
| `révision bac série d` | Moyen | Très faible | `/concours/bac/serie-d` |
| `corrigé enam 2025` | Moyen | Très faible | `/concours/enam` |
| `tutorat IA cameroun` | Faible | Nul | `/` (différenciant) |

#### Longue traîne à créer

- `comment réussir le concours polytechnique yaoundé`
- `annales ens physique 2024 corrigé pdf`
- `programme bac série c cameroun 2026`
- `meilleure plateforme révision cameroun`
- `quiz mathématiques terminale c`

---

## 3. ASO  Google Play Store

### 3.1 Facteurs de classement Play Store 2026

Google Play utilise **5 signaux principaux** :

1. **Pertinence des métadonnées**  titre, courte description (80 chars), longue description (4 000 chars)
2. **Vélocité de téléchargements**  nombre de téléchargements dans les 7 premiers jours post-mise à jour
3. **Taux de conversion**  % de visiteurs qui installent (impacté par les screenshots + icône)
4. **Rétention in-app**  crash rate, DAU/MAU, sessions par utilisateur
5. **Android Vitals**  ANRs (Application Not Responding), crash rate < 1,09 %

### 3.2 Métadonnées optimisées (à appliquer)

**Titre de l'application** (30 chars max) :
```
WinPlus - Concours Cameroun
```

**Courte description** (80 chars) :
```
Annales corrigées, quiz IA & cours pour BAC, ENS, Polytechnique au Cameroun.
```

**Longue description** (4 000 chars)  stratégie :
- Paragraphe 1 : accroche avec mots-clés principaux (`concours cameroun`, `bac cameroun`, `annales corrigées`)
- Paragraphe 2 : fonctionnalités clés (WinAI, quiz adaptatifs, annales PDF)
- Paragraphe 3 : concours couverts (ENS, Polytechnique, ENAM, FMSB, ESSEC, ENSET, BAC, BEPC)
- Paragraphe 4 : social proof (témoignages, nombre d'étudiants)
- Paragraphe 5 : appel à l'action
- Répétition naturelle des mots-clés 3 à 5 fois (pas de keyword stuffing  pénalisé en 2026)

**Mots-clés à placer en titre + description :**
- `concours cameroun` (principal)
- `annales corrigées`
- `bac cameroun`
- `ens yaoundé`
- `polytechnique cameroun`
- `révision bac`
- `quiz mathématiques`
- `tutorat IA`

**Catégorie** : Éducation → Préparation aux examens

**Localisation** : Configurer une **Custom Store Listing** pour le Cameroun avec description en français + prix locaux.

### 3.3 Assets visuels

| Asset | Taille | Best practice |
|---|---|---|
| Icône | 512×512 px | Logo WinPlus sur fond teal, lisible à 48×48px |
| Feature Graphic | 1024×500 px | Texte court + étudiant + mockup de l'app |
| Screenshots | Min 2, max 8 | Montrer WinAI, quiz, annales PDF, résultats |
| Video promo | 30 à 120 s | Démo en français, sous-titres, pas de musique seule |

**Règle 2026** : les screenshots avec du texte sur fond sombre convertissent 18 % mieux que les screenshots d'interface brute. Ajouter des captions.

### 3.4 Ratings & Reviews

- Objectif : maintenir une note ≥ 4,3 / 5 (en dessous = pénalité de visibilité algorithmique)
- Répondre à **100 %** des avis 1 et 2 étoiles dans les 48h
- In-app : déclencher le `In-App Review API` après une session réussie (quiz complété avec bonne note), jamais en entrée
- Signaler les faux avis via Google Play Console

### 3.5 Fréquence de mise à jour

Mettre à jour les métadonnées toutes les **3 à 6 semaines** ou à chaque release pour signal de fraîcheur. Google booste temporairement les apps mises à jour.

---

## 4. SEO local Cameroun

### 4.1 Signaux spécifiques marché africain

- **Google Business Profile** est le levier #1 pour la recherche locale au Cameroun. Créer une fiche catégorisée "Établissement d'enseignement en ligne" à Douala + Yaoundé.
- Les backlinks provenant de médias camerounais reconnus valent plus que des centaines de liens génériques.
- **Pidgin** et bilinguisme : Google indexe les requêtes en français ET en anglais pour le Cameroun  créer des pages dédiées aux concours anglophones (GCE A-Level, O-Level) augmentera massivement la portée.
- La vitesse mobile est critique : plus de 80 % du trafic web camerounais est mobile sur des connexions 3G/4G. Viser LCP < 2,5 s sur réseau mobile.

### 4.2 Pages à créer (opportunités non couvertes)

| Page | Requête cible | Impact |
|---|---|---|
| `/concours/gce-a-level` | `gce a level cameroon` | Fort (anglophones) |
| `/concours/gce-o-level` | `gce o level cameroon` | Fort (anglophones) |
| `/blog/comment-reussir-bac-c` | `comment réussir bac c cameroun` | Moyen |
| `/blog/planning-revision-bac` | `planning révision bac` | Moyen |
| `/blog/bourses-etudes-cameroun` | `bourses étude cameroun 2026` | Fort |
| `/concours/ens/comment-reussir` | `comment réussir ens yaoundé` | Déjà en place |

---

## 5. Checklist d'implémentation

### Immédiat (déjà fait / à vérifier)

- [x] `seoService.ts`  meta title + description par route
- [x] `sitemap.xml`  pages statiques
- [x] `robots.txt`  protection pages privées
- [x] Schema `EducationalOrganization`
- [x] Schema `BreadcrumbList` sur les pages concours
- [x] Schema `Course` sur les pages épreuves

### Sprint SEO 1  Améliorations techniques

- [x] `index.html`  ajout `theme-color`, `og:*` par défaut, `WebSite` + `SearchAction` schema
- [x] `seoService.ts`  schema `FAQPage`, `Article`, `ItemList` (CourseList), `MobileApplication`
- [x] `sitemap.xml`  ajout pages légales + image tags
- [x] `robots.txt`  blocage robots IA (GPTBot, CCBot, Bytespider)
- [ ] Google Business Profile  créer/revendiquer la fiche
- [ ] Google Search Console  soumettre sitemap + demander indexation
- [ ] PageSpeed Insights  benchmark initial + corriger LCP > 2,5 s
- [ ] Ajouter `width`/`height` sur toutes les balises `<img>`
- [ ] Activer Brotli sur EC2 (nginx/Apache)
- [ ] `site.webmanifest`  ficher PWA complet

### Sprint SEO 2  Contenu

- [ ] Pages GCE A-Level / O-Level (marché anglophone)
- [ ] 4 articles de blog longue traîne (révisions, méthodes, résultats)
- [ ] Page `/resultats-etudiants` enrichie avec témoignages vidéo (VideoObject schema)
- [ ] FAQ enrichie avec 15+ questions/réponses (FAQPage schema)
- [ ] Méta données images : alt text descriptifs sur toutes les images

### Sprint SEO 3  Autorité

- [ ] Backlinks : 3 articles invités sur médias camerounais
- [ ] Partenariats lycées (lien entrant depuis site lycée)
- [ ] Google Business Profile : photos, horaires, Q&A
- [ ] Lancement campagne d'avis Google Play (In-App Review API)

---

## 6. Métriques de suivi

| KPI | Outil | Fréquence |
|---|---|---|
| Positions SERP | Google Search Console | Hebdomadaire |
| Impressions / clics | Google Search Console | Hebdomadaire |
| Core Web Vitals | PageSpeed Insights | Mensuelle |
| Classement Play Store | Google Play Console | Hebdomadaire |
| Taux de conversion app | Play Console (Acquisition) | Mensuelle |
| Note moyenne app | Play Console | Hebdomadaire |
| Trafic organique | Google Analytics 4 | Hebdomadaire |

---

## 7. Ressources

- [Google Search Central  Course Schema](https://developers.google.com/search/docs/appearance/structured-data/course)
- [Rich Results Test](https://search.google.com/test/rich-results)
- [PageSpeed Insights](https://pagespeed.web.dev/)
- [Google Business Profile](https://business.google.com/)
- [AppTweak  Google Play ranking factors 2026](https://www.apptweak.com/en/aso-blog/google-play-ranking-factors)
- [Core Web Vitals thresholds 2026](https://www.corewebvitals.io/core-web-vitals)
- [SEO local Cameroun  BEONWEB](https://www.beonweb.cm/en/blog/seo-cameroun-guide-debutant-apparaitre-google-2026)
- [Backlinks Afrique  237online](https://www.237online.com/backlinks-afrique-signal-seo/)
