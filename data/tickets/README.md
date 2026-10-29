# Dataset IT Help Desk — v1.0.0

Corpus synthétique de **240 tickets anglais**, écrit pour préparer trois tâches de classification multiclasse ML.NET. Il représente **120 scénarios indépendants dans la conception**, chacun avec deux reformulations rédigées. Les reformulations sont corrélées : le corpus ne représente pas 240 incidents indépendants observés.

Il s’agit d’un jeu initial de développement et de démonstration. La préparation de ce corpus n’effectue aucun entraînement ni vectorisation. L’entraînement séparé du premier classifieur est décrit dans `docs/category-classification.md` à la racine du projet. Les annotations et leurs justifications sont rédigées avec les exemples ; elles n’ont pas reçu de revue indépendante par des spécialistes Help Desk. Aucune donnée de client, aucun export de la base applicative, aucun secret réel n’est utilisé.

## Fichiers

```text
data/tickets/
├── README.md
├── annotation-policy.md
└── v1/
    ├── source/scenarios.jsonl
    ├── training.csv
    ├── validation.csv
    ├── test.csv
    ├── manifest.json
    └── validation-report.json
scripts/datasets/
├── prepare_tickets.py
└── test_prepare_tickets.py
```

`source/scenarios.jsonl` est la source éditoriale : identifiant de scénario, trois labels, justification et deux variantes. Chaque ligne JSON est un scénario complet. Les CSV sont des exports déterministes de cette source. `manifest.json` conserve le schéma, la graine, la provenance et les empreintes SHA-256 de la source, du générateur, des CSV et du rapport. Le rapport détaille les contrôles, distributions simples et croisées, limites et plus fortes ressemblances lexicales entre partitions.

## Features et labels

CSV UTF-8 sans BOM, séparateur virgule, en-tête présent, échappement CSV standard des guillemets et des virgules. Le futur lecteur ML.NET devra activer les champs entre guillemets et respecter les colonnes suivantes :

| Index | Colonne | Rôle |
|---|---|---|
| 0 | TicketId | UUID déterministe ; traçabilité uniquement |
| 1 | ScenarioId | UUID du groupe de reformulations ; séparation et évaluation groupées uniquement |
| 2 | Title | Feature textuelle, disponible à la création |
| 3 | Description | Feature textuelle, disponible à la création |
| 4 | Category | Label du modèle de catégorie |
| 5 | Priority | Label du modèle de priorité |
| 6 | RecommendedTeam | Label du modèle de routage |

La future entrée textuelle combine `Title` et `Description`, en conservant la ponctuation utile aux erreurs techniques. Les impacts, l’étendue de l’incident, l’environnement et les solutions de contournement sont exprimés dans le texte initial. Les observations et vérifications faites par le demandeur avant création peuvent y figurer.

| Modèle futur | Features v1 | Label | Classes |
|---|---|---|---|
| Ticket Category Classification | Title, Description | Category | Hardware, Software, Network, Database, Security, Billing, Account, Application, Other |
| Ticket Priority Classification | Title, Description ; Category fournie à la création pour le protocole Priority | Priority | Low, Medium, High, Critical |
| Team Recommendation | Title, Description | RecommendedTeam | Support, Network, Database, Security, Backend, Infrastructure |

Les trois tâches utilisent les **mêmes partitions**, avec une cible différente ; neuf CSV dupliqués ne sont pas nécessaires. Le manifeste v1 conserve le contrat textuel initial. Le modèle Priority ajoute explicitement Category selon un protocole conditionnel à une catégorie fournie à la création, documenté dans `docs/priority-classification.md` et ses métadonnées ; les annotations du corpus ne prouvent pas cette disponibilité dans un système réel. Les autres labels sont exclus. Pour un routage ou une priorité utilisant des catégories prédites, une future cascade devra produire des prédictions hors pli sur les seuls groupes de training, puis des prédictions de modèles ajustés sur training pour validation/test. Ne pas utiliser directement les vrais labels comme s’ils étaient des prédictions disponibles en production.

Ne pas utiliser comme features : UUID, nom de fichier/partition, justification, commentaires, résolution, équipe effectivement affectée après enquête, prédictions historiques, statut final, durée de résolution ou audit. Les justifications restent dans la source, jamais dans les CSV d’entraînement.

Les équipes sont des labels métier, pas des GUID de la table Teams ni un nouvel enum du domaine. Une future intégration devra définir une correspondance configurée vers les équipes actives de l’application.

## Séparation des données

| Partition | Tickets | Scénarios | Proportion |
|---|---:|---:|---:|
| training | 168 | 84 | 70 % |
| validation | 36 | 18 | 15 % |
| test | 36 | 18 | 15 % |
| Total | 240 | 120 | 100 % |

