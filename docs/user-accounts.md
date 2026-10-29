# Création des comptes

## Choix retenu

L'administrateur crée les comptes et attribue leur rôle. Il n'y a pas d'inscription publique ni de choix de rôle sur la page de connexion. Une éventuelle inscription publique devra attribuer User côté serveur ; elle ne devra jamais laisser un visiteur se donner Admin ou Support.

Le premier Admin est provisionné par le bootstrap local décrit dans `authentication.md`. Les comptes suivants se créent dans **Users**, route `/admin/users`, ou via Settings → Manage users. Tous les rôles utilisent ensuite `/login` avec leur email et leur mot de passe.

| Rôle | Usage |
| --- | --- |
| User | Créer et consulter ses tickets, commenter |
| Agent | Investiguer les tickets affectés à ses équipes, utiliser l'IA |
| Support | Gérer les tickets du workspace et utiliser l'IA |
| Admin | Administrer comptes, équipes, KB, configuration ML et audit |

Un Agent doit également appartenir à une équipe pour recevoir son périmètre de tickets. Cette page ne crée pas d'appartenance automatiquement.

## Page et contrat

Le formulaire contient nom, email, rôle, mot de passe de 14 à 256 caractères et confirmation. User est présélectionné. Zod valide le formulaire ; les validations serveur existantes restent actives. Un garde empêche les soumissions concurrentes. Une réussite n'est affichée qu'après la réponse du serveur, avec le rôle effectivement retourné.

Les API existantes sont réutilisées : `GET /api/admin/users` retourne au plus 100 comptes ; `POST /api/admin/users` reçoit `{name,email,password,role}` et retourne **200 UserAccount** (`id,name,email,role,isActive`). La confirmation n'est pas transmise. Le filtre de la page porte uniquement sur la liste chargée, sans prétendre rechercher tous les comptes du serveur.

Le backend exige explicitement le rôle Admin sur le contrôleur, en plus du filtre REST existant. La route et les liens frontend sont aussi réservés à Admin. Le rôle de connexion provient du JWT validé et des données serveur, jamais d'un paramètre fourni par un visiteur.

Les mots de passe sont effacés du formulaire après une création ou une erreur réseau/API. Ils ne figurent pas dans la liste, le stockage navigateur ou les notifications. Les comptes utilisent le hachage, la limitation de tentatives et l'audit existants. Aucun email n'est envoyé : l'admin transmet les identifiants au propriétaire par un canal privé. Le système ne prétend pas imposer un changement de mot de passe à la première connexion.

## Validation

Builds .NET et React réussis ; 30 tests frontend réussis. Huit scénarios Playwright sur simulation Postman : création de chaque rôle, confirmation du contrat envoyé, effacement des mots de passe, email dupliqué, validation et refus de la route pour User/Agent/Support. Axe vérifie les deux thèmes, et le responsive est testé à 280/390/1280 pixels. Les fixtures de comptes sont synthétiques ; aucun compte réel n'a été créé par ces tests.

La validation avec la vraie base reste dépendante de PostgreSQL/pgvector, indisponible lors des étapes précédentes. Cette livraison ne revendique pas un nouveau test réussi de persistance réelle. Le gate global du kit reste à 0/52 faute de harness configurés dans le dépôt ; son journal est `artifacts/frontend/users-design-gate.txt`, distinct des contrôles ciblés sur la page.

```powershell
npm test --prefix frontend
npm run build --prefix frontend
# Simulation frontend-read-api sur 4918 ; Vite avec son proxy sur 5183 :
$env:FRONTEND_URL='http://127.0.0.1:5183'
cd frontend
npx playwright test e2e/users.spec.ts
```
