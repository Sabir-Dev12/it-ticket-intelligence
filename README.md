<div align="center">

# IT Ticket Intelligence

### Le support IT, éclairé par l’intelligence artificielle.

Gestion des tickets · Classification ML.NET · Recherche documentaire RAG · Assistant IA

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)
![React 19](https://img.shields.io/badge/React-19-149ECA?style=flat-square)
![TypeScript](https://img.shields.io/badge/TypeScript-5.9-3178C6?style=flat-square)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-4169E1?style=flat-square)
![ML.NET](https://img.shields.io/badge/ML.NET-Predictions-512BD4?style=flat-square)

**Un espace de travail pour comprendre, prioriser et traiter les incidents.**  
Les suggestions de l’IA restent soumises à la décision humaine.

[Aperçu](#aperçu-de-lapplication) · [Fonctionnalités](#fonctionnalités) · [Architecture](#architecture) · [Installation](#installation-locale) · [Documentation](#documentation)

</div>

![Tableau de bord connecté avec le compte Admin de démonstration](docs/screenshots/roles/admin-dashboard.jpg)

## Le projet

IT Ticket Intelligence est un projet personnel de portfolio full stack qui réunit la gestion du support informatique et des outils d’aide au diagnostic. Le backend ASP.NET Core suit une Clean Architecture ; le frontend React et TypeScript propose une interface responsive avec thèmes clair et sombre.

Le parcours relie un ticket, des prédictions de catégorie/priorité/équipe, une base de connaissances et une investigation IA structurée. L’objectif est de rendre les informations utiles accessibles au même endroit, avec contrôle des accès, traçabilité et revue humaine.

## Fonctionnalités

| Module | Ce que l’application propose |
| :--- | :--- |
| **Tickets** | Création, modification, filtres, pagination, affectation, statuts, commentaires, pièces jointes, archivage et restauration. |
| **Intelligence ML.NET** | Trois prédicteurs indépendants : catégorie, priorité et équipe recommandée. Scores de confiance, modèles versionnés et abstention selon la politique serveur. |
| **Assistant IA** | Investigation et conversation liées à un ticket ; synthèses structurées, propositions et citations contrôlées. Historique privé et audit des outils consultés. |
| **Knowledge Base & RAG** | Documents texte/Markdown, articles, FAQ et procédures ; découpage en chunks, embeddings Ollama et recherche dans PostgreSQL. |
| **Dashboard & analytics** | Vue du support, tickets récents et répartition des catégories, priorités et statuts sur les données chargées. |
| **Temps réel** | Actualisation facultative via SignalR avec périmètre d’accès vérifié côté serveur. |
| **Comptes & sécurité** | Authentification JWT, rôles User/Agent/Support/Admin, administration des comptes et équipes. |
| **MCP** | Outils de lecture contrôlés, permissions par outil, périmètre des tickets et audit durable. |

> **La décision reste humaine.** Les prédictions et investigations n’affectent pas automatiquement une équipe, un statut ou une classification. Les résultats des modèles et leurs limites sont documentés.

## Aperçu de l’application

**Quatre comptes, quatre parcours.** Captures récentes de l’application connectée avec les rôles **Admin**, **User**, **Agent** et **Support**. Les noms, adresses et tickets affichés sont fictifs, dans une base de démonstration séparée.

**Faites défiler chaque galerie vers la droite** avec sa barre de défilement, le pavé tactile ou `Maj + molette`. **Cliquez sur une capture pour l’ouvrir en grand.** Chaque image conserve ses proportions et présente la page complète.

[Admin](#compte-admin) · [User](#compte-user) · [Agent](#compte-agent) · [Support](#compte-support)

### Compte Admin

Administration du workspace : dashboard, comptes, tickets, documents et intelligence.

<table>
<tr>
<th align="left">01&nbsp;·&nbsp;Tableau&nbsp;de&nbsp;bord&nbsp;—&nbsp;Admin</th>
<th align="left">02&nbsp;·&nbsp;Gestion&nbsp;des&nbsp;comptes&nbsp;—&nbsp;Admin</th>
<th align="left">03&nbsp;·&nbsp;Tous&nbsp;les&nbsp;tickets&nbsp;—&nbsp;Admin</th>
<th align="left">04&nbsp;·&nbsp;Détail&nbsp;et&nbsp;analyse&nbsp;d’un&nbsp;ticket&nbsp;—&nbsp;Admin</th>
<th align="left">05&nbsp;·&nbsp;Créer&nbsp;un&nbsp;ticket&nbsp;—&nbsp;Admin</th>
<th align="left">06&nbsp;·&nbsp;Modifier&nbsp;un&nbsp;ticket&nbsp;—&nbsp;Admin</th>
<th align="left">07&nbsp;·&nbsp;Assistant&nbsp;IA&nbsp;—&nbsp;Admin</th>
<th align="left">08&nbsp;·&nbsp;Base&nbsp;de&nbsp;connaissances&nbsp;—&nbsp;Admin</th>
<th align="left">09&nbsp;·&nbsp;Lire&nbsp;un&nbsp;guide&nbsp;—&nbsp;Admin</th>
<th align="left">10&nbsp;·&nbsp;Ajouter&nbsp;un&nbsp;document&nbsp;—&nbsp;Admin</th>
<th align="left">11&nbsp;·&nbsp;Analytics&nbsp;du&nbsp;workspace&nbsp;—&nbsp;Admin</th>
<th align="left">12&nbsp;·&nbsp;Réglages&nbsp;de&nbsp;la&nbsp;session&nbsp;—&nbsp;Admin</th>
</tr>
<tr>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-dashboard.jpg"><img src="docs/screenshots/roles/admin-dashboard.jpg" alt="Compte Admin — Tableau de bord" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-users.jpg"><img src="docs/screenshots/roles/admin-users.jpg" alt="Compte Admin — Gestion des comptes" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-tickets.jpg"><img src="docs/screenshots/roles/admin-tickets.jpg" alt="Compte Admin — Tous les tickets" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-ticket-detail.jpg"><img src="docs/screenshots/roles/admin-ticket-detail.jpg" alt="Compte Admin — Détail et analyse d’un ticket" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-create-ticket.jpg"><img src="docs/screenshots/roles/admin-create-ticket.jpg" alt="Compte Admin — Créer un ticket" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-edit-ticket.jpg"><img src="docs/screenshots/roles/admin-edit-ticket.jpg" alt="Compte Admin — Modifier un ticket" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-assistant.jpg"><img src="docs/screenshots/roles/admin-assistant.jpg" alt="Compte Admin — Assistant IA" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-knowledge.jpg"><img src="docs/screenshots/roles/admin-knowledge.jpg" alt="Compte Admin — Base de connaissances" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-knowledge-detail.jpg"><img src="docs/screenshots/roles/admin-knowledge-detail.jpg" alt="Compte Admin — Lire un guide" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-create-knowledge.jpg"><img src="docs/screenshots/roles/admin-create-knowledge.jpg" alt="Compte Admin — Ajouter un document" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-analytics.jpg"><img src="docs/screenshots/roles/admin-analytics.jpg" alt="Compte Admin — Analytics du workspace" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/admin-settings.jpg"><img src="docs/screenshots/roles/admin-settings.jpg" alt="Compte Admin — Réglages de la session" width="720"></a></td>
</tr>
</table>

### Compte User

Espace utilisateur : quatre tickets personnels dans ce jeu de démonstration.

<table>
<tr>
<th align="left">01&nbsp;·&nbsp;Mes&nbsp;tickets&nbsp;—&nbsp;User</th>
<th align="left">02&nbsp;·&nbsp;Détail&nbsp;d’un&nbsp;ticket&nbsp;personnel&nbsp;—&nbsp;User</th>
<th align="left">03&nbsp;·&nbsp;Créer&nbsp;une&nbsp;demande&nbsp;—&nbsp;User</th>
<th align="left">04&nbsp;·&nbsp;Analytics&nbsp;de&nbsp;mes&nbsp;tickets&nbsp;—&nbsp;User</th>
<th align="left">05&nbsp;·&nbsp;Réglages&nbsp;de&nbsp;ma&nbsp;session&nbsp;—&nbsp;User</th>
</tr>
<tr>
<td align="center" valign="top"><a href="docs/screenshots/roles/user-tickets.jpg"><img src="docs/screenshots/roles/user-tickets.jpg" alt="Compte User — Mes tickets" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/user-ticket-detail.jpg"><img src="docs/screenshots/roles/user-ticket-detail.jpg" alt="Compte User — Détail d’un ticket personnel" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/user-create-ticket.jpg"><img src="docs/screenshots/roles/user-create-ticket.jpg" alt="Compte User — Créer une demande" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/user-analytics.jpg"><img src="docs/screenshots/roles/user-analytics.jpg" alt="Compte User — Analytics de mes tickets" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/user-settings.jpg"><img src="docs/screenshots/roles/user-settings.jpg" alt="Compte User — Réglages de ma session" width="720"></a></td>
</tr>
</table>

### Compte Agent

Espace agent : huit tickets affectés à son équipe, assistant et guides de dépannage.

<table>
<tr>
<th align="left">01&nbsp;·&nbsp;Tickets&nbsp;de&nbsp;mon&nbsp;équipe&nbsp;—&nbsp;Agent</th>
<th align="left">02&nbsp;·&nbsp;Investigation&nbsp;d’un&nbsp;ticket&nbsp;—&nbsp;Agent</th>
<th align="left">03&nbsp;·&nbsp;Assistant&nbsp;IA&nbsp;—&nbsp;Agent</th>
<th align="left">04&nbsp;·&nbsp;Base&nbsp;de&nbsp;connaissances&nbsp;—&nbsp;Agent</th>
<th align="left">05&nbsp;·&nbsp;Consulter&nbsp;un&nbsp;guide&nbsp;—&nbsp;Agent</th>
<th align="left">06&nbsp;·&nbsp;Analytics&nbsp;de&nbsp;mon&nbsp;périmètre&nbsp;—&nbsp;Agent</th>
<th align="left">07&nbsp;·&nbsp;Réglages&nbsp;de&nbsp;la&nbsp;session&nbsp;—&nbsp;Agent</th>
</tr>
<tr>
<td align="center" valign="top"><a href="docs/screenshots/roles/agent-tickets.jpg"><img src="docs/screenshots/roles/agent-tickets.jpg" alt="Compte Agent — Tickets de mon équipe" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/agent-ticket-detail.jpg"><img src="docs/screenshots/roles/agent-ticket-detail.jpg" alt="Compte Agent — Investigation d’un ticket" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/agent-assistant.jpg"><img src="docs/screenshots/roles/agent-assistant.jpg" alt="Compte Agent — Assistant IA" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/agent-knowledge.jpg"><img src="docs/screenshots/roles/agent-knowledge.jpg" alt="Compte Agent — Base de connaissances" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/agent-knowledge-detail.jpg"><img src="docs/screenshots/roles/agent-knowledge-detail.jpg" alt="Compte Agent — Consulter un guide" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/agent-analytics.jpg"><img src="docs/screenshots/roles/agent-analytics.jpg" alt="Compte Agent — Analytics de mon périmètre" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/agent-settings.jpg"><img src="docs/screenshots/roles/agent-settings.jpg" alt="Compte Agent — Réglages de la session" width="720"></a></td>
</tr>
</table>

### Compte Support

Espace support : douze tickets du workspace, création, modification et investigation.

<table>
<tr>
<th align="left">01&nbsp;·&nbsp;Tous&nbsp;les&nbsp;tickets&nbsp;—&nbsp;Support</th>
<th align="left">02&nbsp;·&nbsp;Détail&nbsp;et&nbsp;analyse&nbsp;d’un&nbsp;ticket&nbsp;—&nbsp;Support</th>
<th align="left">03&nbsp;·&nbsp;Créer&nbsp;un&nbsp;ticket&nbsp;—&nbsp;Support</th>
<th align="left">04&nbsp;·&nbsp;Modifier&nbsp;un&nbsp;ticket&nbsp;—&nbsp;Support</th>
<th align="left">05&nbsp;·&nbsp;Assistant&nbsp;IA&nbsp;—&nbsp;Support</th>
<th align="left">06&nbsp;·&nbsp;Base&nbsp;de&nbsp;connaissances&nbsp;—&nbsp;Support</th>
<th align="left">07&nbsp;·&nbsp;Consulter&nbsp;un&nbsp;guide&nbsp;—&nbsp;Support</th>
<th align="left">08&nbsp;·&nbsp;Analytics&nbsp;du&nbsp;support&nbsp;—&nbsp;Support</th>
<th align="left">09&nbsp;·&nbsp;Réglages&nbsp;de&nbsp;la&nbsp;session&nbsp;—&nbsp;Support</th>
</tr>
<tr>
<td align="center" valign="top"><a href="docs/screenshots/roles/support-tickets.jpg"><img src="docs/screenshots/roles/support-tickets.jpg" alt="Compte Support — Tous les tickets" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/support-ticket-detail.jpg"><img src="docs/screenshots/roles/support-ticket-detail.jpg" alt="Compte Support — Détail et analyse d’un ticket" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/support-create-ticket.jpg"><img src="docs/screenshots/roles/support-create-ticket.jpg" alt="Compte Support — Créer un ticket" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/support-edit-ticket.jpg"><img src="docs/screenshots/roles/support-edit-ticket.jpg" alt="Compte Support — Modifier un ticket" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/support-assistant.jpg"><img src="docs/screenshots/roles/support-assistant.jpg" alt="Compte Support — Assistant IA" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/support-knowledge.jpg"><img src="docs/screenshots/roles/support-knowledge.jpg" alt="Compte Support — Base de connaissances" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/support-knowledge-detail.jpg"><img src="docs/screenshots/roles/support-knowledge-detail.jpg" alt="Compte Support — Consulter un guide" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/support-analytics.jpg"><img src="docs/screenshots/roles/support-analytics.jpg" alt="Compte Support — Analytics du support" width="720"></a></td>
<td align="center" valign="top"><a href="docs/screenshots/roles/support-settings.jpg"><img src="docs/screenshots/roles/support-settings.jpg" alt="Compte Support — Réglages de la session" width="720"></a></td>
</tr>
</table>

<details>
<summary><strong>Écran de connexion commun aux quatre rôles</strong></summary>

![Connexion à IT Ticket Intelligence](docs/screenshots/roles/login.jpg)

</details>

## Architecture

```mermaid
flowchart LR
    UI["React + TypeScript"] --> API["ASP.NET Core API"]
    API --> APP["Application : cas d’usage et interfaces"]
    APP --> DOMAIN["Domain : entités et invariants"]
    API --> INFRA["Infrastructure : adaptateurs"]
    INFRA --> APP
    INFRA --> DB[("PostgreSQL / EF Core")]
    INFRA --> ML["ML.NET : modèles versionnés"]
    INFRA --> AI["Ollama / LLM compatible OpenAI"]
    API --> RT["SignalR + MCP"]
```

| Couche | Responsabilité |
| :--- | :--- |
| `Domain` | Modèle métier et invariants, sans dépendance externe. |
| `Application` | Cas d’usage, contrats, politiques et interfaces de persistance/IA. |
| `Infrastructure` | EF Core, migrations, ML.NET, RAG, LLM et implémentations des interfaces. |
| `Api` / `Worker` | Composition DI et hébergement ; le Worker démarre sans traitement enregistré. |
| `frontend` | Interface React, validation Zod, navigation et client SignalR. |

```text
.
├── backend/       Solution .NET : domaine, application, infrastructure, API, Worker, tests
├── frontend/      Application React + TypeScript + Vite
├── docs/          Documentation fonctionnelle, technique et captures
├── models/        Bundles ML.NET versionnés et rapports d’évaluation
├── data/tickets/  Dataset synthétique et politique d’annotation
├── postman/       Collections, contrats et scénarios de test
├── scripts/       Initialisation locale et vérifications
├── samples/       Exemples de prédiction
└── tools/         Outils complémentaires
```

## Installation locale

### Prérequis

- SDK **.NET 10.0.401** ou patch compatible avec `global.json`.
- **Node.js 24**, npm et Git.
- **Docker Desktop**, moteur Linux actif, pour PostgreSQL.
- **Ollama**, si vous souhaitez activer les fonctions locales de génération et d’embeddings.

### 1. Récupérer et préparer le projet

```powershell
git clone https://github.com/Sabir-Dev12/it-ticket-intelligence.git
cd it-ticket-intelligence
./scripts/Initialize-Postgres.ps1
./scripts/Initialize-Jwt.ps1
```

Les scripts génèrent les secrets locaux dans `.env`, ignoré par Git. Consulter `.env.example` pour les options disponibles. Avant le premier démarrage, définir `AUTH__BootstrapEmail` et `AUTH__BootstrapPassword` dans le `.env` local pour créer le premier administrateur ; choisir un mot de passe de 14 à 256 caractères. Retirer ces deux variables après le bootstrap. Aucun identifiant par défaut n’est livré.

### 2. Appliquer les migrations et compiler

```powershell
. ./scripts/Import-LocalEnvironment.ps1
dotnet tool restore
dotnet restore backend/ITTicketIntelligence.sln
dotnet ef database update --project backend/src/ITTicketIntelligence.Infrastructure --startup-project backend/src/ITTicketIntelligence.Api --configuration Release
dotnet build backend/ITTicketIntelligence.sln --configuration Release -m:1
npm ci --prefix frontend
npm run build --prefix frontend
```

### 3. Lancer l’application

Terminal backend :

```powershell
. ./scripts/Import-LocalEnvironment.ps1
dotnet run --project backend/src/ITTicketIntelligence.Api
```

Terminal frontend :

```powershell
npm run dev --prefix frontend
```

| Service | Adresse locale |
| :--- | :--- |
| Application | http://127.0.0.1:5173 |
| API | http://localhost:5080 |
| OpenAPI en Development | http://localhost:5080/openapi/v1.json |

La connexion utilisateur est requise. Les fonctions RAG/LLM demandent leur propre configuration et des documents indexés : voir [Ollama local](docs/ollama-local.md) et [pipeline RAG](docs/rag-pipeline.md). Docker Compose démarre la base de données ; les hôtes API et frontend se lancent avec les commandes ci-dessus.

## Vérification et limites

Contrôles exécutés pour cette publication : **build backend Release réussi** (0 avertissement, 0 erreur), **build frontend réussi**, **328/328 tests unitaires backend** et **36/36 tests frontend**. La suite d’intégration complète n’a pas été réexécutée pour cette publication.

```powershell
npm test --prefix frontend
dotnet test backend/tests/ITTicketIntelligence.UnitTests --configuration Release -m:1
```

La suite d’intégration utilise PostgreSQL et peut être lancée avec `dotnet test backend/ITTicketIntelligence.sln --configuration Release -m:1`. Des tests HTTP historiques restent à adapter à l’authentification JWT : la réussite des builds et des tests unitaires ne valide pas toute cette suite. Voir [les limites de validation](docs/authentication.md#vérification).

- Les modèles sont entraînés sur un petit dataset **synthétique**. Leurs performances ne justifient pas une application automatique des suggestions.
- Les analytics décrivent les tickets chargés ; ils ne mesurent pas un historique complet ni un taux de résolution par l’IA.
- La génération peut produire des hypothèses ou des résultats insuffisamment sourcés, à examiner avant toute action.
- Les JWT actuels restent valides jusqu’à révocation ; le frontend les garde en mémoire. Recharger l’onglet exige une reconnexion. Voir [authentification et sécurité](docs/authentication.md).
- Ce dépôt présente un projet de développement/portfolio ; le durcissement et le déploiement de production nécessitent une validation dédiée.

## Documentation

| Sujet | Référence |
| :--- | :--- |
| Modèle métier & persistance | [Domaine](docs/domain-model.md) · [Persistance](docs/persistence.md) |
| Tickets & formulaires | [API tickets](docs/ticket-management.md) · [Formulaires](docs/ticket-forms.md) · [Détail d’un ticket](docs/ticket-details.md) |
| Machine learning | [Catégorie](docs/category-classification.md) · [Priorité](docs/priority-classification.md) · [Équipe](docs/team-recommendation.md) · [Pipeline](docs/ticket-intelligence-pipeline.md) |
| Intelligence artificielle | [Agent](docs/ai-ticket-agent.md) · [Assistant](docs/ai-assistant.md) · [Abstraction LLM](docs/llm-abstraction.md) |
| Recherche documentaire | [Knowledge Base](docs/knowledge-base.md) · [RAG](docs/rag-pipeline.md) · [pgvector](docs/knowledge-pgvector.md) |
| Accès & intégrations | [Authentification](docs/authentication.md) · [Comptes](docs/user-accounts.md) · [SignalR](docs/signalr.md) · [MCP](docs/mcp-server.md) |
| Interface & données | [Frontend](docs/frontend.md) · [Dataset synthétique](data/tickets/README.md) |

---

<details>
<summary><strong>Référence technique détaillée et historique des modules</strong></summary>

## Référence technique détaillée

Plateforme Clean Architecture .NET avec module Ticket Management, persistance EF Core/PostgreSQL et frontend React/TypeScript.

Le [modèle du domaine](docs/domain-model.md) décrit les entités, relations et invariants. La [documentation de persistance](docs/persistence.md) décrit les mappings, les migrations et la gestion des secrets. Le [contrat Ticket Management](docs/ticket-management.md) décrit les routes, DTOs, filtres, révisions et erreurs HTTP.

Le [dataset ML.NET préparé](data/tickets/README.md) contient 240 tickets synthétiques en anglais, séparés en training/validation/test par scénario, avec annotations Category/Priority/RecommendedTeam et contrôles reproductibles. La préparation des données ne réalise aucun entraînement de modèle.

Le [classifieur de catégories ML.NET](docs/category-classification.md) dispose d’un entraînement console séparé, d’un bundle versionné sauvegardé et d’un port de prédiction chargé au démarrage de l’API. `POST /api/predictions/category` retourne catégorie, confiance et version. Les métriques de la première baseline sont publiées ; ses performances restent insuffisantes pour appliquer automatiquement les catégories.

Le [modèle de priorité ML.NET](docs/priority-classification.md) utilise titre, description et catégorie fournie à la création. Son entraînement compare des variantes pondérées/non pondérées sur validation, puis évalue le modèle sélectionné sur test. `POST /api/predictions/priority` retourne priorité prédite, confiance et version. Le rappel Critical insuffisant est détaillé dans le rapport.

Le [modèle de recommandation d’équipe](docs/team-recommendation.md) est indépendant de Category/Priority et utilise titre + description. `POST /api/predictions/team` retourne une suggestion ou `team: null` avec `needsHumanReview: true` sous le seuil serveur. Aucune affectation n’est exécutée automatiquement, quelle que soit la confiance.

Le [Ticket Intelligence Pipeline](docs/ticket-intelligence-pipeline.md) expose `POST /api/tickets/{id}/analyze` : Category → Priority → Team, agrégation, puis sauvegarde atomique des trois prédictions pour la révision analysée. Il conserve l'abstention et demande une revue si un score est insuffisant. Il ajoute uniquement des prédictions à l'historique du ticket.

Le [module Knowledge Base](docs/knowledge-base.md) gère documents, articles, guides de dépannage, FAQs et procédures via `/api/knowledge`. Les contenus texte/Markdown sont extraits, nettoyés, découpés en chunks et enrichis de métadonnées. Les interfaces pour embeddings et vector store sont prêtes ; aucun LLM n'est connecté.

Le [pipeline RAG](docs/rag-pipeline.md) indexe ces chunks avec des embeddings Ollama locaux et les recherche par cosinus dans PostgreSQL. `POST /api/rag/documents/{id}/index` indexe une version préparée ; `POST /api/rag/retrieve` retourne les chunks courants, leurs scores et métadonnées. Aucune réponse LLM n'est générée.

L’[abstraction LLM](docs/llm-abstraction.md) expose `ILLMService.GenerateAsync` pour un prompt et un contexte RAG explicite, avec réponse structurée, citations validées, timeouts, cancellation et retries bornés. Ollama et OpenAICompatible se configurent par variables `LLM__*`. Le transport LLM n’a aucun accès au DbContext.

L’[AI Ticket Agent](docs/ai-ticket-agent.md) expose `POST /api/tickets/{id}/investigate` : ML → RAG → collecte d’informations par six outils de lecture → synthèse structurée avec sources vérifiées. Il distingue faits, prédictions et hypothèses, impose une approbation humaine et archive ses propositions avec une trace d’audit. Il ne modifie jamais le statut, la classification ou l’affectation du ticket.

Le [serveur MCP](docs/mcp-server.md) expose ces outils via `/mcp`, avec authentification par token configuré hors Git, permissions par outil, scope des tickets et audit durable. Le [catalogue documenté](docs/mcp-tools.md) fournit les schémas d’entrée et de sortie. L’agent utilise le même service applicatif, limité au ticket courant.

Le [backend SignalR](docs/signalr.md) expose `TicketHub` sur `/hubs/tickets`. Les services Application publient les événements après commit ; les groupes respectent les permissions et le scope des tickets. Le [frontend React](docs/frontend.md) propose un dashboard connecté, la consultation des tickets et documents, les analytics sur échantillon, deux thèmes et une connexion SignalR facultative. La page [Ticket Details](docs/ticket-details.md) connecte les analyses ML/IA, l’affectation, le changement de statut et les commentaires, avec actualisation des résultats sauvegardés et contrôle de révision.

L’[AI Assistant](docs/ai-assistant.md) permet de poser une question sur un ticket,
de suivre les lectures d’outils auditées et de consulter les réponses sourcées
dans un historique privé persistant. Un profil de session doit être associé à un
utilisateur actif ; les corrections restent des propositions soumises à revue.

Les [formulaires de tickets](docs/ticket-forms.md) ajoutent création et modification
avec validation Zod/serveur, suggestions ML.NET à confirmer et pièces jointes
limitées et contrôlées, persistées dans PostgreSQL. Ils protègent les brouillons
et distinguent un ticket sauvegardé d’un upload encore non confirmé.

## Prérequis

- SDK .NET 10.0.401 ou patch compatible (voir `global.json`).
- Node.js 24 et npm.
- Docker avec le moteur Linux démarré pour PostgreSQL et les tests de persistance.

## Dépendances entre projets

- Domain : aucun projet ni package externe.
- Application → Domain.
- Infrastructure → Application (et Domain par transitivité).
- Api et Worker → Application + Infrastructure : racines de composition DI.
- Application définit les interfaces de repositories et d’unité de travail ; Infrastructure les implémente et enregistre AppDbContext.

Les propriétés `Nullable`, `ImplicitUsings` et `TreatWarningsAsErrors` sont activées dans `Directory.Build.props` pour tous les projets .NET.

## Compiler et tester

Depuis la racine du dépôt :

```powershell
./scripts/Initialize-Postgres.ps1
. ./scripts/Import-LocalEnvironment.ps1
dotnet tool restore
dotnet restore backend/ITTicketIntelligence.sln
dotnet ef database update --project backend/src/ITTicketIntelligence.Infrastructure --startup-project backend/src/ITTicketIntelligence.Api --configuration Release
dotnet build backend/ITTicketIntelligence.sln --no-restore --configuration Release
dotnet test backend/ITTicketIntelligence.sln --no-build --configuration Release
npm ci --prefix frontend
npm run build --prefix frontend
```

Les tests xUnit vérifient le domaine, l’architecture, le démarrage des hôtes et la persistance sur PostgreSQL réel dans une base temporaire isolée. Les tests d’architecture inspectent les projets source et s’exécutent donc depuis le dépôt.

## Démarrer les hôtes

L'intégration du chat Ollama local, ses routes et ses tests sont décrits dans [Ollama local](docs/ollama-local.md).

Dans trois terminaux distincts :

```powershell
. ./scripts/Import-LocalEnvironment.ps1
dotnet run --project backend/src/ITTicketIntelligence.Api
. ./scripts/Import-LocalEnvironment.ps1
dotnet run --project backend/src/ITTicketIntelligence.Worker
npm run dev --prefix frontend
```

API : `http://localhost:5080`. Frontend : `http://127.0.0.1:5173`.

L’API expose la création, consultation, liste paginée, modification, affectation, changement de statut, ajout de commentaire, archivage et restauration des tickets. En Development, le contrat OpenAPI est disponible à `http://localhost:5080/openapi/v1.json`. Le Worker démarre sans traitement enregistré. Le frontend s’ouvre sur `/dashboard` ; Vite relaie `/api` et `/hubs` vers le backend local sur 5080.

Chaque modification exige la `revision` courante retournée par le serveur. `DELETE` archive sans effacer l’historique ; `POST /api/tickets/{id}/restore` restaure le ticket. Le créateur et l’auteur des commentaires proviennent maintenant du JWT ; les équipes référencées doivent exister. Voir [Authentification](docs/authentication.md) pour le premier administrateur, les rôles et les limites de validation actuelles des tests historiques.

PostgreSQL est lancé par Docker Compose avec un secret généré dans `.env`, ignoré par Git. Aucun secret n’est inclus dans les appsettings. La connexion JWT protège REST, SignalR, AI et MCP ; le frontend redirige vers `/login` sans token. Le workflow d’approbation/application des suggestions et la conteneurisation des hôtes restent des étapes distinctes.

</details>
