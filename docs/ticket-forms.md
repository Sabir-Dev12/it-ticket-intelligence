# Création et modification des tickets

Les formulaires utilisent désormais l'identité issue du JWT : l'utilisateur n'a plus à saisir son identifiant comme créateur ou auteur. L'affectation et la modification sont réservées à Support/Admin. Voir [Authentification JWT](authentication.md).

## Interface

`/tickets/new` utilise `CreateTicketPage`, `EditTicketForm` et les champs partagés
`TicketFormFields`. `/tickets/:id/edit` charge les valeurs sauvegardées. Les liens
Create ticket et Edit ticket sont intégrés à la liste et au détail existants.
Le formulaire reprend le thème partagé, les boutons, les états et la navigation.
Les pages de formulaire et Zod sont chargés dans un bundle séparé.

Champs : titre, description, catégorie, priorité, équipe et pièces jointes.
Le POST existant exige aussi `createdById` : le formulaire demande donc l’UUID
d’un utilisateur actif existant. Il ne crée pas d’utilisateur et ne simule pas
une connexion. Les équipes sont lues sur le serveur, jamais embarquées comme
liste de secours. Un ticket archivé, résolu ou fermé ne peut pas être modifié.

Aucun éditeur riche n’était installé. La description reste du texte brut ;
les balises HTML sont affichées littéralement. Aucun contenu utilisateur n’est
injecté comme HTML.

## Validation et sauvegarde

- Zod vérifie les champs, valeurs d’enum, UUID, caractères de contrôle et fichiers.
  Le titre contient 1 à 160 caractères, la description 1 à 8 000 après nettoyage
  des espaces périphériques. Catégorie et priorité sont toutes deux renseignées
  ou toutes deux absentes, conformément au domaine existant.
- Le backend répète la validation et vérifie utilisateur/équipe actifs, révision
  et statut. Les anciennes formes de POST/PUT restent acceptées.
- Création et modification des champs sont atomiques, y compris l’affectation.
  Les pièces jointes sont des opérations ultérieures, identifiées séparément.
- Une référence synchrone bloque les soumissions concurrentes avant même le
  prochain rendu React. Aucun POST/PUT n’est relancé automatiquement.
- Si les champs ont été enregistrés mais qu’un fichier échoue, le formulaire
  annonce ce résultat partiel, conserve les fichiers restants et propose leur
  reprise. Il ne recrée pas le ticket et ne renvoie pas les champs déjà sauvés.
- Un résultat de création incertain bloque une nouvelle soumission. Un conflit
  409 conserve le brouillon et impose une revue du ticket courant ; aucune
  révision récente n’est substituée silencieusement à celle éditée.
- Succès après confirmation de toutes les opérations : navigation vers le détail
  avec une notification. Annulation/navigation avec un brouillon : confirmation
  accessible ; fermeture/rechargement : avertissement natif du navigateur.
- La suppression de fichiers sauvegardés est d’abord préparée dans le formulaire,
  puis confirmée dans un dialogue nommant les fichiers avant le DELETE réel.

La navigation utilise désormais le Data Router de React Router pour `useBlocker`.
Les autres routes et la session mémoire existante sont conservées. Les dialogues
gèrent Échap, boucle Tab/Maj+Tab et retour du focus. Les sélecteurs personnalisés
prennent en charge flèches, Home/End, saisie de préfixe, Entrée et Échap.

## Analyze with AI

L’action est facultative, avant création comme pendant l’édition. Elle appelle
uniquement les trois routes ML.NET déjà présentes :

1. `POST /api/predictions/category` avec titre et description.
2. `POST /api/predictions/team` avec les mêmes entrées, indépendamment de Category.
3. `POST /api/predictions/priority` avec le texte et la catégorie prédite.

Elle n’appelle aucun LLM et ne crée aucun ticket. Les suggestions et confiances
sont affichées avant toute application. Use these suggestions remplit les champs ;
l’utilisateur peut les modifier, puis Save confirme leur persistance. Une équipe
n’est copiée que si le modèle ne s’abstient pas et qu’une unique équipe active
correspond au label. Sinon, la sélection reste manuelle. Les suggestions deviennent
inapplicables lorsque le titre ou la description a changé depuis la prédiction.

## Contrats API ajoutés ou étendus

Les manques ont été signalés avant implémentation : pièces jointes, équipes avant
création, classification/affectation initiales et modification groupée.

| Route | Contrat |
|---|---|
| `POST /api/tickets` | Ajout des champs optionnels `category`, `priority`, `assignedTeamId` ; `createdById` reste obligatoire ; retourne TicketDto, 201 |
| `PUT /api/tickets/{id}` | Ajout optionnel de `assignment: { teamId }` ; omission = conserver l’équipe, `{ teamId: null }` = désaffecter ; `revision` obligatoire |
| `GET /api/tickets/form-options` | `teams` actives, ordonnées, 200 maximum et `teamsTruncated` |
| `GET /api/tickets/{id}/attachments` | Métadonnées `items` et `revision`, sans contenu binaire |
| `POST /api/tickets/{id}/attachments` | Multipart : `attachmentId` UUID client, `revision`, `file` ; retourne `{ attachment, revision, created }`, 201 |
| `GET /api/tickets/{id}/attachments/{attachmentId}` | Fichier original, Content-Disposition attachment, nosniff, CSP sandbox, no-store |
| `DELETE /api/tickets/{id}/attachments/{attachmentId}?revision=N` | Supprime le fichier et retourne la nouvelle révision |

