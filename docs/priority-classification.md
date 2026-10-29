# Ticket Priority Classification

Contrat défini avant implémentation : `POST /api/predictions/priority` reçoit `title`, `description` et `category` (nom de catégorie obligatoire). Réponse 200 : `predictedPriority`, `confidence` entre 0 et 1, `modelVersion`. Les entrées invalides répondent 400 ProblemDetails. Aucun ticket n’est modifié.

La catégorie est supposée fournie/confirmée au moment de la création. Les évaluations seront **conditionnelles à cette catégorie**, et ne mesureront pas une chaîne automatique Category → Priority. Le dataset synthétique ne prouve pas que cette information a réellement été recueillie avant la priorité ; cette hypothèse devra être validée avec des données réelles. Une cascade automatique nécessiterait des catégories prédites hors pli pour training et des prédictions issues des seuls modèles de training pour validation/test.

Aucune feature métier supplémentaire n’est inventée à partir du label Priority. Le corpus ne contient pas de champs structurés d’impact/urgence recueillis séparément. Les impacts et contournements restent dans le texte initial.

Protocole fixé avant entraînement : six candidats SDCA Maximum Entropy, L2 dans {0,00001 ; 0,0001 ; 0,001}, avec/sans poids inverses des fréquences calculés sur training. Sélection sur macro-F1 de validation, puis rappel Critical, puis log loss et identifiant stable. Les scores de test sont calculés seulement après cette sélection ; ses labels sont lus auparavant pour les contrôles d’intégrité, de couverture et d’effectifs. Les partitions du dataset v1 et le modèle Category restent conservés.

## Architecture et cycle de vie

- `Application/Intelligence` : `ITicketPriorityPredictor`, entrée validée, résultat et query handler sans type ML.NET.
- `Infrastructure/MachineLearning` : `TicketPriorityPredictor`, `PriorityModelStore`, colonnes, métadonnées et DI propres à Priority.
- `backend/tools/ITTicketIntelligence.Training` : `PriorityTrainingPipeline`, `PriorityTrainer`, `PriorityEvaluation` et commandes `train-priority` / `predict-priority`.
- `Api/Controllers/PriorityPredictionsController` : adaptation HTTP uniquement.
- `models/ticket-priority/v1` : ZIP du pipeline complet, métadonnées, rapport et réponse HTTP de démonstration.

Le lecteur CSV commun conserve maintenant Priority, auparavant ignoré, sans changer les features Category. Le dispatcher console ajoute deux commandes. Le pipeline Category, son prédicteur, son store et les artefacts sauvegardés ne sont pas modifiés.

L’API construit le singleton Priority au démarrage depuis `MachineLearning:PriorityModelDirectory`, par défaut `Models/TicketPriority` relatif à AppContext.BaseDirectory. Le build/publish copie le bundle versionné. Le chargement vérifie checksum SHA-256, contrat, features, labels, schéma et ordre des scores. Un artefact absent/incompatible bloque le démarrage. Aucun entraînement ou chargement de CSV n’a lieu dans une requête HTTP.

Le moteur reste en mémoire ; SemaphoreSlim sérialise les appels concurrents. L’annulation est vérifiée pendant l’attente et avant/après prédiction ; une opération ML.NET synchrone déjà commencée n’est pas interrompue. Le store sauvegarde le dossier complet par renommage d’un dossier temporaire adjacent et refuse d’écraser une version existante. Un nouveau dossier se configure via `MachineLearning__PriorityModelDirectory`, puis l’API doit redémarrer. Les artefacts doivent venir d’un producteur de confiance : un checksum seul ne garantit pas leur authenticité.

## Features et fuites

Title/Description passent par FeaturizeText. Category est encodée nominalement en OneHot ; les blocs sont concaténés et normalisés L2. Toutes les transformations sont ajustées sur training uniquement. Priority est la cible ; ExampleWeight sert au formateur, pas à la prédiction. Les tests changent ces valeurs et vérifient l’invariance des scores. RecommendedTeam, identifiants, résolution, commentaires et justifications sont exclus. Les reformulations d’un scénario restent ensemble.

Category provient ici des annotations du corpus et est traitée comme une donnée fournie à la création. Le manifeste initial reste inchangé ; le contrat enrichi figure dans les métadonnées Priority. Si la catégorie est prédite automatiquement, il faut des catégories hors pli sur training et des prédictions issues de training pour validation/test. Ce rapport ne mesure pas cette cascade ; la qualité limitée de Category pourra affecter Priority. Aucun champ métier n’est reconstruit depuis la priorité cible.

## Déséquilibre

| Priorité | Training | Validation | Test | Total | Poids sélectionné |
|---|---:|---:|---:|---:|---:|
| Low | 30 | 6 | 6 | 42 | 1,400000 |
| Medium | 62 | 10 | 14 | 86 | 0,677419 |
| High | 60 | 16 | 12 | 88 | 0,700000 |
| Critical | 16 | 4 | 4 | 24 | 2,625000 |

Medium + High représentent 72,62 % du training. Critical représente 9,52 % ; Medium est 3,875 fois plus fréquent. Les 16 tickets Critical correspondent à huit scénarios ; validation et test ne contiennent chacun que deux scénarios Critical, reformulés deux fois.

