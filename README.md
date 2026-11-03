# ForgeConflict

**Détection et résolution des conflits de pilotes graphiques sous Windows 10 / 11.**

ForgeConflict identifie les conflits entre les pilotes graphiques AMD, NVIDIA et Intel et le reste
du système — applications, services Windows, autres pilotes, configuration matérielle — explique
chaque problème en clair, et propose une correction dont chaque changement est prévisualisé,
journalisé et réversible.

Version : `1.3.0` · Windows 10 1607+ / Windows 11 · `.exe` portable, sans installation (~71 Mo)

## Mises à jour automatiques

Au démarrage, puis toutes les 6 heures, ForgeConflict lit `latest.json` (release GitHub la plus récente).
Si une version plus récente existe, une fenêtre en français montre les nouveautés ; « Installer et
redémarrer » télécharge le nouvel `.exe`, le contrôle, le remplace après fermeture et relance l'application.
Page **Mises à jour** : bouton « Rechercher une mise à jour » et case pour couper la vérification
automatique (réglage dans `forgeconflict-update.json`, à côté de l'exe).

Contrôles avant tout remplacement : signature ECDSA P-256 du manifeste (clé publique embarquée), taille et
SHA-256 de l'exécutable, adresse limitée à `github.com/Heiphaistos/ForgeConflict/releases/download/`, et, si
l'exe actuel est signé Authenticode, même certificat sur le nouveau.

**Publier (mainteneur)** : `build.ps1` signe l'exe puis produit `publish/latest.json` + `latest.json.sig`
avec la clé privée `D:\MDP\forgeconflict-updater.key` (jamais dans le dépôt ; création : `tools/new-updater-key.ps1`).
Joindre `ForgeConflict.exe`, `latest.json` et `latest.json.sig` à la release `vX.Y.Z`.

## Principes

**Rien n'est modifié sans ton accord.** Aucune correction automatique. Chaque écriture est précédée
d'un aperçu exact — clé, valeur avant, valeur après — et d'une confirmation explicite.

**Toute écriture est réversible.** Le plan de restauration est écrit sur disque *avant* d'agir. Si
l'application plante, si l'écran devient noir ou si la machine redémarre en cours de route, la
transaction non validée est détectée au démarrage suivant et l'état précédent rétabli.

**Le scan ne peut rien casser.** La couche d'inventaire et le moteur de règles ne contiennent
aucun code d'écriture : un scan ne peut pas modifier la machine, même par accident.

**Le silence vaut refus.** Après une modification touchant l'affichage, un compte à rebours de
30 secondes exige une confirmation. Sans réponse — écran noir — le changement est annulé.

## Ce que fait l'application

| Page | Rôle |
|---|---|
| **Tableau de bord** | Anneau de santé des 10 sources, verdict des conflits |
| **Conflits** | Chaque constat : ce qui a été trouvé, pourquoi, ce que ferait la correction, quel risque |
| **Pilotes** | Version conseillée pour ta carte, avec le raisonnement |
| **Surveillance** | Overlays et incidents graphiques en direct — observe, n'agit jamais |
| **Inventaire** | État brut des dix sources |
| **Journal** | Traces techniques |
| **Désinstalleur** | Nettoyage complet façon DDU, avec mode sans échec orchestré |
| **Apparence** | 10 thèmes, contrastes validés WCAG |

### Les dix sources d'inventaire

Adaptateurs · magasin de pilotes · réglages graphiques (TDR, HAGS, MPO) · services constructeurs ·
processus overlay · événements GPU · traces de plantage · liens PCIe · alimentation · écrans

### Les neuf règles de détection

Doublons de paquets · pilote générique · pilote non signé · réglages TDR · service constructeur en
échec · overlays · historique de plantage · lien PCIe dégradé · configuration hybride

La gravité dépend du **contexte**, pas du symptôme : un lien PCIe réduit est une information sur un
portable, où c'est le câblage du constructeur, et un problème sur un PC de bureau.

## Le désinstalleur

La sortie du mode sans échec est **programmée avant d'y entrer**, et l'entrée est refusée si cette
programmation échoue. Un nettoyage qui meurt en route laisserait sinon la machine en boucle de mode
sans échec — le pire résultat que ce projet puisse produire.

## Construire

```powershell
.\build.ps1     # publie publish\ForgeConflict.exe
dotnet test ForgeConflict.sln
```

L'application exige les droits administrateur : le magasin de pilotes est inaccessible autrement.
Non signée, elle déclenchera SmartScreen au premier lancement.

## Documentation

- [Spécification de conception](docs/superpowers/specs/2026-08-05-forgeconflict-design.md)
- [Plans d'implémentation](docs/superpowers/plans/)
- [Preuves de vérification](docs/verification/)

## Licence

Usage privé.