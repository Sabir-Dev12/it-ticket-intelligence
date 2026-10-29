# Team Recommendation

Contrat défini avant implémentation : `POST /api/predictions/team` reçoit `title` et `description`. Il n’accepte ni catégorie, ni priorité, ni seuil fourni par le client. Le modèle est indépendant de Category et Priority.

Réponse 200 : `team` (Support, Network, Database, Security, Backend, Infrastructure, ou null si refus), `confidence` (score maximal), `needsHumanReview`, `threshold`, `modelVersion`. Si confidence < threshold, team est null et needsHumanReview est true. À égalité, le seuil est atteint. Le seuil serveur est configurable, avec une valeur de référence fixée à 0,80 avant entraînement.

Aucune recommandation, même au-dessus du seuil, ne déclenche une affectation, une modification, un audit ou une exécution d’outil. needsHumanReview=false indique seulement que le seuil est atteint, pas qu’une action est autorisée. Le score n’est pas une probabilité de justesse calibrée.

Protocole fixé avant entraînement : six variantes SDCA Maximum Entropy (L2 0,00001 / 0,0001 / 0,001 ; pondération inverse des fréquences activée ou non). Sélection par macro-F1 de validation, puis log loss, puis identifiant stable. Mesure du test et du taux de refus seulement après sélection. Seuil de référence 0,80 fixe, sans optimisation sur test.

## Architecture et indépendance

- `Application/Intelligence` : port `ITicketTeamPredictor`, `TicketTeamInput`, `TeamRecommendationPolicy`, résultat et query handler ; aucun type ML.NET.
- `Infrastructure/MachineLearning` : `TicketTeamPredictor`, `TeamModelStore`, contrats de colonnes, métadonnées et DI propres à Team.
- `backend/tools/ITTicketIntelligence.Training` : `TeamTrainingService`, `TeamTrainingPipeline`, `TeamEvaluationService`, commandes `train-team` et `predict-team`.
- `Api/Controllers/TeamPredictionsController` : adaptation HTTP vers le handler.
- `models/ticket-team/v1` : ZIP complet, métadonnées, rapport et démonstration HTTP.

La prédiction n’utilise que Title et Description. Category et Priority ne sont ni des features ni des services appelés. Le training partage le lecteur CSV vérifié existant ; celui-ci conserve maintenant aussi RecommendedTeam, auparavant ignoré. Les deux premiers modèles, leurs pipelines et leurs artefacts restent inchangés. Un bundle Team seul suffit au prédicteur et à la commande console ; l’hôte API compose les trois modèles et les charge au démarrage.

La taxonomie de six noms est indépendante des identifiants et appartenances des entités Team en base. Ce module ne résout pas un nom vers un TeamId, ne contrôle pas l’activité d’une équipe en base et n’autorise aucune affectation. Cette correspondance devra être vérifiée au moment d’une éventuelle action explicitement approuvée.

## Entraînement, évaluation et stockage

Les CSV v1 et leurs partitions sont conservés : 168 tickets / 84 scénarios de training, 36 / 18 en validation et 36 / 18 en test. Les deux reformulations d’un scénario restent ensemble. Les empreintes, le schéma, les groupes et les labels sont vérifiés avant entraînement.

Le pipeline ajuste MapValueToKey sur RecommendedTeam, puis FeaturizeText sur Title/Description avec normalisation L2, SDCA Maximum Entropy et décodage du label. Le vocabulaire et le classifieur sont ajustés uniquement sur training. RecommendedTeam est la cible ; ExampleWeight sert seulement au formateur. Les tests changent cible/poids à l’inférence et vérifient l’invariance des scores. Les deux autres labels, identifiants, résolution, commentaires, affectation finale et justifications sont exclus des features.

Les six variantes partagent la graine 20261024, un thread et 200 itérations maximum. Les poids équilibrés sont calculés par `N_training / (6 × effectif_training_de_la_classe)`. Validation et test ne sont ni pondérés ni rééchantillonnés à l’évaluation. La sélection ne dépend ni du test, ni d’une réduction artificielle de la couverture par changement du seuil.

