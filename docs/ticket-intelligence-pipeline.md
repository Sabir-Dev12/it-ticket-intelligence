# Ticket Intelligence Pipeline

`POST /api/tickets/{id}/analyze` analyse le titre et la description stockés en base.
La requête ne nécessite aucun body. Le pipeline n'appelle ni RAG, ni LLM, ni React.

## Contrat

Réponse HTTP 200, exemple de forme (scores illustratifs) :

```json
{
  "category": "Database",
  "categoryConfidence": 0.94,
  "priority": "High",
  "priorityConfidence": 0.91,
  "recommendedTeam": "Database",
  "teamConfidence": 0.88,
  "needsHumanReview": false
}
```

`recommendedTeam` devient `null` lorsque Team refuse la recommandation. La confiance
reste disponible. Les scores ML.NET ne constituent pas des probabilités calibrées.
`needsHumanReview: false` n'autorise aucune action automatique.

- 400 : identifiant GUID vide ; 404 : ticket absent ou URL sans GUID valide.
- 409 : ticket archivé, résolu, fermé, ou révision modifiée pendant l'analyse.
- 503 : PostgreSQL indisponible ; 500 : erreur inattendue d'un prédicteur.
- Les erreurs utilisent le `ProblemDetails` global avec `traceId`.

## Orchestration Application

`ITicketIntelligencePipeline` expose `AnalyzeAsync(ticketId, cancellationToken)`.
`TicketIntelligencePipeline` exécute dans l'ordre :

1. Charger un instantané du ticket : ID, titre, description, révision et état.
2. Vérifier que le ticket est Open ou InProgress et non archivé.
3. Category : titre + description.
4. Priority : même texte + **catégorie prédite à l'étape 3**, même si sa confiance est faible.
5. Team : même texte uniquement ; les modèles restent indépendants.
6. Agréger les scores et les décisions de revue.
7. Sauvegarder trois `TicketPrediction` dans une seule transaction.
8. Retourner `IntelligenceResult` uniquement après commit.

Les prédicteurs existants, leurs entraînements et leurs bundles restent inchangés.
Il n'y a aucun entraînement ni rechargement de modèle par requête.
La qualité de Priority en cascade n'est pas équivalente à son évaluation autonome :
une erreur Category peut se propager. Aucun score de précision du pipeline complet
n'est revendiqué ici.

Les ports Application permettent de remplacer les prédicteurs et la persistance
dans les tests. Le controller ne fait que déléguer. Les opérations de base et les
prédicteurs reçoivent le même `CancellationToken`, vérifié aussi entre les étapes.
L'inférence ML.NET synchrone déjà engagée ne peut pas être interrompue au milieu de
son calcul ; les étapes suivantes et la sauvegarde respectent l'annulation.

## Politique de revue

`needsHumanReview` est vrai si au moins une prédiction exige une revue :

- Category : confiance < CategoryMinimumConfidence.
- Priority : confiance < PriorityMinimumConfidence.
- Team : décision d'abstention du prédicteur, selon TeamMinimumConfidence.

Les trois seuils serveur valent **0,8** par défaut. L'égalité au seuil est acceptée.
Pour Category et Priority, la valeur suggérée reste visible même sous le seuil.
Pour Team, le nom est masqué sous le seuil, conformément au contrat du prédicteur.

Configuration dans `MachineLearning` ou variables d'environnement :

```text
MachineLearning__CategoryMinimumConfidence=0.8
MachineLearning__PriorityMinimumConfidence=0.8
MachineLearning__TeamMinimumConfidence=0.8
```

Les valeurs doivent être finies, > 0 et <= 1. Aucune option de la requête ne permet
de modifier les seuils. Les seuils effectifs sont conservés avec les prédictions.
Une confiance élevée ne garantit pas l'exactitude ; les limites connues des trois
baselines restent documentées dans leurs rapports individuels.

## Persistance et concurrence

`ITicketIntelligenceStore` appartient à Application. Son implémentation EF Core
appartient à Infrastructure. La migration `TicketIntelligencePipeline` ajoute :

