# ForgeConflict — Spécification de conception

Date : 2026-08-05 · Version cible : 0.1.0 · Statut : validé

## 1. Objectif

Fournir une application Windows portable qui détecte les conflits impliquant les pilotes graphiques
AMD, NVIDIA et Intel, les explique dans un langage compréhensible, et permet de les corriger sans
jamais risquer de laisser la machine sans affichage.

Le critère d'acceptation dominant est la sûreté : **une correction ratée doit toujours être
récupérable, y compris si la machine perd l'image ou redémarre en plein milieu de l'opération.**

## 2. Décisions arrêtées

| Sujet | Décision | Raison |
|---|---|---|
| Stack | WPF .NET 8 + WPF-UI (Fluent 2) | Rendu DirectX natif, survit à la désinstallation du pilote, pas de dépendance à un navigateur embarqué |
| Distribution | `.exe` self-contained single-file, win-x64 | Portable réel, aucune installation, W10 1607+ et W11 |
| Automatisation | Confirmation manuelle systématique | Aucune écriture sans accord explicite de l'utilisateur |
| Périmètre v1 | Complet (5 familles de détection) | Demande explicite : « parfait en tout point » |
| Désinstalleur | Complet, avec mode sans échec orchestré | Seule méthode qui retire réellement les restes de pilote |
| Catalogue pilotes | Base embarquée + rafraîchissement en ligne optionnel | Fonctionne hors-ligne, reste à jour entre deux versions |
| Privilèges | `requireAdministrator` dans le manifest | Le DriverStore et les clés pilote sont inaccessibles autrement |
| Tests | Machine virtuelle Hyper-V obligatoire | Aucun test d'écriture sur la machine hôte, sans exception |

### Contraintes acceptées

- L'exécutable pèse environ 150 Mo (runtime .NET embarqué).
- Non signé, l'application déclenchera SmartScreen et potentiellement un faux positif Defender.
  Une signature Authenticode reste possible ultérieurement.
- x64 uniquement. ARM64 n'est pas ciblé en v1.

## 3. Architecture

```
ForgeConflict.sln
├── ForgeConflict.Core        (net8.0-windows, classlib)  logique, zéro UI
│   ├── Inventory/          collecte de l'état système — LECTURE SEULE
│   ├── Rules/              moteur de détection — CONSTATE, NE CORRIGE JAMAIS
│   ├── Remediation/        actions de correction — TRANSACTIONNELLES
│   ├── Safety/             journal, snapshots, points de restauration, watchdog
│   ├── Catalog/            base de pilotes et moteur de recommandation
│   ├── Uninstaller/        machine à états de la désinstallation DDU-like
│   └── Monitoring/         surveillance temps réel
├── ForgeConflict.Native      (net8.0-windows, classlib)  P/Invoke isolé
├── ForgeConflict.App         (net8.0-windows, WinExe)    UI WPF Fluent 2
└── ForgeConflict.Tests       (net8.0-windows, xunit)     tests
```

**Règle d'or structurante** : le code de lecture et le code d'écriture vivent dans des assemblages
de responsabilité distincte. `Inventory` et `Rules` ne référencent jamais `Remediation`. Un scan ne
peut pas, par construction, modifier quoi que ce soit.

`ForgeConflict.Native` isole tous les appels P/Invoke (SetupAPI, CfgMgr32, DXGI, NVML, ADL,
PowrProf, BCD) derrière des façades typées. Aucun `DllImport` ailleurs dans la solution.

## 4. Couche Inventaire — lecture seule

Chaque collecteur a une responsabilité unique et produit un fragment de `SystemSnapshot`.