Le store sauvegarde modèle, métadonnées et rapport dans un dossier temporaire adjacent, puis publie ce dossier par renommage. Une version existante n’est jamais écrasée. Le chargement vérifie checksum SHA-256, contrat, schéma, features et ordre exact des slots de scores. Les fichiers doivent venir d’un producteur de confiance ; un checksum ne protège pas contre une falsification simultanée du modèle et de ses métadonnées.

Le singleton conserve son PredictionEngine en mémoire, avec SemaphoreSlim pour les accès concurrents et propagation du CancellationToken. Une opération ML.NET synchrone déjà commencée ne peut pas être interrompue ; les annulations sont vérifiées pendant l’attente et avant/après scoring. Aucun fichier ou dataset n’est relu par requête. Un modèle absent/corrompu bloque le démarrage, sans entraînement automatique ni équipe de secours inventée.

## Configuration du refus

```json
{
  "MachineLearning": {
    "TeamModelDirectory": "Models/TicketTeam",
    "TeamMinimumConfidence": 0.8
  }
}
```

Les variables `MachineLearning__TeamModelDirectory` et `MachineLearning__TeamMinimumConfidence` surchargent ces valeurs. Le chemin relatif part de AppContext.BaseDirectory ; build/publish copie le bundle v1 dans Models/TicketTeam. Une nouvelle version ou un nouveau seuil nécessitent un redémarrage pour reconstruire le singleton.

Le seuil doit être fini et dans ]0 ; 1]. Une configuration invalide bloque le démarrage. L’API n’accepte pas un seuil, une catégorie, une priorité ou un flag de revue dans le corps : les champs inconnus sont rejetés avec ProblemDetails. Le client ne peut donc pas désactiver le refus par une valeur dans sa requête.

Le score est celui du slot correspondant au label retenu, pas l’ordinal d’un enum ; aucun second softmax n’est appliqué. Il s’agit d’un score normalisé non calibré sur du trafic réel. Le seuil 0,80 est un choix d’ingénierie fixé avant entraînement, pas une garantie de 80 % de bonnes réponses. Les recommandations qui dépassent le seuil restent des suggestions et ne déclenchent aucune action.

## Déséquilibre et résultats de team-v1

| Équipe | Training | Validation | Test | Scénarios de training |
|---|---:|---:|---:|---:|
| Support | 58 | 10 | 10 | 29 |
| Network | 20 | 4 | 6 | 10 |
| Database | 6 | 4 | 4 | 3 |
| Security | 18 | 4 | 4 | 9 |
| Backend | 40 | 6 | 4 | 20 |
| Infrastructure | 26 | 8 | 8 | 13 |

Support a près de dix fois plus de tickets de training que Database. La couverture de Database dans les partitions ne remplace pas un nombre suffisant d’incidents d’apprentissage indépendants. Les fréquences sont synthétiques et ne décrivent pas une organisation réelle.

La variante choisie est **l2-0.001-balanced**. Sa baseline majoritaire, calculée depuis training, prédit Support et obtient 27,78 % d’accuracy sur chacune des partitions d’évaluation.

| Mesure | Validation | Test |
|---|---:|---:|
| Accuracy | 38,89 % | 44,44 % |
| MacroAccuracy | 32,50 % | 38,33 % |
| Macro-F1 | 30,34 % | 37,00 % |
| LogLoss | 1,565615 | 1,427740 |
| Top-2 accuracy | 58,33 % | 66,67 % |
| Recommandations au-dessus de 0,80 | 1/36 | 2/36 |
| Revue humaine requise | 35/36 | 34/36 |
| Couverture au seuil 0,80 | 2,78 % | 5,56 % |
| Accuracy parmi les recommandations au-dessus du seuil | 0/1 | 1/2 |

Les métriques globales portent sur les prédictions brutes avant refus. Les métriques de couverture et d’accuracy conditionnelle portent uniquement sur les réponses qui atteignent le seuil. Si aucune réponse ne l’atteint, l’accuracy conditionnelle est null et non zéro ou NaN. Ces mesures ne sont pas des autorisations d’affectation.