- `analysis_id` : identifiant commun aux trois lignes ; index unique (analysis_id, type).
- `recommended_team_name` : label du modèle, sans dépendance envers une Team réelle.
- `review_threshold` et `needs_human_review` : décision par prédiction.
- Des CHECK constraints sur la cohérence score/seuil/revue et le résultat typé.

Chaque ligne conserve également `input_revision`, `model_version`, la confiance et
la date UTC commune. Les anciennes prédictions restent lisibles : leurs nouvelles
colonnes nullable sont absentes et leur indicateur de revue vaut false. Cela
signifie une absence de décision de revue enregistrée, pas une approbation rétroactive.
L'ancien champ `recommended_team_id` et sa FK restent disponibles pour l'historique.
Le pipeline écrit exclusivement les labels du modèle, sans créer ni affecter d'équipe.

L'inférence se déroule hors transaction. Au moment de sauvegarder, une transaction
courte prend un verrou `SELECT ... FOR UPDATE` sur le ticket, recharge son état,
vérifie la révision puis ajoute les trois résultats. Une modification concurrente
fait échouer l'analyse avec 409 ; aucune prédiction de ce run n'est conservée.
Une erreur SQL annule l'ensemble du lot. L'historique reste append-only.

Chaque appel réussi produit une nouvelle analyse : l'endpoint n'est pas idempotent.
Les trois résultats d'un run sont retrouvables dans `GET /api/tickets/{id}` grâce à
`analysisId`, avec leur décision de revue et leur seuil. Les runs précédents restent
associés à leur révision ; une modification ultérieure ne les rend pas courants.

Le pipeline ne change ni Category/Priority du ticket, ni affectation, ni statut,
ni révision, ni UpdatedAt. Seul l'historique des prédictions est ajouté. L'approbation
humaine et l'application des suggestions restent des étapes futures.

## Vérification

```powershell
./scripts/Initialize-Postgres.ps1
. ./scripts/Import-LocalEnvironment.ps1
dotnet build backend/ITTicketIntelligence.sln --configuration Release --no-restore
dotnet ef database update --project backend/src/ITTicketIntelligence.Infrastructure --startup-project backend/src/ITTicketIntelligence.Infrastructure --configuration Release --no-build
dotnet test backend/ITTicketIntelligence.sln --configuration Release --no-build
```

Les tests couvrent l'ordre des appels, le chaînage Category → Priority, la propagation
du token, les seuils et l'abstention, les sorties incohérentes, les erreurs des modèles,
les erreurs SQL, les changements concurrents, l'historisation de plusieurs analyses,
l'absence d'affectation automatique et l'exécution HTTP avec les trois vrais modèles.

La collection Postman conserve deux vérifications d'erreurs dans `Smoke` et un
parcours `Intelligence` pour un `userId` actif existant : création d'un ticket,
analyse, lecture de l'historique, nouvelle analyse et vérification des deux lots.
Ce parcours crée des données de démonstration et nécessite l'API locale démarrée.

```powershell
postman collection run "postman/collections/IT Ticket Intelligence" -i Smoke --no-report-events
postman collection run "postman/collections/IT Ticket Intelligence" -i Intelligence --env-var "userId=<UUID utilisateur actif>" --no-report-events
```

Vérifications réalisées : compilation Release sans avertissement ni erreur,
**328 tests xUnit réussis** (184 unitaires et 144 d'intégration), migration
appliquée sur PostgreSQL local, collection lintée (26 requêtes,
aucune erreur ni avertissement), Smoke (12 requêtes / 32 assertions) et
Intelligence (5 requêtes / 14 assertions), tous réussis.

Exemple observé avec les vrais modèles pour « SQL transactions blocked by long
running locks » :

```json
{
  "category": "Application",
  "categoryConfidence": 0.1715567409992218,
  "priority": "High",
  "priorityConfidence": 0.5238181352615356,
  "recommendedTeam": null,
  "teamConfidence": 0.3016349971294403,
  "needsHumanReview": true
}
```

Cet exemple est transmis à la revue humaine ; la catégorie Application ne valide
pas le routage attendu pour cet incident SQL. Les deux runs Postman ont conservé
six prédictions dans deux lots distincts, sans modifier les champs du ticket.
