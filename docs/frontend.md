# Frontend React : premier jalon

Le jalon suivant ajoute `/login`, les JWT en mémoire et la navigation selon le rôle. Les instructions historiques de saisie manuelle d'un token sont remplacées par [Authentification JWT](authentication.md).

## Direction visuelle

Console d’opérations IT, destinée aux équipes support. L’objectif est de repérer
le travail actif, inspecter un ticket et retrouver une procédure. Le nombre de
tickets actifs mène le dashboard ; les urgences, la file et les catégories suivent.

Direction inspirée de Linear : navigation ardoise, typographie Inter hébergée
localement, surfaces claires, accent bleu, bordures fines et peu d’élévation.
L’identité est adaptée à une console opérationnelle plutôt qu’à un site marketing.
Le thème sombre utilise les mêmes composants. L’interface est en anglais, comme
les exemples de tickets. Aucun service externe de police ou d’image n’est appelé.

Le système partagé réside dans `frontend/src/styles/theme.css` : couleurs
sémantiques, échelle d’espacement de quatre pixels, typographie, rayons et mouvement.
`app.css` compose les composants à partir de cette couche, importée une seule fois.
Le mobile est la base ; les grilles s’élargissent avec des media queries min-width.
La priorité reste visible sous le titre d’un ticket sur petit écran.

Composants : Button, PageHeader, SectionHeader, TicketTable, StatusBadge,
PriorityBadge, ResourceState, EmptyState et FeatureNotice. Les actions ont des
états de survol, focus visible et pression ; les actions asynchrones sont
désactivées pendant leur chargement ; les filtres et thèmes déclarent leur
sélection ; le formulaire de connexion indique ses erreurs. Le mouvement respecte
prefers-reduced-motion. Navigation mobile au clavier, Échap et retour du focus.

## Pages et branchements

| Route | Comportement de ce jalon |
|---|---|
| `/` | Redirection vers `/dashboard` |
| `/dashboard` | Totaux des tickets, actifs, critiques et documents ; six tickets récents ; catégories de la page chargée |
| `/tickets`, `/tickets/` | Lecture, recherche, filtres de statut, filtre critique depuis le dashboard, pagination serveur |
| `/tickets/new`, `/tickets/:id/edit` | Création/modification validées, suggestions ML facultatives et pièces jointes ; voir [Formulaires](ticket-forms.md) |
| `/tickets/:id` | Informations, ML, analyse IA sauvegardée, sources, cinq actions et actualisation SignalR ; voir [Ticket Details](ticket-details.md) |
| `/ai-assistant` | Questions liées à un ticket, réponses structurées, sources, activité auditée et historique privé ; voir [AI Assistant](ai-assistant.md) |
| `/knowledge` | Bibliothèque paginée, recherche |
| `/knowledge/:id` | Lecture du contenu enregistré en texte, sans HTML interprété |
| `/analytics` | Distributions réelles de catégories, priorités et statuts des 100 derniers tickets au maximum |
| `/settings` | Thème, session privée de l’assistant et connexion/déconnexion SignalR facultative |

La seconde route `/tickets/` de la demande est prise comme la liste avec slash
terminal ; le détail est ajouté explicitement avec `:id`. Les liens inconnus et
identifiants mal formés affichent une issue utile.

Ticket Details connecte désormais analyse, investigation, affectation, statut et
commentaire. L’AI Assistant ajoute les investigations conversationnelles privées.
Les formulaires ajoutent désormais la création et la modification des tickets.
Les écritures documentaires, le chat sans ticket et les actions administratives restent hors de ce
jalon. Aucun bouton ne simule leur réussite.
Aucune donnée synthétique n’est embarquée comme secours dans le produit.

## Client API

`src/api/contracts.ts` reprend les DTOs Application, enums sérialisés en chaînes
et pagination `items/totalCount/page/pageSize`. Les lectures tickets, ticket,
knowledge et document sont complétées par le contexte et les cinq actions de
Ticket Details.
Le contrat existant `postman/specs/openapi.json` et les DTOs backend servent de référence.

Les compteurs utilisent les totalCount renvoyés par les filtres serveur. Le
nombre actif additionne Open et InProgress. Le compteur Critical couvre toutes
les priorités critiques actuelles, y compris les tickets résolus. Les snapshots
proviennent de plusieurs lectures ; ils ne constituent pas une transaction de
reporting. Les graphiques annoncent leur échantillon et ne prétendent fournir ni
historique global, ni taux de résolution IA, ni temps moyen de résolution.