Upload : extension, MIME déclaré et signature du contenu doivent correspondre.
PNG/JPEG/PDF/TXT UTF-8 uniquement ; 1 octet à 5 MiB par fichier, cinq fichiers
maximum par ticket. SVG, HTML, exécutables, noms contenant chemins ou contrôles,
texte non UTF-8 et signatures incohérentes sont refusés. Les PNG ont aussi une
limite de dimensions. Les limites multipart et HTTP bornent les requêtes.
Il s’agit de contrôles de format et taille, pas d’une analyse antivirus.

Prévisualisation PNG/JPEG par URL Blob, TXT en texte échappé sur les premiers
4 KiB ; pour les PDF, métadonnées et téléchargement de l’original, sans lecteur
actif embarqué. Les URL Blob sont révoquées au retrait/démontage.

Un upload avec le même identifiant, nom et contenu déjà enregistrés est reconnu
sans ajouter de fichier ni révision. Un autre contenu avec le même identifiant
est refusé. Les compteurs, révisions et état actif sont contrôlés sous verrou de
ligne ; un ajout concurrent ne contourne pas la limite de cinq fichiers.

Les contrôles d’accès restent ceux du module Ticket Management local existant.
Ce jalon n’ajoute pas d’authentification utilisateur ou de nouveaux outils MCP
d’écriture. Les profils et conversations privées de l’assistant restent séparés.

## Persistance et Clean Architecture

Migration `20261028192056_TicketAttachments`, appliquée sur PostgreSQL local.
La table `app.ticket_attachments` possède PK, FK restrictive vers le ticket,
index `(ticket_id, created_at)` et contraintes de type, taille et empreinte.
Les métadonnées et octets `bytea` sont sauvegardés dans la même transaction que
la révision et l’audit. Le volume PostgreSQL Docker existant conserve ces données ;
aucun chemin fichier client n’est utilisé sur le serveur.

Les pièces jointes sont immuables : ajout/suppression, sans réécriture ; elles
ont un CreatedAt. La suppression conserve l’audit avec identifiant et empreinte,
mais retire définitivement les octets. Ce choix convient aux limites du portfolio ;
le port de stockage permettra un stockage objet pour un volume plus important.

Domain reste sans technologie externe et contrôle l’état/révision du ticket.
Application définit validation et ports ; `TicketFileService` publie SignalR après
commit. Infrastructure gère les transactions et les octets ; les contrôleurs
bornent et traduisent HTTP. Le LLM ne reçoit aucun accès à ce stockage.

## Vérifications

- Build Release .NET : zéro erreur/avertissement ; 314 tests unitaires et 305
  tests d’intégration PostgreSQL réussis.
- Frontend : 26 tests unitaires, dont les bornes Zod ; 19 scénarios existants et
  huit scénarios formulaire vérifiés dans Chrome.
- Cas couverts : création complète, compatibilité des anciens PUT, validation
  atomique des références, révisions périmées, spoof MIME, fichiers trop gros,
  limite de quantité, ticket archivé, téléchargement et audit, upload idempotent,
  application volontaire des suggestions, reprise d’upload, issue de création
  incertaine, brouillons, focus, clavier, axe et petits écrans à texte agrandi.
- Parcours Chrome réel → API → ML.NET → PostgreSQL : création, relecture des
  vraies prédictions, upload/aperçu d’image, affichage HTML littéral, modification,
  garde de navigation et suppression confirmée. Aucun fournisseur LLM simulé
  n’était nécessaire pour ce parcours.
- Collection Postman TicketForms : huit requêtes et huit assertions réussies sur
  l’API réelle. Collection complète lintée : 74 requêtes, zéro erreur/avertissement.
- Contrôles ciblés de tokens et références du thème réussis ; 69 états interactifs
  du formulaire et 69 du dialogue vérifiés dans chaque thème. Taste audit après
  ajustement de la largeur d’aide : aucun signal mesurable.
- Revue indépendante : aucun blocage ; annonce de fin d’analyse, retour du focus,
  référence stable du ticket et raccourci de relecture intégrés après les retours.
- Le rapport global du kit retourne 0/52 depuis le dépôt : ses scripts utilisent
  des chemins relatifs au kit et `python3`. Ce run ne valide pas l’application ;
  les contrôles ciblés précédents sont exécutés sur ses fichiers et rendus réels.

Les fixtures HTTP sont dans la simulation Postman existante, jamais dans le produit.
Le simulateur reproduit les scénarios UI ; les contrôles de contenu binaire et
de persistance sont vérifiés avec les vrais endpoints PostgreSQL.

Scripts locaux : `prepare-ticket-form-smoke.ps1`, `ticket-form-live.mjs`,
`cleanup-ticket-form-smoke.ps1` dans `frontend/scripts`. Les identifiants et captures
sont dans `artifacts/frontend/ticket-form-*`, ignorés. Le nettoyage archive
uniquement les tickets vérifiés du propriétaire temporaire, retire leurs fichiers
et désactive ses références ; les audits sont conservés.
