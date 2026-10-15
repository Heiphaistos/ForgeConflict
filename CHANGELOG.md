# Journal des modifications

## 1.2.0

- Mise à jour automatique : vérification silencieuse au démarrage puis toutes les 6 heures, bouton « Rechercher une mise à jour » (page « Mises à jour ») et réglage pour la couper.
- Intégrité : manifeste `latest.json` signé (ECDSA P-256), taille et SHA-256 de l'exécutable contrôlés, adresse de téléchargement limitée aux releases du dépôt, signature Authenticode du nouvel exécutable comparée à celle de l'actuel.
- Installation silencieuse : l'exécutable est remplacé après la fermeture de l'application, qui redémarre toute seule (en administrateur, comme avant).
- Publication : `build.ps1` produit `publish/latest.json` et `latest.json.sig` (clé privée hors dépôt).

## 1.1.0

- Version précédente (voir l'historique git).
