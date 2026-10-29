# Affectation à une personne

Le formulaire de création et de modification propose **Assigned to**, un sélecteur de noms utilisant le composant `ChoiceSelect` existant. Il liste jusqu'à 200 comptes Agent/Support actifs, sans exposer leurs emails. Un message signale une liste tronquée. Le choix reste facultatif ; `Unassigned` retire le responsable lors d'une modification. L'équipe et la personne constituent deux affectations indépendantes. La page de détails affiche le nom courant du responsable.

## Contrat

- `GET /api/tickets/form-options` ajoute `assignees: [{id,name,role}]` et `assigneesTruncated`.
- `POST /api/tickets` accepte `assignedUserId: uuid | null`.
- `PUT /api/tickets/{id}` accepte `assignee: {userId: uuid | null}`. Omettre `assignee` conserve le responsable courant ; `userId: null` le retire.
- Le détail `TicketDto` ajoute `assignedUserId` et `assignedUserName`. Le nom est résolu lors de la lecture du ticket.
- La migration `TicketIndividualAssignee` ajoute une FK nullable vers users avec suppression restreinte et un index partiel responsable/statut. Aucun ticket existant n'est automatiquement réaffecté.

La validation Application refuse un compte absent, désactivé ou ayant un rôle différent de Agent/Support. La sélection initiale d'une personne est autorisée aux créateurs User/Admin/Support. La modification d'une affectation reste réservée à Admin/Support. Un Agent dispose d'un accès en lecture aux tickets qui lui sont directement affectés, en plus de ses tickets d'équipe. Le retrait de l'affectation individuelle retire ce droit, sauf s'il dispose encore d'un accès par son équipe. Les contrôles de session courante alimentent REST, MCP et SignalR. Les actions restent auditées et les clients reçoivent les événements TicketCreated/TicketUpdated existants.

## Validation

Builds backend et frontend réussis ; migration appliquée sur PostgreSQL local. **328 tests unitaires backend et 34 frontend réussis.**

Le test facultatif `frontend/e2e/ticket-assignee-live.spec.ts` passe sur la vraie API : sélection d'un Agent dans le formulaire User, enregistrement, affichage du nom, lecture par l'Agent sans équipe, refus de rôles non éligibles/identifiants invalides/inexistants, réaffectation par Support, retrait immédiat de l'accès de l'Agent et désaffectation. Axe est vérifié dans les deux thèmes et l'absence de débordement à 280/320/414/1280 pixels. Les mots de passe sont fournis par variables d'environnement de test. Les captures sont dans `artifacts/frontend/ticket-assignee-{light,dark}.png`.

Le gate global du kit reste 0/52 faute de scripts/harness de référence configurés dans le dépôt ; son journal est `artifacts/frontend/ticket-assignee-design-gate.txt`. Ces résultats ciblés ne prétendent pas valider toute la suite historique d'intégration.
