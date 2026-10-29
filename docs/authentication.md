# Authentification JWT et autorisation

La page `/login` reprend les composants, couleurs et thèmes existants. `POST /api/auth/login` reçoit `email` et `password` et retourne `accessToken`, `tokenType: Bearer`, `expiresIn: null` et l'utilisateur. Les nouveaux JWT n'ont pas de date d'expiration : ils restent valides jusqu'à révocation. Envoyer `Authorization: Bearer <accessToken>` sur les requêtes suivantes. `GET /api/auth/me` retourne l'identité courante ; `POST /api/auth/logout` invalide tous les JWT de cet utilisateur.

Les JWT HS256 valident signature, algorithme, issuer, audience et date de début de validité. Les tokens anciens qui comportent une expiration continuent à la respecter ; se reconnecter une fois pour obtenir un nouveau token sans expiration. Les rôles et périmètres sont relus côté serveur. Une modification du rôle, une désactivation, une réinitialisation du mot de passe ou une déconnexion invalide les tokens précédents. Les mots de passe sont hachés avec PBKDF2 SHA256, sel aléatoire et 310 000 itérations. Cinq échecs bloquent le compte 15 minutes ; le endpoint login est également limité à dix tentatives par minute et par IP.

Le frontend garde le JWT uniquement en mémoire, sans minuterie de déconnexion. L'utilisation prolongée ne nécessite plus de reconnexion ; recharger ou fermer l'onglet efface toujours le token local et exige une nouvelle connexion. Il n'y a pas de refresh token, de cookie d'authentification, ni de JWT dans localStorage/sessionStorage. Le thème peut rester dans localStorage. Utiliser HTTPS en dehors du développement local et configurer explicitement les proxys de confiance avant de déployer la limitation par IP derrière un reverse proxy.

## Configuration locale

Depuis la racine, après initialisation de PostgreSQL :

```powershell
./scripts/Initialize-Jwt.ps1
. ./scripts/Import-LocalEnvironment.ps1
dotnet ef database update --project backend/src/ITTicketIntelligence.Infrastructure --startup-project backend/src/ITTicketIntelligence.Api --configuration Release
```

`Initialize-Jwt.ps1` génère une clé aléatoire dans `.env`, ignoré par Git, sans l'afficher. `JWT__SigningKey` est obligatoire pour émettre/valider un JWT et doit contenir au moins 64 octets. Aucun secret n'est fourni par défaut.

Avant le premier démarrage de l'API, définir `AUTH__BootstrapEmail` et `AUTH__BootstrapPassword` dans l'environnement du processus ou le `.env` local. Choisir un mot de passe de 14 à 256 caractères. Importer à nouveau l'environnement et lancer l'API. Le bootstrap crée le premier Admin uniquement tant qu'aucun compte avec mot de passe n'existe ; il réutilise le compte portant cette adresse s'il existe déjà. Retirer ensuite ces deux variables. Les autres comptes sont provisionnés par l'API Admin ; aucune inscription publique n'est exposée.

## Permissions

| Rôle | Tickets | Intelligence et AI | Administration |
| --- | --- | --- | --- |
| User | Créer, consulter ses tickets, commenter et joindre des pièces sur ses tickets | Aucune analyse/assistant | Aucune |
| Agent | Consulter et commenter les tickets affectés à ses équipes actives | Analyser et utiliser l'assistant sur ce périmètre | Aucune |
| Support | Gérer tous les tickets | Analyse et assistant | Aucune |
| Admin | Tous | Tous | Comptes, équipes, Knowledge Base, audit et lecture de la configuration ML |

L'identité du créateur et de l'auteur d'un commentaire provient du JWT : les identifiants envoyés par le client ne permettent pas d'usurper un autre utilisateur. Le filtrage des listes est effectué avant pagination. Un ticket hors périmètre retourne 404. Les suggestions ML/AI ne donnent aucun droit supplémentaire et ne déclenchent aucune action sensible automatiquement.

