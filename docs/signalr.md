# SignalR : notifications backend des tickets

Mise à jour : le navigateur utilise désormais le JWT de connexion et les droits sont revérifiés à chaque notification. Les profils et résultats historiques ci-dessous décrivent le jalon initial. Voir [Authentification JWT](authentication.md) pour la configuration actuelle et l'état des tests.

`TicketHub` est exposé sur `/hubs/tickets`. Sa première livraison était limitée
au backend. Le [premier frontend React](frontend.md) ajoute désormais le client
facultatif, avec connexion, abonnement et relecture après reconnexion.

## Publication et couches

Application définit `TicketEvent`, `TicketEventName`, le port `ITicketEventSink`
et `TicketEventNotifier`. Les services applicatifs appellent le notifier après
leur sauvegarde réussie. API implémente le transport par
`SignalRTicketEventSink`, basé sur `IHubContext<TicketHub>`, et compose les
dépendances. Les controllers ne publient aucun événement directement.
Domain et Infrastructure ne référencent pas SignalR. Aucune migration n’est
nécessaire.

| Événement | Service qui publie après sauvegarde |
|---|---|
| TicketCreated | TicketCommandHandler, création |
| TicketUpdated | TicketCommandHandler, modification ; archivage/restauration effectifs |
| TicketAssigned | TicketCommandHandler, affectation ou désaffectation |
| TicketStatusChanged | TicketCommandHandler, changement de statut |
| CommentAdded | TicketCommandHandler, ajout de commentaire |
| PredictionCompleted | TicketIntelligencePipeline, après validation du lot de trois prédictions |
| AIAnalysisCompleted | TicketAgent, après persistance de la proposition et de son audit terminal |

Les erreurs de validation, conflits de révision et sauvegardes refusées ne
déclenchent pas une notification de réussite. Une analyse IA peut échouer après
la sauvegarde de ses prédictions ML ; PredictionCompleted reste alors valide,
mais AIAnalysisCompleted n’est pas envoyé.

## Contrat des événements

Le nom de la méthode cliente est exactement le nom de l’événement ci-dessus.
Son unique argument est une enveloppe JSON :

```json
{
  "eventId": "11111111-1111-4111-8111-111111111111",
  "name": "PredictionCompleted",
  "ticketId": "22222222-2222-4222-8222-222222222222",
  "ticketRevision": 3,
  "occurredAt": "2026-10-26T12:00:00Z",
  "relatedId": "33333333-3333-4333-8333-333333333333"
}
```

`relatedId` identifie le lot ML ou le run IA pour les événements correspondants ;
il est null pour les autres événements. L’enveloppe ne contient ni description,
commentaire, réponse IA, token ou donnée personnelle du ticket. C’est une
notification d’invalidation : le client recharge un snapshot autorisé.

La révision est celle de l’opération ou de l’entrée analysée. Les prédictions et
l’analyse IA n’incrémentent pas la révision du ticket. Plusieurs événements à
la même révision sont donc normaux : dédupliquer par eventId, pas uniquement
par ticketRevision. Les notifications concurrentes peuvent arriver dans un
ordre différent de leur création ; le snapshot reste la source de vérité.

## Méthodes du hub

| Méthode | Argument | Résultat |
|---|---|---|
| JoinTicket | ticketId UUID non vide | TicketDto courant, incluant commentaires et prédictions |
| LeaveTicket | ticketId UUID non vide | Fin de l’abonnement au groupe privé du ticket |
| GetTicket | ticketId UUID non vide | TicketDto courant, avec les mêmes contrôles d’accès |

Le serveur compose lui-même les noms des groupes. Il vérifie le droit de
lecture et le scope du ticket pour chaque méthode. Un ticket absent renvoie
une HubException contrôlée ; les erreurs détaillées du serveur sont désactivées.
JoinTicket rejoint le groupe avant la lecture du snapshot pour fermer la
fenêtre entre abonnement et chargement ; un échec de lecture retire ce groupe.

Les clients limités reçoivent uniquement les tickets rejoints et autorisés.
Un profil explicitement AllTickets=true rejoint le groupe d’observation global
à la connexion et reçoit aussi TicketCreated pour les nouveaux tickets. Il
ne rejoint pas en plus le groupe privé lors de JoinTicket, pour éviter les
doublons. LeaveTicket ne retire pas ce droit d’observation globale ; fermer
la connexion met fin à cette session.

## Authentification et autorisation

Le hub utilise les profils de tokens techniques du [module MCP](mcp-server.md) :
`MCP__Clients__N__TokenHash`, Subject, Permissions, TicketIds et AllTickets.
La permission `tickets.read` est obligatoire à la connexion. Le scope est
appliqué indépendamment du ticketId demandé par le client. Sans profil configuré,
aucun accès au hub n’est accordé.