| Collecteur | Ce qu'il collecte | Source technique |
|---|---|---|
| `GpuCollector` | GPU présents, VRAM, état, adaptateur actif | DXGI `EnumAdapters1`, SetupAPI, `Win32_VideoController` |
| `DriverStoreCollector` | Pilotes installés, INF, doublons, orphelins | `pnputil /enum-drivers`, DriverStore |
| `ServiceCollector` | Services graphiques et constructeurs | SCM, `Win32_Service` |
| `ProcessCollector` | Processus actifs et DLL injectées dans le rendu | `Win32_Process`, énumération des modules |
| `RegistryCollector` | Clés pilote, TDR, HAGS, MPO | `HKLM\SYSTEM\CCS\Control\GraphicsDrivers`, classe `{4d36e968-...}` |
| `EventLogCollector` | Historique de plantages GPU | Journal System : events 4101, 14, WHEA |
| `CrashDumpCollector` | Traces de crash noyau | `LiveKernelReports`, `Minidump` |
| `HardwareCollector` | Lien PCIe, Resizable BAR, température | SetupAPI `DEVPKEY`, WMI, NVML/ADL |
| `PowerCollector` | Plan d'alimentation, PCIe Link State PM | `PowrProf`, WMI |
| `DisplayCollector` | Écrans, fréquences, MUX, mode hybride | `EnumDisplayDevices`, DXGI outputs |

### Robustesse de la collecte

Un collecteur défaillant ne doit jamais interrompre le scan. Chaque collecteur est exécuté isolé :

- capture d'exception locale, retour d'un fragment marqué `Failed` avec le détail de l'erreur ;
- timeout individuel — **WMI peut bloquer indéfiniment**, c'est un piège réel et fréquent ;
- exécution parallèle avec agrégation, jamais de `.Result` ni de `.Wait()` sur le thread UI.

L'interface affiche explicitement les fragments manquants plutôt que de présenter un diagnostic
silencieusement incomplet.

## 5. Moteur de règles

Une règle est un objet déclaratif :

```
Id           identifiant stable, ex. FG-HAGS-001
Category     DriverConflict | ServiceConflict | ApplicationConflict
             | HardwareConfig | CrashHistory | DriverVersion
Severity     Critique | Élevé | Moyen | Information
Title        formulation courte et lisible
Explanation  pourquoi c'est un problème, en français, pour un non-spécialiste
Evidence     ce qui a été trouvé concrètement, valeurs à l'appui
Detect(SystemSnapshot) -> Finding?
Remediations[]  zéro à plusieurs options de correction
```

Une règle constate ; elle ne corrige jamais. Comme les collecteurs, chaque règle est évaluée en
isolation : une règle qui lève une exception est signalée dans le journal et ignorée, sans affecter
les autres.

### 5.1 Familles de détection

**Pilote ↔ pilote**
Windows Update écrasant le pilote constructeur · fichiers `oemXX.inf` orphelins dans le DriverStore ·
doublons de pilotes · pilotes d'affichage virtuels tiers (Parsec, Sunshine, IddSampleDriver) ·
pilote audio HDMI/DisplayPort du GPU désynchronisé · mode test signature actif ·
cohabitation de restes AMD et NVIDIA

**Pilote ↔ services Windows**
HAGS (planification GPU accélérée par le matériel) sur pilote incompatible ·
MPO / Multi-Plane Overlay, cause majeure de scintillement et de gel sous Windows 11 ·
Fast Startup empêchant la réinitialisation propre du pilote ·
services constructeurs en doublon (NVIDIA Display Container, AMD External Events, Intel DTT) ·
Game Bar, Game DVR, Auto HDR

**Pilote ↔ applications**
Overlays qui s'accrochent au rendu (Discord, Steam, NVIDIA App, RTSS / MSI Afterburner, OBS,
Medal, Razer, EA, Ubisoft) — la première cause de plantages ·
anti-cheats noyau (Vanguard, EAC, BattlEye) · antivirus interceptant le rendu ·
injecteurs (ReShade, SpecialK, Lossless Scaling) ·
**applications s'exécutant sur le mauvais GPU** — critique en configuration Optimus

**Matériel et alimentation**
Resizable BAR / SAM mal configuré · lien PCIe dégradé (x16 tombé en x4 ou x8) ·
PCI Express Link State Power Management bridant le GPU ·
mode MUX hybride contre discret · écrans multiples à fréquences hétérogènes ·
limitation thermique · saturation VRAM et débordement vers la mémoire système

