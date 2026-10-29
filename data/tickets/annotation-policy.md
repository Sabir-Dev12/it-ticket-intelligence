# Politique d’annotation v1

Cette politique définit des décisions cohérentes pour le corpus synthétique. Elle ne constitue ni un SLA réel ni une politique universelle. Les exemples sont les observations disponibles au moment de créer un ticket : pas de résolution ajoutée rétrospectivement pour rendre le label évident. La justification est une note d’annotation exclue des features.

## Catégorie principale

Une seule catégorie par exemple, choisie selon le domaine principal du besoin ou de l’incident. Une catégorie n’est pas nécessairement la cause racine déjà prouvée, ni l’équipe propriétaire.

| Label | Périmètre et frontière |
|---|---|
| Hardware | Défaut physique, panne électrique, composant ou accessoire ; un périphérique inaccessible à cause du routage réseau reste Network. |
| Software | Installation, compatibilité, licence ou fonctionnement d’un logiciel installé ; un défaut du produit web métier relève d’Application. |
| Network | Connectivité, DNS, DHCP, tunnel, routage, ingress ou TLS de transport ; une identité refusée avec connectivité fonctionnelle relève d’Account. |
| Database | Connexion SQL, requêtes, transactions, stockage du datastore, sauvegardes/restauration ou données non rafraîchies ; un échec général du produit sans signal de datastore reste Application. |
| Security | Suspicion/confirmation de compromission, exposition ou vulnérabilité ; une demande ordinaire de récupération d’accès n’est pas automatiquement Security. |
| Billing | Facture, paiement, renouvellement, remboursement et données administratives de facturation ; on conserve Billing même si un défaut logiciel doit être corrigé. |
| Account | Identité, authentification, habilitation, provisioning, récupération ou profil ; les indices de menace active font passer l’incident en Security. |
| Application | Fonctionnement ou évolution d’un produit métier : checkout, formulaire, rapport, endpoint ou job ; les domaines Billing/Account plus spécifiques prennent priorité lorsqu’ils constituent le besoin principal. |
| Other | Demande générale IT, orientation, signal non attribué ou contexte insuffisant pour choisir une catégorie précise ; pas une catégorie de secours pour les exemples difficiles mais suffisamment décrits. |

Les cas frontières ont une décision explicite : un client VPN incompatible reste Software mais se route vers Network ; un disque de base presque plein reste Database mais se route vers Infrastructure ; une facture PDF vide reste Billing et se route vers Backend ; une fuite entre tenants est Security et peut nécessiter une correction Backend.

## Priorité selon impact et urgence

| Label | Règle v1 |
|---|---|
| Low | Information, demande planifiée, commodité ou défaut cosmétique ; le travail actuel est possible sans urgence. |
| Medium | Dégradation limitée ou retard de test ; une alternative utilisable et approuvée permet de continuer, ou aucun arrêt de travail immédiat n’est décrit. |
| High | Employé entièrement bloqué sans alternative au moment où son travail doit commencer, fonction métier ou groupe significativement bloqué, perte de redondance à risque, exposition importante sans attaque active confirmée. |
| Critical | Arrêt complet d’un service essentiel à large impact sans alternative, compromission active avec dommages en cours, perte de données ou charges financières erronées systématiques et toujours en cours. |

Un mot alarmant ne suffit pas : un endpoint sandbox « down » n’a pas le même impact qu’un arrêt de toutes les transactions en production. Une vulnérabilité grave sans exploitation constatée est High dans ces exemples, tandis qu’une exfiltration confirmée et encore active est Critical. Un scanner unique qui bloque un poste de dispatch sans remplacement est High, pas automatiquement Critical pour toute l’organisation.

Ces conventions expliquent les labels fournis ; elles devront être confrontées aux SLA et à la criticité réelle des services avant tout usage réel. Les scénarios sont assez décrits pour appliquer cette politique, mais ne prouvent pas qu’un futur texte incomplet permette une décision certaine. Prévoir ensuite une abstention/escalade plutôt que forcer une confiance artificielle.

## Équipe responsable recommandée

Le label correspond au propriétaire pressenti de l’intervention, selon les signaux initiaux. Il ne représente ni la personne assignée, ni le premier contact de tous les tickets. Les scénarios de sécurité qui nécessitent une correction Backend ou Infrastructure peuvent demander une coordination Security ; le corpus mono-label ne représente que le propriétaire de l’action décrite.

| Label | Responsabilité fictive |
|---|---|
| Support | Postes/accessoires, applications utilisateur, récupération standard d’accès, administration simple de facturation et demandes générales. |
| Network | LAN/WAN, DNS, DHCP, pare-feu, VPN et chemins de communication. |
| Database | Moteur SQL, intégrité transactionnelle, permissions du datastore, requêtes, réplication et vues. |
| Security | Analyse et confinement de menace, exposition de credentials, capteurs de protection, revue privilégiée et offboarding à risque. |
| Backend | Code et workflows du produit, traitements d’import/export, facturation applicative, scopes d’intégration et défauts d’autorisation du produit. |
| Infrastructure | Serveurs, stockage, hyperviseurs, alimentation, orchestration, certificats d’ingress, distribution de logiciels et plateforme centrale d’identité. |

Les frontières restent organisationnelles : toutes les entreprises ne répartissent pas ainsi les certificats, l’identité ou les sauvegardes. Les catégories ont plusieurs équipes possibles et ne doivent pas devenir un simple dictionnaire catégorie → équipe. Vérifier les distributions croisées dans `v1/validation-report.json` lors d’un enrichissement.

## Enrichissement et revue ultérieurs

Ajouter de nouveaux incidents indépendants, préserver leurs liens de panne majeure et leur texte initial, anonymiser les données sensibles sans coder le label dans le remplacement. Prévoir une double annotation par des personnes compétentes sur un échantillon, résoudre les désaccords et mesurer l’accord avant d’utiliser les labels pour évaluer un système opérationnel. Une revue de forme automatisée ne remplace pas cette validation métier.

Conserver la provenance, la version de politique et les dates réelles lorsqu’elles existent ; ne pas inventer de dates pour simuler une évaluation temporelle. Pour un incident réaliste multi-équipe ou réellement ambigu, documenter la règle de choix du propriétaire ou élargir explicitement le schéma ; ne pas changer silencieusement les labels ni déplacer les exemples de test pour améliorer les futurs scores.