Ce mécanisme est l’authentification par token opaque existante du projet. Il
n’ajoute pas de module de login utilisateur, ni de JWT/SSO. Les profils sont
chargés depuis l’environnement ; leur rotation/révocation requiert le
redémarrage de l’hôte, ce qui ferme les connexions existantes. Ces tokens
techniques n’ont pas d’expiration automatique. L’option de fermeture sur
expiration est activée pour une future identité possédant une expiration.

Les clients HTTP/.NET envoient Authorization: Bearer. Les API navigateur
WebSocket et SSE peuvent utiliser access_token dans la query string : ce
paramètre n’est accepté que sur la route exacte du hub et pour ces transports.
Il n’authentifie ni POST /negotiate, ni les routes REST ou MCP. WebSocket est
activé avant l’authentification pour que la requête d’upgrade soit identifiée.

`Realtime__AllowedOrigins` est une liste d’origines exactes séparées par
virgules. En Development uniquement, le défaut autorise les deux origines
locales de développement http://127.0.0.1:5173 et http://localhost:5173. En
production, aucune Origin navigateur n’est acceptée sans configuration.
Aucune politique CORS ouverte n’est ajoutée ; le frontend React utilise un proxy
même origine. Un déploiement cross-origin demanderait une politique explicite.

Les logs applicatifs contiennent connexion, sujet technique, ticketId,
révision, eventId et type d’événement. Ils ne journalisent ni token, payload
du ticket ni query string. La configuration ASP.NET Core reste au niveau
Warning. Un reverse proxy doit également masquer access_token dans ses logs.

## Reconnexion et limites de livraison

La reconnexion classique crée une nouvelle connexion. Les groupes de l’ancienne
connexion ne sont pas conservés. Le client React réauthentifie la connexion,
réappelle JoinTicket et invalide ses lectures pour afficher le snapshot courant.
Cela rattrape les changements faits pendant l’interruption. Un échec initial
se relance manuellement depuis Settings ; la reconnexion automatique possède
quatre tentatives bornées.

La notification est best effort, pas un journal persistant ou un outbox.
Le notifier utilise un budget séparé de deux secondes après commit ; une panne
du transport est loggée et ne transforme pas une écriture déjà validée en
erreur HTTP. Il n’y a pas de replay des événements manqués. Le client peut
toujours recharger GetTicket après une reconnexion ou une erreur de transport.

Cette implémentation couvre un hôte API. Le Worker garde le transport no-op
par défaut et ne publie pas vers un autre processus. Plusieurs instances ou
un traitement hors API nécessiteront un backplane/service SignalR et, si une
livraison durable est requise, un outbox. La taille d’un message entrant est
limitée à 8 Kio et les invocations sont séquentielles par connexion.

La reconnexion avec conservation d’état (`AllowStatefulReconnects`) est
explicitement désactivée. Le client de tests .NET utilise la version corrigée
10.0.14. Le runtime ASP.NET Core local observé est 10.0.12 ; avant déploiement,
installer un runtime corrigé 10.0.13 ou supérieur, conformément à
[l’avis Microsoft CVE-2026-70159](https://github.com/advisories/GHSA-334f-vvp4-7g6p).
La reconnexion classique testée ici ne dépend pas de cette fonctionnalité.

## Validation

Les tests d’intégration utilisent le vrai client SignalR .NET, le hub et
PostgreSQL : sept événements, isolation des groupes, abonnement/retrait,
reconnexion et relecture, refus 401/403, contrôle de ticketId, jeton WebSocket
et refus de son usage sur d’autres routes. ML est réel ; RAG/LLM sont des fakes
dans le scénario d’analyse IA. Les tests unitaires vérifient l’enveloppe et
l’isolation d’une panne de publication après commit.

```powershell
. ./scripts/Import-LocalEnvironment.ps1
dotnet build backend/ITTicketIntelligence.sln --configuration Release
dotnet test backend/ITTicketIntelligence.sln --configuration Release --no-build
```

Le dossier Postman « SignalR backend » vérifie uniquement la négociation
authentifiée et le refus sans token. Il ne prétend pas vérifier la livraison
des messages ; celle-ci est couverte par les tests d’intégration.

Validation du 2026-10-26 : compilation Release sans erreur ni avertissement ;
590 tests xUnit réussis (298 unitaires et 292 d’intégration), dont 20 tests
ciblés SignalR. Les deux requêtes Postman passent leurs quatre assertions.
Lors de cette livraison backend, le frontend et ses dépendances avaient été
remis au squelette initial. Le jalon React suivant est décrit dans
[frontend.md](frontend.md).

Références : [authentification SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0)
et [protocole client/reconnexion](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-10.0).