**Traces de plantage**
TDR (dépassement du délai de réponse du pilote) · Event 4101 et Event 14 (`nvlddmkm`) ·
`LiveKernelReports` DXGKRNL et WATCHDOG · minidumps imputés à `nvlddmkm.sys`,
`amdkmdag.sys`, `igdkmd64.sys` · erreurs WHEA sur le bus PCIe ·
réglages d'overclocking et de limite de puissance appliqués par des outils tiers

## 6. Moteur de remédiation — le cœur de la sûreté

Une remédiation expose :

```
Id, Title, RiskLevel, RequiresReboot, RequiresSafeMode, AffectsDisplay
Explanation   ce que fait la correction, en français
Preview()     liste exacte des changements : cible, valeur actuelle, valeur après
Apply(TransactionContext)
```

### 6.0 Deux échelles distinctes, à ne pas confondre

`Severity` qualifie un **constat** : à quel point le problème détecté nuit à la machine.
`RiskLevel` qualifie une **action** : à quel point la corriger peut mal tourner.

Les deux sont indépendantes. Un problème critique peut avoir une correction sans risque, et
inversement.

| `RiskLevel` | Nature | Garanties appliquées |
|---|---|---|
| `Faible` | Réglage applicatif, service tiers, préférence GPU par application | Journal transactionnel |
| `Moyen` | Service Windows, plan d'alimentation, paramètre d'affichage | Journal + watchdog si l'affichage est touché |
| `Élevé` | Clé registre du pilote (TDR, HAGS, MPO), configuration matérielle | Journal + point de restauration + watchdog |
| `Critique` | Suppression de pilote, purge du DriverStore, mode sans échec | Journal + point de restauration + export registre complet + reprise après redémarrage |

### 6.1 Pipeline obligatoire de toute écriture