La séparation est faite **avant toute transformation apprise**, sur les groupes de scénarios. Les deux formulations d’un incident restent ensemble. La graine `20261023` et les quotas par catégorie sont fixes. Parmi 4 000 affectations candidates, le générateur choisit celle qui limite l’écart de répartition des priorités et équipes ; il impose au moins deux scénarios par priorité et par équipe dans chaque partition. Ce choix utilise les annotations pour stratifier, sans entraîner ni comparer de modèle.

La couverture minimale des classes rares prend priorité sur un ratio 70/15/15 exact pour chaque label. Par exemple, l’équipe Database ne dispose que de sept scénarios au total : trois en training, deux en validation et deux en test. La couverture est assurée, mais le support d’apprentissage est faible. Les catégories Billing et Other ont chacune un seul scénario dans chaque partition d’évaluation.

- **training** : apprentissage futur du vocabulaire, des transformations et du modèle ; éventuelle validation croisée interne groupée par ScenarioId.
- **validation** : choix des hyperparamètres, du modèle, des seuils d’abstention et de calibration.
- **test** : évaluation finale une fois le protocole figé ; ne pas ajuster le système en fonction de ses résultats.

Les labels de test sont accessibles pour l’audit du dataset ; aucun entraînement n’a eu lieu. Le gel est une discipline de version et de protocole, pas un contrôle d’accès cryptographique. Si les données ou les groupes changent après livraison, créer une nouvelle version de corpus avec son manifeste et conserver v1. Le générateur refuse de remplacer un artefact existant différent. Pour une nouvelle version, copier la source dans un nouveau dossier, réviser explicitement les quotas et le protocole du générateur, puis régénérer et revalider ce dossier ; les quotas fournis sont propres à v1.

## Distributions et déséquilibre

Les nombres ci-dessous sont issus du rapport généré, pas d’un trafic réel estimé.

| Catégorie | training | validation | test | Total |
|---|---:|---:|---:|---:|
| Hardware | 20 | 4 | 4 | 28 |
| Software | 24 | 4 | 4 | 32 |
| Network | 24 | 6 | 6 | 36 |
| Database | 16 | 4 | 4 | 24 |
| Security | 16 | 4 | 4 | 24 |
| Billing | 16 | 2 | 2 | 20 |
| Account | 24 | 6 | 6 | 36 |
| Application | 20 | 4 | 4 | 28 |
| Other | 8 | 2 | 2 | 12 |

| Priorité | training | validation | test | Total |
|---|---:|---:|---:|---:|
| Low | 30 | 6 | 6 | 42 |
| Medium | 62 | 10 | 14 | 86 |
| High | 60 | 16 | 12 | 88 |
| Critical | 16 | 4 | 4 | 24 |

| Équipe | training | validation | test | Total |
|---|---:|---:|---:|---:|
| Support | 58 | 10 | 10 | 78 |
| Network | 20 | 4 | 6 | 30 |
| Database | 6 | 4 | 4 | 14 |
| Security | 18 | 4 | 4 | 26 |
| Backend | 40 | 6 | 4 | 50 |
| Infrastructure | 26 | 8 | 8 | 42 |

Les tickets Critical sont volontairement représentés pour pouvoir tester le protocole : leurs 10 % ne prétendent pas refléter la fréquence réelle d’incidents majeurs. Dans training, Support a presque dix fois plus d’exemples que Database. Une accuracy globale peut donc masquer une mauvaise prise en charge des équipes rares. Les distributions conjointes du rapport montrent aussi que toutes les combinaisons catégorie/priorité/équipe ne sont pas couvertes.

Pour enrichir le corpus, privilégier de nouveaux incidents indépendants des classes rares plutôt que davantage de reformulations. Ne pas rééquilibrer validation/test. Toute pondération ou réplication éventuelle devra être calculée et appliquée uniquement à training, puis choisie sur validation. Ne pas supposer qu’un formateur ML.NET supporte les poids sans vérifier son contrat. Garder une évaluation sur un trafic réel anonymisé avant tout usage opérationnel.

## Fuites et biais à prévenir

