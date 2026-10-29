# Ticket Category Classification

Contrat défini avant implémentation : `POST /api/predictions/category` reçoit `title` (1–160 caractères non blancs) et `description` (1–8000 caractères non blancs). Réponse 200 : `category` (nom d’enum), `confidence` (score entre 0 et 1), `modelVersion`. Validation : 400 ProblemDetails. Aucun ticket, priorité, affectation ou historique n’est modifié par cet endpoint.

L’entraînement est un outil console séparé. L’API charge un artefact versionné au démarrage, vérifie son intégrité et réutilise son prédicteur ; aucun entraînement ou accès au dataset n’est effectué pendant une requête. Le port applicatif `ITicketCategoryPredictor` ne contient aucun type ML.NET. Remplacer son implémentation conserve le contrat HTTP.

Le dataset v1 est synthétique et petit. Une confiance de modèle n’est pas une garantie de justesse ni une probabilité calibrée sur du trafic réel. Le test sert à l’évaluation finale et ne doit pas guider le choix des hyperparamètres.

## Organisation et dépendances

- `Application/Intelligence` : `ITicketCategoryPredictor`, entrée textuelle validée, résultat et query handler. Aucun type ou package ML.NET.
- `Infrastructure/MachineLearning` : adaptateur `TicketCategoryPredictor`, contrats de colonnes internes, chargement/sauvegarde `CategoryModelStore` et enregistrement DI.
- `backend/tools/ITTicketIntelligence.Training` : console d’entraînement/prédiction, lecture du dataset, pipeline d’apprentissage et évaluation. L’API ne référence pas ce projet.
- `Api/Controllers/CategoryPredictionsController` : adaptation HTTP vers le handler applicatif.
- `models/ticket-category/v1` : modèle complet `model.zip`, `metadata.json`, `evaluation.json`.
- `samples/category-prediction.json` : entrée de démonstration indépendante des lignes d’évaluation.

Domain conserve ses dépendances nulles. Aucune logique ML n’est ajoutée aux entités, aux repositories ou aux contrôleurs de tickets. Le Worker n’entraîne aucun modèle. La prédiction proposée n’applique pas une catégorie au ticket et ne crée pas de TicketPrediction ; l’orchestration persistante de l’intelligence sera une étape distincte.

## Pipeline d’entraînement

1. Lire les partitions existantes, vérifier leurs empreintes SHA-256 contre le manifeste, le schéma CSV avec champs entre guillemets, les classes, identifiants et groupes disjoints. Les fichiers ne sont pas redistribués aléatoirement.
2. Charger uniquement Title, Description et Category dans les colonnes ML.NET. Category sert uniquement à encoder Label ; Priority, RecommendedTeam et les identifiants ne sont pas des features.
3. Ajuster MapValueToKey sur training, avec ordre des catégories par valeur.
4. FeaturizeText sur Title et Description : normalisation de casse, features de mots/caractères et normalisation L2. Toutes les transformations apprises sont ajustées sur training uniquement.
5. Ajuster SdcaMaximumEntropy : graine 20261024, 200 itérations maximum, un thread, régularisation L2 de 0,01. Ce premier pipeline est fixé avant l’entraînement ; aucune recherche d’hyperparamètres n’a été effectuée.
6. Mesurer validation, puis test avec le même pipeline, sans réentraînement sur validation/test. Calculer une baseline majoritaire depuis training.
7. Sauvegarder le pipeline complet et les métadonnées dans un dossier temporaire adjacent, puis publier le dossier par renommage. Un dossier de version existant n’est jamais écrasé.

Les tests de régression peuvent réexécuter cette évaluation fixe pour vérifier le round-trip. Ils n’optimisent pas les paramètres sur test. Les scores de test déjà observés ne doivent pas servir à choisir un prochain modèle ; toute amélioration devra suivre un protocole de sélection sur training/validation et tenir compte de l’exposition du test lors des itérations.

## Chargement et prédiction

`MachineLearning:CategoryModelDirectory` est lu par la factory DI lorsque le singleton est construit au démarrage. Un chemin relatif est résolu depuis `AppContext.BaseDirectory`. La valeur par défaut `Models/TicketCategory` correspond au bundle copié automatiquement par build/publish de l’API depuis `models/ticket-category/v1`.

Le chargement vérifie la version du contrat, les deux features attendues, les neuf catégories, le checksum du ZIP, le schéma d’entrée et les noms/positions des scores. Une prédiction de démarrage exerce le pipeline désérialisé. En l’absence d’artefact valide, le démarrage échoue avec un message indiquant l’entraînement séparé requis ; aucun modèle n’est créé automatiquement et aucun fallback aléatoire n’est utilisé.

Le singleton garde le pipeline et son PredictionEngine en mémoire. Un SemaphoreSlim sérialise les appels à ce moteur mutable, qui [n’est pas thread-safe](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/machine-learning-model-predictions-ml-net). Ce choix simple convient au prototype local ; un pool borné de moteurs pourra augmenter le débit sans changer le port applicatif. Le token d’annulation est propagé, vérifié avant/après prédiction et utilisé pendant l’attente. ML.NET ne permet pas d’interrompre une prédiction synchrone déjà commencée.

La catégorie est reliée à la position correcte du vecteur Score par les annotations SlotNames sauvegardées, sans supposer l’ordre numérique de l’enum. SdcaMaximumEntropy produit une distribution normalisée via softmax ([documentation du formateur](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.trainers.sdcamaximumentropymulticlasstrainer)). `confidence` est le score de la catégorie retenue, compris entre 0 et 1 ; on n’applique pas un second softmax. Les scores invalides, non finis ou incohérents avec la catégorie sont rejetés.