Matrice test : lignes réelles, colonnes prédites, ordre Support, Network, Database, Security, Backend, Infrastructure.

```text
8 0 0 0 2 0
1 0 0 0 4 1
0 0 0 0 4 0
1 1 0 2 0 0
2 0 0 0 2 0
0 1 0 0 3 4
```

Le rapport conserve aussi la matrice validation, les métriques et supports par équipe, les six variantes et les erreurs parmi les réponses dépassant le seuil. Top-2 est une mesure diagnostique, pas une seconde équipe automatiquement affectée ([métriques ML.NET](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.data.multiclassclassificationmetrics)).

Les performances restent faibles, notamment les rappels de Network et Database sur test. Une recommandation au-dessus de 0,80 est erronée parmi les deux observées. Il ne faut donc pas confondre confiance et justesse, ni transformer needsHumanReview=false en permission d’exécution. Le petit corpus synthétique et ses reformulations corrélées ne valident pas un routage autonome ; de nouveaux incidents indépendants et une revue des frontières de responsabilité sont nécessaires avant usage opérationnel.

## Exemple exécuté

`samples/team-prediction.json` décrit des transactions SQL bloquées par des verrous. La réponse du modèle chargé est :

```json
{
  "team": null,
  "confidence": 0.3016349971294403,
  "needsHumanReview": true,
  "threshold": 0.8,
  "modelVersion": "team-v1"
}
```

L’équipe attendue pour cet exemple est Database, mais le service s’abstient : aucune équipe n’est proposée et aucun ticket n’est modifié. La réponse HTTP est enregistrée dans `models/ticket-team/v1/smoke-prediction.json`.

## Commandes

Depuis la racine :

```powershell
python scripts/datasets/prepare_tickets.py
dotnet build backend/ITTicketIntelligence.sln --configuration Release

# v1 est déjà livrée ; une nouvelle exécution doit utiliser un nouveau dossier.
dotnet run --project backend/tools/ITTicketIntelligence.Training --configuration Release -- train-team --data data/tickets/v1 --output models/ticket-team/v2 --version team-v2

# Prédiction indépendante depuis le bundle Team.
dotnet run --project backend/tools/ITTicketIntelligence.Training --configuration Release -- predict-team --model models/ticket-team/v1 --input samples/team-prediction.json

# Seuil expérimental de la console uniquement, sans action sur les tickets.
dotnet run --project backend/tools/ITTicketIntelligence.Training --configuration Release -- predict-team --model models/ticket-team/v1 --input samples/team-prediction.json --threshold 0.95

. ./scripts/Import-LocalEnvironment.ps1
dotnet run --project backend/src/ITTicketIntelligence.Api
```

API démarrée :

```powershell
Invoke-RestMethod http://127.0.0.1:5080/api/predictions/team -Method Post -ContentType application/json -InFile samples/team-prediction.json
postman collection run "postman/collections/IT Ticket Intelligence" -i Smoke --no-report-events
```

Tests :

```powershell
./scripts/Initialize-Postgres.ps1
. ./scripts/Import-LocalEnvironment.ps1
dotnet test backend/ITTicketIntelligence.sln --configuration Release
```

Les tests couvrent frontière exacte du seuil, refus réel avec team=null, scores/configurations invalides, validation/annulation, indépendance des autres prédicteurs et de PostgreSQL, bundle autonome, absence de lecture disque par appel, concurrence, persistance/intégrité, ordre des scores, poids de training, sélection sur validation, évaluation avec zéro réponse au-dessus du seuil, métriques/matrices, HTTP/OpenAPI, contournement du seuil rejeté et absence d’affectation ou d’écriture d’audit même au-dessus du seuil.

Livraison vérifiée : build Release sans erreur ni avertissement, **156 tests unitaires et 128 tests d’intégration réussis**. Postman sur l’API locale : **10 requêtes et 27 assertions réussies**, code de sortie 0. La publication inclut le bundle Team et n’inclut pas l’outil d’entraînement. Les empreintes des artefacts, pipelines et prédicteurs Category/Priority sont identiques avant/après cette étape.