1. **Reformulations et incidents partagés** : jamais de split aléatoire par ligne après augmentation. En données réelles, grouper aussi les tickets enfants d’une panne majeure, doublons, campagnes et tickets issus du même modèle de message.
2. **Informations postérieures** : ne pas apprendre à partir de la résolution, des actions de l’agent ou de l’équipe finale inscrites dans le texte. Les extraits doivent correspondre à l’état réellement disponible au moment de la prédiction.
3. **Labels comme entrées** : ne pas entraîner un modèle avec la vraie cible, ni faire dépendre le routage des vrais labels d’une autre tâche. Les identifiants et justifications peuvent également révéler indirectement la cible.
4. **Prétraitement global** : ne pas apprendre le vocabulaire, IDF, sélection de features, normalisation statistique, poids de classe ou calibration sur l’ensemble des CSV. Ces étapes appartiendront au pipeline entraîné sur training.
5. **Test utilisé pour sélectionner** : ne pas choisir un modèle, un seuil ou une nouvelle formulation après inspection de ses erreurs de test. Versionner un nouveau protocole si le test est devenu un jeu de développement.
6. **Style synthétique** : formulation propre, impacts explicites et mentions fréquentes d’absence d’alternative peuvent créer des raccourcis. Des fautes réelles, informations manquantes, contradictions et styles variés devront être ajoutés avec de nouveaux groupes, sans recopier les textes existants entre partitions.
7. **Organisation et temps** : le routage reflète une convention fictive de responsabilité. Les futurs tickets réels nécessiteront une séparation temporelle et, selon le contexte, par client/site, ainsi qu’une revue des changements d’équipes et de politique de priorité.

L’absence de doublon lexical détecté ne prouve pas l’absence de fuite sémantique. La similarité maximale de bigrammes entre partitions est actuellement 0,1111, sous le seuil technique 0,65 ; ce seuil n’est pas une garantie scientifique. Une revue humaine des scénarios et des frontières de labels reste nécessaire avant de traiter les annotations comme une vérité métier.

## Métriques à utiliser plus tard

| Tâche | Mesures principales | Compléments |
|---|---|---|
| Catégorie | Macro-F1, précision/rappel par catégorie | MicroAccuracy, MacroAccuracy, matrice de confusion, LogLoss |
| Priorité | Rappel Critical et macro-F1 | Faux négatifs Critical, sous-estimations de deux niveaux ou plus, précision Critical, matrice de confusion, coût métier ordinal |
| Équipe | Top-1 accuracy et macro-F1 | Rappel par équipe, Top-2 accuracy, matrice de confusion, performance et couverture après abstention |

ML.NET expose notamment MicroAccuracy, MacroAccuracy, LogLoss, LogLossReduction, ConfusionMatrix et TopKAccuracy. Les métriques et limites sont décrites dans la [documentation Microsoft](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/metrics) et le [contrat MulticlassClassificationMetrics](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.data.multiclassclassificationmetrics). Macro-F1 et les erreurs ordinales devront être calculées séparément à partir des prédictions/matrices ; ne pas les présenter comme déjà mesurées.

MicroAccuracy correspond au taux global de bonnes prédictions. MacroAccuracy donne le même poids aux classes ; elle n’est pas Macro-F1. LogLoss sert à examiner la qualité des distributions prédites, mais un score élevé n’est pas automatiquement une probabilité calibrée. Pour une priorité ordinale, documenter explicitement l’ordre Low < Medium < High < Critical et le coût d’une sous-estimation ; ne pas transformer arbitrairement ce problème en régression sans comparaison.

Comparer ultérieurement chaque modèle à une baseline majoritaire calculée sur training et à un routage simple. Rapporter le support de chaque classe en **tickets et scénarios**. Les intervalles d’incertitude devront rééchantillonner les scénarios, pas les paraphrases indépendamment. Avec seulement 18 scénarios de test et parfois un seul scénario par catégorie, une estimation de performance serait très instable. Aucun score de modèle ni seuil d’acceptation n’est annoncé à cette étape.

## Exécuter la préparation et la validation

Python 3.12 ou ultérieur suffit ; uniquement la bibliothèque standard. Cet utilitaire hors application ne modifie aucune couche .NET, aucune base PostgreSQL et ne réalise aucun apprentissage.

Depuis la racine du projet :

```powershell
# Vérifier les fichiers livrés, les empreintes et les invariants.
python scripts/datasets/prepare_tickets.py

# Reproduire les fichiers absents ; un fichier existant différent fait échouer la commande.
python scripts/datasets/prepare_tickets.py --build

# Tests positifs et corruptions volontaires dans des dossiers temporaires.
python -m unittest discover -s scripts/datasets -p 'test_*.py' -v
```

Contrôles exécutés : schéma JSONL/CSV strict, champs obligatoires et bornes compatibles avec Ticket, labels autorisés, identifiants uniques, cohérence des annotations d’un groupe, classes présentes dans chaque partition, groupe absent des autres partitions, absence de textes normalisés identiques et de titres partagés entre groupes, détection lexicale de quasi-doublons entre partitions, reconstruction déterministe et comparaison des empreintes. La validation de schéma ne vérifie pas automatiquement la langue ou la justesse métier des annotations.