APIs Admin protégées :

- `GET/POST /api/admin/users`, `PATCH /api/admin/users/{id}` (role/isActive), `POST /api/admin/users/{id}/password`.
- `GET/POST /api/admin/teams`, `PUT/DELETE /api/admin/teams/{id}/members/{userId}`.
- `GET /api/admin/audit?page=1` : 50 entrées de métadonnées par page, sans contenu de conversation ni secrets.
- `GET /api/admin/ml-configuration` : lecture des seuils/configurations ; leur modification reste une opération de configuration/entraînement hors requête.

Le dernier administrateur actif ne peut pas être désactivé/rétrogradé. Les connexions, refus, révocations et modifications sensibles sont audités. Les APIs utilisateurs/équipes sont disponibles ; une interface complète d'administration n'est pas ajoutée à cette étape.

## SignalR et MCP

SignalR utilise le même JWT via `accessTokenFactory`. Le paramètre `access_token` est accepté uniquement pour WebSocket/SSE sur `/hubs/tickets`, jamais sur les routes REST. Les autorisations sont revérifiées avant chaque notification et accès à un ticket, y compris après retrait d'une équipe ou révocation. Les anciennes connexions ne reçoivent donc plus les données d'un périmètre retiré. Les logs ASP.NET standards sont réduits pour ne pas journaliser la query string du hub ; garder cette règle pour les journaux de proxy.

MCP accepte les JWT utilisateurs et applique les permissions et périmètres existants, en lecture seule. Les credentials techniques MCP configurés côté serveur restent distincts ; ils n'ouvrent pas les routes REST de gestion. L'assistant est limité aux rôles Agent/Support/Admin et conserve son historique privé. Le LLM n'accède jamais directement au DbContext.

## Vérification

Politique « jusqu'à déconnexion » : deux tests d'intégration JWT réussissent, dont l'acceptation d'un JWT signé daté de 30 jours sans `exp`, puis son refus après déconnexion. La collection Postman passe ses 7 assertions sur 6 requêtes réelles. Le navigateur reste connecté après 31 minutes simulées et vérifie à nouveau la révocation après déconnexion. Le build et les 26 tests frontend passent. Ces contrôles ciblés ne réexécutent pas toute la suite historique décrite plus bas.

`./scripts/Verify-Auth.ps1` crée une base PostgreSQL temporaire, applique les migrations et lance une API/Vite isolés. Il exécute la collection locale `postman/collections/JWT Authentication` sans publier de résultats distants (6 requêtes / 7 assertions), puis Chrome/Playwright avec de vrais JWT. Les credentials temporaires sont supprimés et la base est détruite en fin d'exécution. Le navigateur vérifie connexion, appel autorisé, absence de stockage persistant/cookie et refus du token après déconnexion. Axe vérifie la page dans les deux thèmes. Les captures et résultats non sensibles sont dans `artifacts/frontend`.

Les trois tests ciblés JWT/migration réussissent. `JwtAuthorizationTests` vérifie sur PostgreSQL réel les rôles, la propriété des tickets, l'affectation d'équipe, la signature, la révocation, MCP, la négociation et la livraison SignalR, le refus de notifications après retrait d'équipe/expiration, ainsi que le verrouillage/réinitialisation d'un compte. Le build frontend et ses 26 tests unitaires passent ; les contrôles des états default/hover/focus passent dans les deux thèmes et le responsive sans débordement à 280/320/414 pixels.

Les tests HTTP historiques qui ne fournissent pas encore de JWT doivent être migrés : l'exécution complète avant l'ajout du test de verrouillage donne 314 tests unitaires réussis, puis 222 tests d'intégration réussis et 84 échecs (83 refus 401 et un scénario SignalR attendant un événement après une écriture REST non authentifiée). Ne pas interpréter les seuls contrôles JWT réussis comme une validation complète de la suite historique.