Le bundle est chargé une fois par démarrage. Pour remplacer les poids, produire un nouveau dossier et configurer son chemin, puis redémarrer l’API. Pour remplacer la technologie, fournir un autre `ITicketCategoryPredictor` à la racine DI : aucun changement de route ou de contrôleur n’est nécessaire. Les fichiers doivent provenir d’un producteur de confiance ; le checksum détecte une altération accidentelle, pas une falsification simultanée du modèle et de ses métadonnées.

## Commandes

Depuis la racine :

```powershell
python scripts/datasets/prepare_tickets.py
dotnet restore backend/ITTicketIntelligence.sln

# La version v1 est déjà livrée. Utiliser un nouveau dossier pour une nouvelle exécution.
dotnet run --project backend/tools/ITTicketIntelligence.Training --configuration Release -- train --data data/tickets/v1 --output models/ticket-category/v2 --version category-v2

# Charger v1 depuis le disque et prédire sans entraîner.
dotnet run --project backend/tools/ITTicketIntelligence.Training --configuration Release -- predict --model models/ticket-category/v1 --input samples/category-prediction.json

# L’API utilise par défaut le bundle v1 copié lors du build.
. ./scripts/Import-LocalEnvironment.ps1
dotnet run --project backend/src/ITTicketIntelligence.Api
```

Surcharge pour une version alternative existante :

```powershell
$env:MachineLearning__CategoryModelDirectory = (Resolve-Path models/ticket-category/v2).Path
dotnet run --project backend/src/ITTicketIntelligence.Api
```

La chaîne de connexion PostgreSQL reste configurée comme auparavant. L’endpoint de prédiction lui-même n’utilise pas la base. Ne pas placer de secret dans le bundle ou dans la configuration ML.

Exemple HTTP, API démarrée :

```powershell
Invoke-RestMethod http://127.0.0.1:5080/api/predictions/category -Method Post -ContentType application/json -InFile samples/category-prediction.json
postman collection run "postman/collections/IT Ticket Intelligence" -i Smoke --no-report-events
```

## Résultats mesurés de category-v1

168 tickets / 84 scénarios de training ; chaque partition d’évaluation contient 36 tickets / 18 scénarios. La baseline prédit Account, majoritaire dans training avec départage alphabétique des égalités.

| Mesure | Validation | Test |
|---|---:|---:|
| Accuracy (MicroAccuracy) | 0,166667 | 0,250000 |
| MacroAccuracy | 0,138889 | 0,231481 |
| LogLoss | 2,053697 | 2,008475 |
| Macro-F1 calculée | 0,105820 | 0,224401 |
| Accuracy de la baseline majoritaire | 0,166667 | 0,166667 |

Matrice de confusion **test** : lignes = catégorie réelle, colonnes = prédiction. Ordre commun : Account, Application, Billing, Database, Hardware, Network, Other, Security, Software.

```text
2 1 0 0 0 2 0 0 1
0 3 0 0 0 0 0 0 1
0 2 0 0 0 0 0 0 0
0 0 0 1 0 0 0 0 3
0 2 0 0 0 0 0 0 2
0 2 0 0 0 0 0 0 4
0 0 0 0 0 0 0 0 2
0 0 0 0 0 0 0 2 2
0 3 0 0 0 0 0 0 1
```

`evaluation.json` conserve les deux matrices complètes, l’ordre des classes, les supports et précision/rappel/F1 par classe. Les performances sont faibles : plusieurs classes ne sont jamais prédites correctement sur test. Cette première baseline n’est pas adaptée à une application automatique des catégories. Le petit corpus synthétique, les groupes corrélés et son style rédactionnel limitent aussi la portée de l’estimation.

La vraie exécution sur `samples/category-prediction.json`, portant sur une facture PDF, retourne :

```json
{
  "category": "Application",
  "confidence": 0.19048559665679932,
  "modelVersion": "category-v1"
}
```

Le label attendu selon notre politique est **Billing** : cet exemple est donc mal classé. Son résultat et son faible score sont présentés tels quels. Aucune règle par mots-clés n’est ajoutée pour masquer cette erreur.

## Vérification

```powershell
./scripts/Initialize-Postgres.ps1
. ./scripts/Import-LocalEnvironment.ps1
dotnet build backend/ITTicketIntelligence.sln --configuration Release
dotnet test backend/ITTicketIntelligence.sln --configuration Release --no-build
```

Les tests couvrent validation/annulation et délégation du handler, intégrité du ZIP, absence/incompatibilité des métadonnées, ordre des scores, exclusion du vrai label des features, scores bornés, concurrence, absence de lecture disque par requête, entraînement/sauvegarde/rechargement, cohérence des matrices avec les métriques, contrôle du dataset avant entraînement, HTTP/OpenAPI, remplacement du port et refus de démarrage sans artefact.

Les assertions HTTP vérifient le contrat de service et non une précision métier fictive. Aucun seuil de qualité ou de confiance n’est revendiqué comme validé sur un trafic réel.

Exécution de livraison : build Release sans erreur ni avertissement, 125 tests unitaires et 63 tests d’intégration réussis. Postman sur l’API locale : six requêtes et quinze assertions réussies, code de sortie 0. La prédiction HTTP réelle est conservée dans `models/ticket-category/v1/smoke-prediction.json`, avec catégorie attendue et résultat mesuré. La publication de l’API inclut le ZIP et ses métadonnées ; le projet d’entraînement n’est pas une dépendance de cette publication.
