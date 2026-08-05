# ForgeGrafic

**Détection et résolution des conflits de pilotes graphiques sous Windows 10 / 11.**

ForgeGrafic identifie les conflits entre les pilotes graphiques AMD, NVIDIA et Intel et le reste du
système — applications, services Windows, autres pilotes, configuration matérielle — explique
chaque problème en clair, et propose une correction dont chaque changement est prévisualisé,
journalisé et réversible.

Version : `0.1.0` · Cible : Windows 10 1607+ / Windows 11 · Format : `.exe` portable, sans installation

## Principes

**Rien n'est modifié sans ton accord.** ForgeGrafic détecte et explique en temps réel, mais aucune
écriture n'a lieu sans une confirmation explicite, précédée d'un aperçu exact du changement
(clé, valeur avant, valeur après).

**Toute écriture est réversible.** Chaque correction ouvre une transaction journalisée sur disque
*avant* d'agir. Si l'application plante, si l'écran devient noir, ou si le PC redémarre en cours de
route, ForgeGrafic détecte la transaction non validée au démarrage suivant et restaure l'état
précédent.

**Le scan ne peut rien casser.** La couche d'inventaire est strictement en lecture seule et ne
contient aucun code d'écriture.

## Fonctionnalités

- Détection des conflits pilote ↔ pilote, pilote ↔ services Windows, pilote ↔ applications
- Analyse de la configuration matérielle et énergétique (Resizable BAR, lien PCIe, MUX, Optimus)
- Analyse des traces de plantage GPU (TDR, Event 4101/14, LiveKernelReports, minidumps, WHEA)
- Surveillance temps réel des processus et événements système liés au GPU
- Désinstallation propre de pilote graphique avec orchestration du mode sans échec
- Recommandation de version de pilote adaptée à ta carte et à ta configuration

## Documentation

- [Spécification de conception](docs/superpowers/specs/2026-08-05-forgegrafic-design.md)

## Licence

Usage privé.
