# Journal des modifications

## 1.3.0

- Sécurité (projet de tests seulement) : xunit 2.9.2, xunit.runner.visualstudio 2.8.2, Microsoft.NET.Test.Sdk 17.11.1 ; retire System.Net.Http 4.3.0 et System.Text.RegularExpressions 4.3.0 vulnérables (tirés par NETStandard.Library 1.6.1). L'application elle-même est inchangée.

## 1.2.0

- Mise à jour automatique : vérification silencieuse au démarrage puis toutes les 6 heures, bouton « Rechercher une mise à jour » (page « Mises à jour ») et réglage pour la couper.
- Intégrité : manifeste `latest.json` signé (ECDSA P-256), taille et SHA-256 de l'exécutable contrôlés, adresse de téléchargement limitée aux releases du dépôt, signature Authenticode du nouvel exécutable comparée à celle de l'actuel.
- Installation silencieuse : l'exécutable est remplacé après la fermeture de l'application, qui redémarre toute seule (en administrateur, comme avant).
- Publication : `build.ps1` produit `publish/latest.json` et `latest.json.sig` (clé privée hors dépôt).

## 1.1.0

- Version précédente (voir l'historique git).
