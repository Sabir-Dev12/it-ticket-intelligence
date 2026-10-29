# Vérification du frontend, 2026-10-27

## Résultat applicatif

- `npm run build --prefix frontend` : TypeScript strict et build Vite réussis.
  Rollup retire deux annotations PURE mal placées dans la dépendance SignalR ;
  aucun échec de compilation. Bundle principal observé : environ 111 Kio gzip.
- `npm test --prefix frontend` : **14/14** tests, trois fichiers. Couverture ciblée
  du client HTTP, erreurs contrôlées, annulation/délai, compteurs, distribution,
  sept événements, déduplication, reconnexion, retrait des groupes et arrêt concurrent.
- `npm run test:e2e --prefix frontend` : **8/8** scénarios Chrome contre la
  simulation Postman, en passant par le vrai client fetch et le proxy Vite.
  Ils couvrent détails, recherche, historique navigateur, pagination, filtres,
  réponse vide, 503 et reprise, documents, absence d’écriture IA, thèmes,
  validation du token, routes invalides et navigation mobile au clavier.
- Axe : **aucune violation détectée** pour les huit vues testées, dans les deux
  thèmes, sur les règles WCAG A/AA activées. Ce contrôle automatisé n’est pas une
  certification d’accessibilité ni une vérification manuelle avec lecteur d’écran.
- Responsive Chrome : six pages à 280, 320, 390, 768, 1280 et 1600 pixels, avec
  texte à 125 %, sans débordement horizontal de la page. Échap rend le focus au
  bouton de navigation ; la priorité reste visible sur téléphone.
- `npm run qa:live --prefix frontend` : **réussi** sur l’API existante et PostgreSQL
  réel, avec profil de lecture temporaire. Totaux comparés à l’API, authentification,
  JoinTicket, interruption WebSocket, reconnexion/réabonnement et déconnexion.
  Aucun stockage du token et aucune écriture métier. Le premier essai attendait
  seulement cinq secondes au démarrage ; le contrôle attend désormais jusqu’à
  vingt secondes, tandis que le client HTTP garde son budget de quinze secondes.

Les scénarios HTTP simulés ne prouvent pas les mutations backend, le LLM ou le
MCP. Le smoke réel vérifie la connexion et le rattrapage, sans provoquer d’écriture
pour générer un événement. Les événements reçus sont couverts par les tests du
client et les tests d’intégration SignalR backend existants. Les tests xUnit ne
sont pas relancés dans ce jalon, qui ne modifie aucun code backend.

## Mesures du kit design-screen

Le rapport global demandé a été exécuté, puis relancé avec accès local autorisé :
**`RESULT: 0/52 checks passed = 0%`**. L’installation partagée n’a pas Python3
fonctionnel, ses exemples de référence ou ses dépendances de rendu. Certains
scripts contiennent aussi des commandes shell incompatibles avec Windows. Ce
résultat n’est ni masqué ni présenté comme une validation du frontend.

Le chargeur local `frontend/scripts/kit-loader.mjs` permet aux scripts de rendu
de résoudre Playwright et axe depuis le projet, sans modifier le kit partagé.
Les snapshots HTML proviennent du vrai DOM React, avec styles embarqués et scripts
retirés, dans `artifacts/frontend`, dossier ignoré par Git.

Contrôles ciblés exécutés avec DS_REQUIRE_BROWSER=1 :

| Contrôle | Résultat mesuré |
|---|---|
| verify_states, dashboard clair | 69 états élément/survol/focus, OK |
| verify_states, dashboard sombre | 69 états élément/survol/focus, OK |
| verify_responsive à 125 % | OK à 280/320/414 px |
| verify_overflow, revue indépendante | Aucun texte silencieusement coupé ni contrôle superposé à 320/414/1280 px |
| slop_tells strict, clair | Aucun HIGH ; un LOW sur deux surfaces neutres proches |
| slop_tells strict, sombre | OK dans la revue indépendante |
| taste_audit strict | OK, échelle typographique 6× ; 5 teintes clair, 6 sombre |

Les tests Axe portent sur les pages React interactives, pas seulement sur les
snapshots statiques. Les métriques des heuristiques ne constituent pas un score
de goût. Les captures de référence utilisent des données synthétiques annoncées,
sans remplacement automatique des données réelles dans l’application.

## Critique visuelle

Revue indépendante des six pages en clair/sombre, à 1280 et 390 pixels :
**VERDICT: ship**, aucun constat Critical ou Major. Le reviewer a également
exercé les contrôles dans Chrome.

Finitions appliquées après la revue : retrait du chevron trompeur de la carte
d’espace de travail, simplification des bandeaux techniques et réduction des
espacements de la bibliothèque mobile. Le nombre de chunks a été retiré des
cartes de documents. Les tests navigateur ont été relancés avec succès.

Amélioration éventuelle : une bibliothèque encore plus compacte sur mobile.
Ce verdict est un jugement de conception, pas un score mesuré.

## Fichiers et organisation

```text
frontend/
  src/
    api/          contrats DTO, client REST, tests HTTP
    app/          contexte du thème et de la session temps réel
    components/   layout, navigation, composants UI partagés
    hooks/        lecture annulable et états de ressource
    pages/        sept pages principales et vues de détail
    realtime/     client SignalR indépendant et tests de cycle de vie
    styles/       thème sémantique et compositions responsives
    App.tsx       routes et composition
  e2e/            scénarios Chrome/axe
  scripts/        captures, smoke réel, résolution du kit
  .env.example    cible du proxy Vite, aucun secret
  vite.config.ts
  vitest.config.ts
  playwright.config.ts
postman/
  mocks/frontend-read-api/
  simulations/frontend-read-api/
docs/
  frontend.md
  frontend-validation.md
```

Les fichiers package.json/package-lock.json et index.html sont actualisés.
README.md et docs/signalr.md relient ce jalon aux modules existants. Aucune
migration, aucun contrôleur et aucun service métier backend modifié.