Chaque lecture accepte AbortSignal et possède un délai de 15 secondes. Les
changements de page ou de filtre annulent les requêtes précédentes. ProblemDetails
est converti en message contrôlé ; les détails internes ne sont pas rendus.
Un document HTML de reverse proxy n’est pas accepté comme une réponse JSON.
Les résultats vides, les échecs et la relance sont rendus explicitement.

## SignalR et configuration

`src/realtime/client.ts` est indépendant de React. Il écoute les sept événements
existants, déduplique les eventId avec une mémoire bornée et invalide les lectures.
La révision seule ne sert pas à dédupliquer : ML et IA peuvent partager la même.
Après reconnexion automatique, les abonnements sont rétablis et les snapshots
relus. Un échec initial se relance depuis Settings ; les tentatives de reconnexion
automatique sont bornées à quatre. Un refus JoinTicket est indiqué dans le détail.

L’utilisateur saisit son jeton explicitement dans Settings. Le champ est vidé à
la connexion ; le jeton n’est conservé qu’en mémoire jusqu’à la déconnexion ou au
rechargement. Aucun jeton dans localStorage, sessionStorage, les logs, les fichiers
VITE_* ou le bundle. Seul le thème est mémorisé localement.

Les profils techniques, permissions et scopes restent ceux décrits dans
[SignalR](signalr.md) et [MCP](mcp-server.md). Le frontend n’élargit pas ces droits.
Un profil limité reçoit les événements des tickets rejoints ; l’observation des
nouveaux tickets exige AllTickets explicite. Les notifications sont best effort.

Le proxy de développement Vite transmet `/api` et `/hubs` au backend, y compris
les WebSockets. La cible par défaut est `http://127.0.0.1:5080`, configurable par
`API_PROXY_TARGET` côté serveur Vite (`frontend/.env.example`). Cela évite de modifier
les politiques CORS existantes. Le serveur reste lié à loopback.

```powershell
# Racine, terminal API
. ./scripts/Import-LocalEnvironment.ps1
dotnet run --project backend/src/ITTicketIntelligence.Api

# Autre terminal, racine
npm ci --prefix frontend
npm run dev --prefix frontend
```

Ouvrir `http://127.0.0.1:5173/dashboard`. PostgreSQL et les configurations du backend
doivent être disponibles. Sans backend, une erreur et une relance remplacent les
chiffres. La production nécessite un hôte statique avec fallback SPA et proxy
explicite pour `/api` et `/hubs` avant ce fallback ; `vite preview` n’est pas ce
reverse proxy. L’authentification utilisateur REST demeure un jalon backend distinct.

## Vérifications

```powershell
npm run build --prefix frontend
npm test --prefix frontend
```

Pour les tests HTTP et visuels isolés :

```powershell
# Racine, terminal simulation
postman simulation run postman/simulations/frontend-read-api/frontend-read-api.sim.yaml --port 4908 --no-history

# Terminal frontend (arrêter tout Vite existant sur 5173)
$env:API_PROXY_TARGET='http://127.0.0.1:4908/helpdesk'
npm run dev --prefix frontend

# Autre terminal, racine
npm run test:e2e --prefix frontend
npm run qa:capture --prefix frontend
```

La simulation sert uniquement les lectures prévues, depuis des tickets anglais
synthétiques. La recherche `simulate-unavailable` renvoie 503. Aucune simulation
n’est publiée ni appelée par défaut. Le contexte de reproduction est conservé à
côté de son fichier `.sim.yaml`.

Le script `npm run qa:live --prefix frontend` utilise un backend réel, un Vite
pointé vers lui et `FRONTEND_SMOKE_TOKEN` fourni temporairement dans l’environnement.
Il vérifie les totaux réels, la connexion, JoinTicket, une reconnexion WebSocket,
le réabonnement, la déconnexion et l’absence de stockage du jeton. Il n’écrit aucune
donnée métier. Ne pas enregistrer de trace réseau contenant un jeton réel.

Les contrôles et limites mesurés sont détaillés dans [frontend-validation.md](frontend-validation.md).

Références : [React Router](https://reactrouter.com/start/declarative/routing),
[client SignalR et reconnexion](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-10.0),
[proxy de développement Vite](https://vite.dev/config/server-options).
