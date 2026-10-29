# MCP : opérations de lecture contrôlées

Le serveur est disponible sur `/mcp` dans l’hôte ASP.NET Core. Il utilise
le [SDK C# MCP officiel 2.3.0](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore/2.3.0)
et Streamable HTTP en mode stateless. Le SDK reste exclusivement dans API ;
Domain ne change pas et ne référence aucune technologie MCP.

## Couches

| Emplacement | Responsabilité |
|---|---|
| Application/Mcp/Contracts | DTOs, catalogue, schémas, permissions et ports, sans SDK MCP ni EF |
| Application/Mcp/McpToolService | Validation, autorisation, périmètre des tickets, orchestration et audit |
| Infrastructure/Mcp | Lectures EF prédéfinies, probe de connexion et écriture durable des audits |
| Api/Mcp | Authentification, protection HTTP, mapping JSON-RPC et adaptateur du SDK |
| Application/Mcp/McpAgentTools | Client interne de la couche d’outils pour l’AI Agent |

L’agent passe par le même service applicatif que le transport HTTP, avec une
identité serveur `ticket-agent` limitée au ticket courant. Cet appel interne
évite un aller-retour réseau et n’usurpe aucun utilisateur. Le LLM reçoit les
descriptions/schémas des six outils ; il ne reçoit ni DbContext, ni token MCP,
ni permission de lancer une requête SQL.

L’ancien nom `get_team_information` et les arguments vides des deux lectures
du ticket sont traduits uniquement dans cet adaptateur interne, pour les
transcripts précédents et les lectures initiales du workflow. Sur le serveur
MCP, les noms et paramètres publiés sont strictement ceux du catalogue.

## Catalogue et schémas

Les [contrats complets de chaque outil](mcp-tools.md) documentent nom,
description, input schema, output schema et permission. Leur version
[JSON exportée](mcp-tools.json) provient de `tools/list`, et inclut les
annotations MCP. `McpToolCatalog` est la source des schémas utilisés à la fois
par le serveur et les adaptateurs LLM.

| Tool | Permission | Lecture et limites |
|---|---|---|
| get_ticket | tickets.read | ticketId UUID non vide ; ticket du périmètre autorisé |
| search_tickets | tickets.search | query littérale ≤200 caractères ; page 1–1000, pageSize 1–20 ; scope appliqué avant pagination ; archives exclues |
| get_ticket_history | tickets.history.read | ticketId autorisé ; existence vérifiée ; au plus 10 commentaires tronqués à 500 caractères, 6 prédictions et 10 événements connus |
| search_knowledge | knowledge.read | query non vide ≤500 caractères ; limit 1–5, défaut 3 ; chunks/provenance du RAG existant |
| get_team | teams.read | name parmi Support, Network, Database, Security, Backend, Infrastructure ; objet nullable ; aucune identité de membre |
| get_system_status | system.status.read | aucun argument ; sonde PostgreSQL bornée à 3 secondes ; état grossier sans hôte, identifiant ni chaîne de connexion |

Les DTOs de tickets contiennent les symptômes rapportés et l’état enregistré,
sans coordonnées du créateur. Les prédictions restent désignées comme telles.
Les commentaires, titres, procédures et descriptions d’équipe sont des données
non fiables, pas des instructions d’autorisation.

## Authentification et autorisation

Sans client configuré, `/mcp` refuse l’accès avec HTTP 401. Aucun token de
développement par défaut n’est actif. Les variables `MCP__*` sont lues depuis
l’environnement ; modifier les clients nécessite de redémarrer l’hôte.

Chaque client possède un sujet, un digest SHA-256 de token, une liste explicite
de permissions et un périmètre de tickets. Le header `Authorization: Bearer`
est authentifié avec comparaison des digests en temps constant. Le token doit
avoir 32 à 512 caractères sans espace ni caractère de contrôle. Utiliser une
valeur aléatoire, pas une phrase courte. Les clients sans permission ne peuvent
ni découvrir ni utiliser d’outil.

Exemple de préparation dans PowerShell, avant de lancer l’API :

```powershell
$mcpToken = [Convert]::ToHexStringLower([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:MCP__Clients__0__TokenHash = [Convert]::ToHexStringLower([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($mcpToken)))
$env:MCP__Clients__0__Subject = 'local-reader'
$env:MCP__Clients__0__Permissions = 'tickets.read,tickets.history.read,knowledge.read,teams.read'
$env:MCP__Clients__0__TicketIds = '<UUID-du-ticket-autorise>'
$env:MCP__Clients__0__AllTickets = 'false'
# Configurer le client avec le token gardé en mémoire dans $mcpToken.
# Ne jamais enregistrer ce token dans Git ou une collection partagée.
```

Jusqu’à 16 clients, indices 0–15, peuvent être configurés. Les champs de scope
utilisent des listes séparées par virgules ; au plus 100 tickets par client.
`AllTickets=true` doit être accordé explicitement côté serveur. Le défaut est
false ; une liste vide n’autorise aucun ticket. `knowledge.read` concerne la
Knowledge Base commune et `teams.read` les informations non personnelles des
équipes ; ces modules ne possèdent pas d’ACL documentaire ou de tenant distinct.

`tools/list` filtre les outils par permission. `tools/call` revérifie la
permission, puis le scope avant toute consultation de ticket. Aucun argument
`permissions`, `subject`, `allTickets`, `sql`, URL, nom de table ou chaîne de
connexion n’est accepté. Les opérations EF sont fixées par le code ; la
recherche utilise une comparaison de texte littérale paramétrée.

Ce profil d’authentification par token opaque est destiné à un serveur privé
ou local. Il n’implémente pas de serveur OAuth ni le discovery OAuth MCP.
L’authentification des autres routes REST reste une étape distincte ; cette
configuration protège `/mcp` et les outils partagés, pas toute l’application.

## Protection HTTP et protocole

Hosts autorisés par défaut : localhost, 127.0.0.1 et ::1. `MCP__AllowedHosts`
permet une liste explicite ; pas de wildcard. HTTP est permis sur ces noms
loopback, HTTPS est exigé pour les autres noms autorisés. Les requêtes avec
Origin sont refusées sauf correspondance exacte dans `MCP__AllowedOrigins`.
Aucune politique CORS ouverte n’est ajoutée. Les contrôles Host/Origin et la
configuration HTTPS font partie du périmètre à conserver derrière un proxy.

POST MCP est limité à 16 Kio, profondeur JSON 16. Les propriétés JSON dupliquées
sont rejetées avant leur conversion en dictionnaire par le SDK. Les autres
arguments inconnus, types incorrects, GUID vides, limites ou enums invalides
sont refusés par le service applicatif. Les appels disposent d’un budget de
15 secondes et d’un cancellation token ; aucun outil d’écriture n’existe.

Deux parcours sont vérifiés :

- Révision `2025-11-25` : initialisation et appels Streamable HTTP compatibles.
- Révision `2026-07-28` : requêtes stateless sans session, avec
  `MCP-Protocol-Version`, `Mcp-Method` et, pour tools/call, `Mcp-Name`.
  Chaque `params._meta` doit fournir `io.modelcontextprotocol/protocolVersion`
  et `io.modelcontextprotocol/clientCapabilities` (objet, vide si nécessaire).

Un client SDK compatible renseigne ces métadonnées. Les requêtes HTTP Postman
du dossier Mcp les montrent explicitement. Les réponses peuvent être JSON ou
SSE ; un client HTTP brut doit lire les événements `data:` avant de parser le JSON.

## Erreurs et audit

Un résultat réussi contient le même objet dans `structuredContent` et un
content block texte JSON. Les schémas d’output décrivent cet objet.

Les erreurs d’outil retournent `isError: true` avec :

```json
{"error":{"code":"Forbidden","message":"The requested ticket is outside the authorized scope.","callId":"11111111-1111-4111-8111-111111111111"}}
```

Codes contrôlés : UnknownTool, Forbidden, InvalidArguments, NotFound,
Unavailable. Les erreurs n’exposent ni requête SQL, ni body fournisseur, ni
exception brute. Une erreur d’outil utilise l’enveloppe MCP HTTP 200 ; les
erreurs HTTP/authentification/protocole peuvent produire 400, 401, 403 ou 413.
Les schémas de réussite ne s’appliquent pas aux résultats `isError: true`.

L’audit est écrit dans `app.audit_logs`, sans nouvelle migration, via des
contextes EF courts indépendants. Un CallId relie Started à Succeeded,
Denied, Rejected, Failed ou Cancelled. Les détails contiennent le sujet
authentifié, le nom d’outil reconnu et les hashes des arguments/sorties.
Ils ne stockent pas le bearer token, le texte du ticket, le contenu documentaire
ou les arguments bruts. UserId reste null pour les identités de service ;
l’identité du client est conservée dans les détails, sans créer de faux User.

Si l’audit initial échoue, l’outil n’est pas exécuté. Une annulation conserve
un budget séparé de 5 secondes pour son audit terminal. Une panne PostgreSQL
peut aussi empêcher l’écriture de cette trace ; les événements déjà validés
restent disponibles. Les refus d’authentification et les requêtes HTTP malformées
sont traités avant un appel d’outil ; leurs logs techniques restent génériques.

## Tests et exécution

```powershell
. ./scripts/Import-LocalEnvironment.ps1
dotnet build backend/ITTicketIntelligence.sln --configuration Release
dotnet test backend/ITTicketIntelligence.sln --configuration Release --no-build
```

Les tests unitaires couvrent les six outils, schemas, paramètres, permissions,
scope, annulation, indisponibilité et refus de SQL. Les tests d’intégration
exécutent le vrai SDK et PostgreSQL, avec un RAG fake, et vérifient les deux
révisions de protocole, l’audit, la recherche paramétrée et l’absence de
modification des tickets. La régression de l’agent utilise ce service partagé.

La collection Postman expose six requêtes Mcp, avec `mcpToken` vide à fournir
hors Git. Les requêtes de scope attendent un profil limité aux tickets choisis,
sans AllTickets. Les validations locales utilisent uniquement des credentials
synthetiques éphémères et un ticket inexistant ; aucun secret réel n’est inclus.

Les adaptations du catalogue LLM sont vérifiées contre la simulation Postman
existante. Ces vérifications démontrent transport et contrôles applicatifs,
pas la qualité d’un modèle LLM réel.

Validation du 2026-10-26 : compilation Release sans erreur ni avertissement ;
570 tests xUnit réussis (290 unitaires, 280 d’intégration). Les six requêtes
Postman MCP passent leurs 12 assertions contre le serveur local ; les 16 cas
HTTP de l’agent sont rejoués avec succès contre la simulation LLM. Le lint de
la collection de 57 requêtes ne signale ni erreur ni avertissement.
