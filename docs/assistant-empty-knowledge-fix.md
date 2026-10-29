# Assistant : réponse sans article Knowledge Base

Le problème signalé sur IT-1000 produisait `InvalidResponse` après des appels Ollama réussis et une recherche RAG sans résultat. L'interface traduisait cet échec de validation par une indisponibilité de service, ce que les journaux ne confirmaient pas.

La demande de réponse finale précise maintenant les limites de longueur, les identifiants de citations autorisés et le cas sans article : résumer les informations rapportées par le ticket, déclarer le contexte insuffisant et ne citer aucun document. Le schéma JSON transmis au fournisseur inclut les bornes validées par l'application.

Quand aucun chunk n'a été récupéré, une réponse sans citation peut être conservée même si le modèle renseigne mal son indicateur de contexte : le serveur impose une confiance de zéro et une revue humaine. Une citation inventée reste rejetée, y compris avec une Knowledge Base vide. Les règles de validation des citations lorsque des documents existent sont conservées. Aucun mécanisme ne transforme une réponse invalide ou une panne en faux succès.

L'activité des outils n'est plus rendue dans les conversations, y compris les anciens échanges et les états de chargement/échec. Les journaux d'audit et les contrôles d'autorisation restent actifs côté serveur. Le message d'échec ne prétend plus savoir qu'un service était indisponible et ne renvoie plus l'utilisateur vers des détails techniques cachés.

## Vérification effectuée

- Build de la solution Release : zéro erreur et zéro avertissement.
- 326 tests unitaires backend et 33 tests unitaires frontend réussis.
- 16 tests HTTP du protocole d'agent réussis sur la simulation Postman existante, notamment panne, timeout, refus, réponse tronquée et JSON invalide.
- Question française sur IT-1000 : réponse réelle enregistrée avec PostgreSQL, embeddings Ollama et qwen3:1.7b, sans source inventée. Une relance dans la conversation initialement signalée est également terminée avec succès ; l'ancien échec reste conservé dans l'historique.
- Test navigateur réel `frontend/e2e/assistant-live.spec.ts` : 1 scénario réussi. Vérifie la réponse API `Completed`, son affichage, l'absence d'outils et d'erreur, axe dans les deux thèmes et l'absence de débordement aux largeurs 280/320/414/1280. Les transitions de thème sont désactivées pendant les mesures. Captures : `artifacts/frontend/assistant-fixed-light.png` et `assistant-fixed-dark.png`.
- Build frontend final réussi. Le test navigateur réel est facultatif et lit les credentials dans `ASSISTANT_LIVE_EMAIL` et `ASSISTANT_LIVE_PASSWORD`, jamais dans le code.

Le gate global du kit reste **0/52** : ses harness et scripts de référence ne sont pas installés dans ce dépôt. Journal : `artifacts/frontend/assistant-fix-design-gate.txt`. Les contrôles ciblés ci-dessus ne constituent pas une certification de ce gate global. La suite d'intégration historique complète n'a pas été relancée.