1. **Aperçu** — l'utilisateur voit chaque clé, chaque valeur avant et après. Rien n'est caché.
2. **Confirmation explicite** — aucune écriture automatique, sans exception.
3. **Journal d'écriture anticipée** — la transaction et son rollback complet sont écrits sur disque
   dans `%ProgramData%\ForgeConflict\journal\` **avant** toute modification.
4. **Point de restauration système** si `RiskLevel >= Élevé`.
5. **Application** de la modification.
6. **Watchdog d'affichage** si `AffectsDisplay` — compte à rebours de 30 secondes après
   application : « Conserver ces changements ? ». Sans confirmation, retour automatique à l'état
   précédent. Même mécanisme que Windows lors d'un changement de résolution.
7. **Validation** de la transaction dans le journal.

### 6.2 Récupération après incident

À chaque démarrage, ForgeConflict lit le journal. S'il trouve une transaction ouverte non validée,
c'est qu'il y a eu plantage, écran noir ou redémarrage forcé pendant une opération. L'application
propose alors la restauration — et l'exécute automatiquement si la transaction touchait
l'affichage.

Une tâche planifiée déclenchée au démarrage assure ce contrôle même si l'utilisateur ne peut pas
lancer l'application. **C'est ce mécanisme qui garantit qu'on ne peut pas rester sans image.**

## 7. Désinstalleur de pilote

Machine à états persistée sur disque, conçue pour survivre aux redémarrages :

```
Idle → Prepared → SafeModeBooting → Cleaning → NormalBooting → Report
```

1. **Prepared** — export du registre concerné, inventaire des INF et services à retirer, point de
   restauration système, blocage temporaire de la recherche de pilotes par Windows Update
   (`SearchOrderConfig`, `ExcludeWUDriversInQualityUpdate`), inscription des mécanismes de reprise.
2. **SafeModeBooting** — `bcdedit /set {current} safeboot minimal` puis redémarrage.
3. **Cleaning** — en mode sans échec, lancé automatiquement :
   `pnputil /delete-driver /uninstall /force` sur les INF ciblés, suppression des services
   (`nvlddmkm`, `nvvad`, `AMD*`, `igfx*`), purge des clés registre, suppression de `C:\AMD`,
   `C:\NVIDIA`, `C:\Program Files\NVIDIA Corporation` et des dossiers ProgramData résiduels.
4. **NormalBooting** — `bcdedit /deletevalue {current} safeboot` puis redémarrage.
5. **Report** — ce qui a été retiré, ce qui subsiste, et la version de pilote recommandée.

### Filet de sécurité impératif

La sortie du mode sans échec (`bcdedit /deletevalue {current} safeboot`) est inscrite en tâche de
démarrage **avant** d'entrer en mode sans échec. Si l'étape de nettoyage échoue ou plante, la
machine ressort quand même du mode sans échec au redémarrage suivant.

Sans cette précaution, un échec de nettoyage laisse la machine bloquée en boucle de mode sans
échec. C'est le scénario le plus grave du projet et il doit être traité en premier, avec un test
dédié en machine virtuelle.

## 8. Catalogue et recommandation de pilotes

`drivers-catalog.json` embarqué dans l'exécutable, rafraîchissable depuis un point de terminaison
hébergé sur le VPS.

```
vendor, gpuFamilies[], version, branch (GameReady | Studio | WHQL | Pro),
releaseDate, whql, knownIssues[{ description, affectedConfigs, severity }],
verdict (Recommandé | Stable | À éviter), downloadUrl
```

La recommandation croise le GPU détecté, la topologie (Optimus, bureau, multi-écran), l'historique
de plantage relevé sur la machine et l'usage déclaré (jeu, création, bureautique). La sortie donne
la version conseillée **et l'explication du choix**.

Le rafraîchissement en ligne est optionnel et explicite : aucune requête réseau sans action de
l'utilisateur.

## 9. Surveillance temps réel

Un service de fond, actif seulement quand l'utilisateur l'active :

- `ManagementEventWatcher` sur la création et l'arrêt de processus — détecte le lancement d'un
  overlay conflictuel au moment où il se produit ;
- `EventLogWatcher` sur le journal System — capte un TDR à l'instant du plantage ;
- réévaluation périodique (60 s) des règles peu coûteuses uniquement.

La surveillance notifie et explique. Elle n'agit jamais d'elle-même.

## 10. Interface

Fenêtre WPF-UI Fluent 2, matériau Mica, thème clair ou sombre suivant le système.

| Vue | Contenu |
|---|---|
| Tableau de bord | Santé globale, GPU détectés, décompte des problèmes par sévérité |
| Conflits | Liste catégorisée. Chaque carte dépliable : ce qui a été trouvé, pourquoi c'est un problème, ce que fait la correction, aperçu du changement |
| Pilotes | Version installée, verdict, recommandation argumentée, historique des versions |
| Désinstalleur | Assistant pas à pas avec état de progression persistant |
| Surveillance | Flux temps réel des événements GPU |
| Historique | Toutes les actions effectuées, avec restauration possible |
| Journal | Traces techniques, exportables pour diagnostic |

Chaque conflit affiché répond systématiquement à quatre questions, dans cet ordre : *qu'est-ce qui
a été trouvé*, *pourquoi c'est un problème*, *ce que la correction va faire*, *quel est le risque*.

## 11. Stratégie anti-plantage

- `DispatcherUnhandledException`, `AppDomain.UnhandledException` et
  `TaskScheduler.UnobservedTaskException` sont tous interceptés, journalisés, et n'entraînent
  jamais la fermeture de l'application.
- Collecteurs, règles et remédiations s'exécutent chacun en isolation.
- Timeouts systématiques sur WMI, journal d'événements et appels natifs.
- Aucun blocage synchrone sur le thread d'interface.
- Journaux dans `.logs/`, format `[ISO8601] [NIVEAU] message`, rotation au-delà de 1 Mo vers
  `.logs/archive/`, jamais commités.

## 12. Tests

| Niveau | Portée |
|---|---|
| Unitaire | Règles de détection évaluées contre des `SystemSnapshot` synthétiques couvrant chaque cas |
| Unitaire | Journal transactionnel : ouverture, validation, restauration, reprise après interruption |
| Intégration | Collecteurs contre la machine réelle, vérification du comportement en cas d'échec et de timeout |
| Machine virtuelle | Cycle complet du désinstalleur, **y compris échec provoqué en mode sans échec** |
| Machine virtuelle | Watchdog d'affichage : simulation d'une perte d'image, vérification du retour automatique |

Les scénarios en machine virtuelle sont bloquants : aucune version n'est publiée sans qu'ils
passent, car ce sont exactement les situations où une erreur prive l'utilisateur de son écran.

## 13. Découpage en lots

Le périmètre est trop large pour un plan d'implémentation unique. Chaque lot est livrable et
vérifiable indépendamment, et se construit sur le précédent.

| Lot | Contenu | Livrable vérifiable |
|---|---|---|
| 1A | Socle : solution, journalisation, exécuteur isolé, garde-fou d'exceptions, coque élevée, publication portable | ✅ 06/08/2026 — exe portable vérifié en VM sans SDK .NET |
| 1B | Inventaire managé : contrats, 5 collecteurs (GPU, magasin de pilotes, réglages, services, overlays), orchestration parallèle | Un scan qui affiche l'état graphique réel et l'exporte en JSON |
| 1C | Inventaire natif : façades P/Invoke, 5 collecteurs restants (journal d'événements, dumps, matériel/PCIe, alimentation, écrans) | Inventaire complet des dix sources de la section 4 |
| 2 | Coque UI Fluent 2, tableau de bord, thème, navigation | Interface navigable affichant l'inventaire du lot 1 |
| 3 | **`Safety` : journal transactionnel, restauration au démarrage, watchdog d'affichage** | Tests de coupure : le rollback fonctionne après plantage simulé |
| 4 | Moteur de règles + familles pilote↔système et matériel | Conflits détectés et expliqués, sans correction |
| 5 | Remédiations `Faible` et `Moyen`, aperçu, confirmation, historique | Corrections appliquées et annulables |
| 6 | Familles applications, overlays, traces de plantage | Détection des conflits applicatifs et analyse post-mortem |
| 7 | Remédiations `Élevé`, points de restauration | Corrections touchant le registre pilote, validées en machine virtuelle |
| 8 | Catalogue de pilotes et recommandation | Verdict argumenté sur la version installée |
| 9 | Surveillance temps réel | Notification au lancement d'un overlay conflictuel, capture de TDR |
| 10 | Désinstalleur DDU-like | Cycle complet validé en machine virtuelle, échec provoqué inclus |

Le lot 3 précède délibérément toute écriture. **Aucune remédiation n'est implémentée avant que le
mécanisme de restauration soit écrit et prouvé.** C'est la garantie structurelle qui protège
l'affichage de la machine.

## 14. Environnement de test — machine virtuelle obligatoire

**Règle absolue : aucun test d'écriture sur la machine hôte.** Toute exécution susceptible de
modifier le registre pilote, le DriverStore, la configuration de démarrage ou les services
graphiques se déroule dans une machine virtuelle Hyper-V, restaurée depuis un point de contrôle
avant chaque campagne de test.

### VM de référence

`W11 dev` — Hyper-V génération 2, disques sur `D:\VM`, Windows 11 build 26200, 4 Go de mémoire.
Point de contrôle de départ : `forgeconflict-base` (06/08/2026).

Les points de contrôle automatiques ont été désactivés : ils se recréaient à chaque démarrage et
grossissaient sans limite, ce qui avait déjà saturé un disque par le passé. Les points de contrôle
de ce projet sont donc tous nommés et créés délibérément.

À prévoir avant les campagnes du désinstalleur : porter la mémoire à 8 Go, une désinstallation de
pilote étant à l'étroit dans 4 Go.

### Canal de vérification sans identifiants

PowerShell Direct exige un compte et un mot de passe de l'invité. Pour éviter de faire circuler un
secret, la VM est pilotée par deux mécanismes Hyper-V qui n'en demandent aucun :

- **`Copy-VMFile`** dépose l'exécutable et les scripts dans l'invité depuis l'hôte. Le service
  « Interface de services d'invité » doit être activé sur la VM — il ne l'est pas par défaut, et
  son nom contient une apostrophe typographique qui fait échouer la sélection par `-Name` ;
  passer par l'objet (`Get-VMIntegrationService | Where-Object ... | Enable-VMIntegrationService`).
- **Le canal KVP** renvoie les résultats : le script d'invité écrit sous
  `HKLM\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters`, l'hôte lit ces valeurs via
  `Msvm_KvpExchangeComponent`. Chaque valeur est plafonnée à environ 900 caractères, d'où un
  découpage en plusieurs clés `FC_*`. Sur une installation Windows neuve la clé
  `Guest\Parameters` **n'existe pas** et doit être créée : `New-ItemProperty -Force` ne force que
  la valeur, pas la clé parente, et sans cela toutes les écritures échouent.
- **Le montage hors-ligne du disque** (`tools/read-vm-report.ps1`) est le canal de secours, et le
  seul qui ne dépende de rien dans l'invité : la VM passe en état sauvegardé, son disque est monté
  en lecture seule sur l'hôte, les fichiers sont récupérés, la VM repart. C'est aussi le moyen
  d'inspecter la ruche registre de l'invité hors ligne (`reg load`), utile pour diagnostiquer
  précisément ce qu'une remédiation a écrit.

Le déclenchement dans l'invité reste manuel : l'application exige l'élévation, et l'invite UAC
s'affiche sur le bureau sécurisé, hors de portée de toute automatisation depuis l'hôte.

### Ce que la machine virtuelle ne peut pas couvrir

Une VM Hyper-V expose un adaptateur `Microsoft Hyper-V Video`, pas un pilote NVIDIA, AMD ou Intel
réel. La désinstallation effective d'un pilote constructeur, l'état du lien PCIe, Resizable BAR et
les TDR matériels ne s'y reproduisent pas.

**Contournement par instantanés capturés.** `SystemSnapshot` est sérialisable en JSON. On capture
des instantanés sur des machines physiques — opération strictement en lecture, donc sans risque par
construction, comme garanti en section 3 — puis on rejoue les règles contre ces fichiers en tests
unitaires. Cela couvre les configurations qu'aucune VM ne peut fournir, à commencer par les GPU AMD
absents du parc.

### Répartition des vérifications

| Élément à vérifier | Où |
|---|---|
| Journal transactionnel, restauration au démarrage, watchdog d'affichage | VM |
| Machine à états du désinstalleur, orchestration du mode sans échec, échec provoqué | VM exclusivement |
| Interface, navigation, résistance aux exceptions | VM |
| Collecteurs : comportement, timeouts, gestion des échecs | VM, et hôte en lecture seule |
| Règles de détection | Tests unitaires sur instantanés capturés |
| Désinstallation réelle d'un pilote constructeur | Non couvert en v1 — exigerait une machine physique dédiée |

La dernière ligne est une limite assumée : le désinstalleur sera validé sur sa logique, son
orchestration et sa capacité à se relever d'un échec, mais son effet sur un vrai pilote NVIDIA ou
AMD ne pourra être constaté qu'en usage réel, sur une machine acceptée comme sacrifiable.

## 15. Hors périmètre v1

- ARM64.
- Installation automatique du pilote recommandé — ForgeConflict conseille et fournit le lien,
  l'installation reste à l'utilisateur.
- Overclocking ou réglage de performances. ForgeConflict détecte les réglages problématiques
  appliqués par d'autres outils, il n'en applique pas.
- Signature Authenticode de l'exécutable, à traiter séparément.