Les poids équilibrés sont calculés par `N_training / (4 × effectif_training_de_la_classe)`. Leur moyenne vaut 1 et chaque classe a le même poids total. Les variantes non pondérées utilisent des poids de 1. Validation/test ne servent pas au calcul, ne sont ni rééchantillonnés ni pondérés à l’évaluation. Le corpus est synthétique ; ces proportions ne représentent pas un trafic observé. Pour enrichir Critical, privilégier de nouveaux incidents indépendants.

La baseline, calculée sur training, prédit Medium : accuracy de 27,78 % sur validation et 38,89 % sur test.

## Résultats de priority-v1

Le gagnant des six variantes est **l2-0.0001-balanced**, choisi sur validation. Test est évalué ensuite, sans réentraînement sur validation/test. Les tests automatisés peuvent rejouer le protocole fixe sans modifier le bundle livré.

| Mesure | Validation | Test |
|---|---:|---:|
| Accuracy | 83,33 % | 75,00 % |
| MacroAccuracy | 70,94 % | 65,48 % |
| Macro-F1 | 68,33 % | 65,74 % |
| LogLoss | 0,556556 | 0,703727 |
| Rappel Critical | 0 % | 25 % |
| Faux négatifs Critical | 4/4 | 3/4 |

Matrice de confusion test : lignes réelles, colonnes prédites, ordre Low, Medium, High, Critical.

```text
             Low Medium High Critical
Low            4      2    0        0
Medium         2     11    1        0
High           0      0   11        1
Critical       0      0    3        1
```

Matrice validation, même ordre :

```text
6 0  0 0
0 9  1 0
0 1 15 0
0 0  4 0
```

MacroAccuracy et métriques par classe complètent l’accuracy globale ([métriques ML.NET](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/metrics)). Le rapport inclut supports en tickets/scénarios, précision/rappel/F1, sous-estimations d’au moins deux niveaux et erreur ordinale moyenne. Les trois Critical manqués sont prédits High ; un High est prédit Critical. Aucune sous-estimation de deux niveaux n’est observée sur ce petit test, ce qui ne démontre pas son absence en production.

Le rappel Critical reste insuffisant malgré la pondération. Le corpus est petit, synthétique, ses paraphrases sont corrélées et les catégories sont supposées correctement fournies. Ces résultats ne justifient pas une gestion autonome des incidents majeurs. Les futurs intervalles d’incertitude devront rééchantillonner les scénarios. Aucun SLA de détection n’est revendiqué.

## Confiance et démonstration

SdcaMaximumEntropy produit une distribution normalisée ([formateur ML.NET](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.trainers.sdcamaximumentropymulticlasstrainer)). Le service prend le score du bon slot, sans second softmax. L’ordre interne est Critical, High, Low, Medium ; les matrices sont explicitement réordonnées en ordre métier. `confidence` est un score entre 0 et 1, sans calibration démontrée sur trafic réel. La pondération change l’importance des classes ; ce score n’est pas une probabilité empirique de justesse. Aucun seuil n’est ajusté sur test.

L’exemple `samples/priority-prediction.json` décrit un arrêt de production dans tous les bureaux, catégorie Network. La prédiction depuis l’artefact chargé est :

```json
{
  "predictedPriority": "Critical",
  "confidence": 0.902999222278595,
  "modelVersion": "priority-v1"
}
```

Ce résultat correspond à la priorité attendue pour cet exemple ; le rappel Critical global reste 25 %. La réponse HTTP figure dans `models/ticket-priority/v1/smoke-prediction.json`.

## Commandes et tests

Depuis la racine :

```powershell
python scripts/datasets/prepare_tickets.py
dotnet build backend/ITTicketIntelligence.sln --configuration Release

# v1 est déjà livrée ; choisir un nouveau dossier pour un nouvel entraînement.
dotnet run --project backend/tools/ITTicketIntelligence.Training --configuration Release -- train-priority --data data/tickets/v1 --output models/ticket-priority/v2 --version priority-v2

# Prédiction sans entraînement.
dotnet run --project backend/tools/ITTicketIntelligence.Training --configuration Release -- predict-priority --model models/ticket-priority/v1 --input samples/priority-prediction.json

. ./scripts/Import-LocalEnvironment.ps1
dotnet run --project backend/src/ITTicketIntelligence.Api
```

API démarrée :

```powershell
Invoke-RestMethod http://127.0.0.1:5080/api/predictions/priority -Method Post -ContentType application/json -InFile samples/priority-prediction.json
postman collection run "postman/collections/IT Ticket Intelligence" -i Smoke --no-report-events
```

Tests :

```powershell
./scripts/Initialize-Postgres.ps1
. ./scripts/Import-LocalEnvironment.ps1
dotnet test backend/ITTicketIntelligence.sln --configuration Release
```

Tests ajoutés : validation/annulation, catégorie obligatoire, limites, port, slots de scores, feature nominale Category, exclusion de la cible/des poids, concurrence, absence de lecture disque par requête, checksum/métadonnées, round-trip d’entraînement/sélection/persistance, poids issus de training, matrices/métriques, labels inconnus/contradictoires, HTTP/OpenAPI et remplacement du port. Les tests Category restent exécutés.

Livraison : build Release sans erreur ni avertissement ; **133 tests unitaires et 92 tests d’intégration réussis**. Postman local : **8 requêtes, 21 assertions réussies**, code de sortie 0. Ces assertions vérifient le contrat HTTP ; la qualité métier est décrite par les métriques ci-dessus.
