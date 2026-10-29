# Design — Métriques host exposées par le serveur Jellyfin (CPU, RAM, thermique, sessions)

**Date** : 2026-10-29
**Statut** : approuvé par l'utilisateur (approche 1 + throttling thermique)
**Périmètre** : dépôt serveur `Thyrack/jellyfin` (fork) + packaging APK Shield. UI web non modifiée (tarball upstream `jellyfin-web v12.3` intégré au build).

## 1. Objectif

Exposer les métriques du **host** (et non du seul process serveur) à des fins d'observation sur le dashboard :

- CPU global du host en % (delta `/proc/stat`).
- RAM host : MemTotal / MemAvailable / Swap (l'évolution de la RAM host a déjà provoqué des OOM killer sur la Shield).
- Mémoire du process serveur dotnet (working set, heap GC, uptime serveur).
- Sessions actives / en transcodage (via `ISessionManager`).
- Zones thermiques SoC Shield (throttling typique ≥ 85 °C) via `/sys/class/thermal`.

Le stockage est déjà couvert par l'endpoint existant `GET /System/Info/Storage` (non modifié).

## 2. Constats de l'exploration (vérifié dans le code)

- L'UI du dashboard vit dans `jellyfin-web` (repo séparé, non cloné ici). Le web embarqué dans l'APK provient du tarball upstream `v12.3` construit par `deployment/apk-android/collect-assets.sh` (JF_WEB_TAG).
- Le bundle web v12.3 conserve le chargement des pages de configuration serveur : route `configurationpage?name=<nom>` + `GET web/ConfigurationPages` / `GET web/ConfigurationPage?name=` (vérifié dans `main.jellyfin.bundle.js` et chunk dédié). Ces pages sont servies par `Jellyfin.Api/Controllers/DashboardController.cs` et alimentées par les plugins implémentant `IHasWebPages`.
- Plugins locaux : découverts dans `<datadir>/plugins` (`BaseApplicationPaths.PluginsPath = ProgramDataPath/plugins`), manifeste `meta.json` optionnel — sans manifeste un manifeste actif est auto-créé (`Emby.Server.Implementations/Plugins/PluginManager.LoadManifest`), mais l'APK fournira un `meta.json` complet (whitelist `assemblies`, `targetAbi` aligné à la version serveur, `status: Active`).
- `/proc/stat`, `/proc/meminfo`, `/proc/uptime` restent accessibles aux process d'application Android 9-12 (whitelist procfs) ; `/sys/class/thermal` est en général lisible ; sinon dégradation gracieuse : valeur `null` + log.
- `OperatingSystem.IsAndroid()` déjà utilisé dans `Jellyfin.Server/Program.cs:184` — la cible Shield est une réalité du code, pas une hypothèse.
- Le packaging APK : `collect-assets.sh` (publish IL linux-arm64 + runtime android-arm64 fusionnés, tar.xz dans `app-server/app/src/main/assets/`), `ServerService.kt` extrait vers `filesDir/jf-server/` et réapplique `syncTls` à chaque démarrage (motif à imiter pour `syncPlugin`) ; datadir sur partition contenu si migration (`DataDirSelector`), donc `<datadir>/plugins` survit aux mises à jour d'APK.

## 3. Architecture retenue (approche 1)

```
échantillonnage (2 s, BackgroundService) ─► snapshot volatile ─► GET /System/HostMetrics ◄── page plugin (poll 3 s)
                                                                 ◄── curl / scripts de sonde
```

### 3.1 Côté noyau (ce dépôt)

| Élément | Emplacement | Rôle |
| --- | --- | --- |
| `IHostMetricsService` | `MediaBrowser.Controller/Metrics/` | Interface publique du service de métriques (`GetSnapshot()`). |
| `HostMetricsService` | `Emby.Server.Implementations/Metrics/` | `BackgroundService` singleton : timer 2 s, échantillonne procfs/sysfs + process + sessions, stocke un snapshot unique thread-safe (horodaté UTC). |
| `HostMetricsInfo` | `MediaBrowser.Model/System/` | Modèle DTO côté noyau (contrat stable, multi-projets). |
| `HostMetricsDto` | `Jellyfin.Api/Models/SystemInfoDtos/` | DTO API `record` `required` (pattern `SystemStorageDto`), mapping interne depuis `HostMetricsInfo`. |
| Route `GET /System/HostMetrics` | `Jellyfin.Api/Controllers/SystemController.cs` | **Amendement 2026-10-29 : `[Authorize]` (tout utilisateur connecté)** ; était initialement `Policies.RequiresElevation` (comme `Info/Storage`) — l'exposition CPU/RAM/températures/sessions aux visiteurs connectés de la Shield est assumée par l'admin. Retourne `HostMetricsDto`. |
| `ISystemManager.GetHostMetrics()` | `MediaBrowser.Controller/ISystemManager.cs` + `Emby.Server.Implementations/SystemManager.cs` | Délégation vers `IHostMetricsService` (cohérence avec `GetSystemStorageInfo`). |
| Enregistrement DI | `Jellyfin.Server/Startup.cs` (convention `AddHostedService` l.149-155) | `services.AddHostedService<HostMetricsService>()` (sans `IHostedService`, la boucle 2 s ne démarre jamais) + façade sur la même instance : `services.AddSingleton<IHostMetricsService>(sp => sp.GetRequiredService<HostMetricsService>())`. |

### 3.2 Échantillonnage procfs (linux/Android)

- **CPU %** : lecture `/proc/stat`, ligne `cpu  ` → `user nice system idle iowait irq softirq steal guest...` (`guest` inclus dans `user`, compteur noyau). Delta entre 2 échantillons consécutifs : `total = somme(user..steal)` ; `busy = total - idle - iowait` ; `pct = busyΔ/totalΔ*100` (0 si delta total nul). `coreCount` provient de `Environment.ProcessorCount`.
- **RAM** : `/proc/meminfo` → `MemTotal`, `MemAvailable`, `SwapTotal`, `SwapFree` (KiB). Champs absents → null.
- **Uptime / loadavg** : `/proc/uptime` (s), `/proc/loadavg` (`load1 load5 <runnable>…`).
- **Thermique** : glob `/sys/class/thermal/thermal_zone*/` → `type` (chaîne) + `temp` (millidegrés) → `celsius`. Zone illisible → ignorée (log WARN unique). Seuil d'alerte UI fixe : 85 °C.
- **Process** : `Process.GetCurrentProcess()` (`WorkingSet64`, `PrivateMemorySize64`), `GC.GetGCMemoryInfo().HeapSizeBytes`, `Environment.TickCount64` pour l'uptime process.
- **Sessions** : `ISessionManager.Sessions` → `active` = sessions dont `LastActivityDate` date de moins de 300 s (constante `ActiveSessionWindowSeconds = 300`, sémantique alignée sur le filtrage `activeWithinSeconds` existant) ; `transcoding` = `TranscodingInfo != null`. Définition simple, sans requête supplémentaire.
- Aucune accumulation : chaque lecture stocke valeur + valeur précédente (struct privée) ; pas d'historique (YAGNI).

### 3.3 Endpoint & payload

`GET /System/HostMetrics` — `200 OK` :

```json
{
  "timestamp": "2026-10-29T12:34:56.789Z",
  "cpu": { "percent": 34.5, "coreCount": 8, "load1": 1.2, "load5": 0.9 },
  "memoryTotalKiB": 2480008, "memoryAvailableKiB": 950000,
  "swapTotalKiB": 524200, "swapFreeKiB": 410000,
  "process": { "workingSetBytes": 512000000, "heapBytes": 210000000, "uptimeSeconds": 885 },
  "hostUptimeSeconds": 1958,
  "thermalZones": [ { "zone": "thermal_zone0", "type": "CPU-therm", "celsius": 57.4 } ],
  "sessions": { "active": 2, "transcoding": 1 }
}
```

- `cpu.percent` null tant que 2 échantillons ne sont pas accumulés (premiers appels après démarrage).
- Windows / `/proc` indisponible → champs null, code toujours 200.

### 3.4 Plugin d'affichage (`Jellyfin.Plugin.HostMetrics`)

- Nouveau projet dans la solution : `Jellyfin.Plugin.HostMetrics.csproj`, références : `MediaBrowser.Common` uniquement. `HostMetricsPlugin : IPlugin, IHasWebPages` (`GetPages()` retourne une `PluginPageInfo`).
- Page : HTML/JS vanilla embarqué (resource intégrée), auth évoluanble : **Amendement 2026-10-29** — priorité `window.ApiClient` du shell, repli sur la credence legacy `localStorage["jellyfin_credentials"]` (le token du shell n'était pas attaché avant le script de la page), état d'authentification affiché (ligne « État »), poll 3 s resilient.
- Contenu : jauge CPU %, barre RAM (utilisé/disponible) + swap, cartes RAM process (working set, heap GC, uptime serveur), uptime host, liste zones thermiques (rouge ≥ 85 °C), compteurs sessions actives / transcodage.
- Libellés externalisés dans un unique objet `MESSAGES` du fichier (baseline i18n fonctionnelle, un seul fichier de langue initial = fr).
- Accès garanti : `configurationpage?name=HostMetrics`. `EnableInMainMenu`, `MenuSection`, `MenuIcon` remplis pour l'intégration menu opportuniste (le listing menu dépend du web ; l'URL directe est garantie par le contrôleur DashboardController).
- Aucune configuration persistante (aucune implémentation de `IPluginServiceRegistrator`) — KISS.

### 3.5 Intégration build/packaging Shield

- `Jellyfin.sln` : projet plugin ajouté (build standard, IL pur net10.0 — pas de cible runtime spécifique).
- `collect-assets.sh` : ajout d'une étape (5) — publish du plugin → copie `HostMetrics.dll` + `meta.json` générés (version alignée serveur) dans `assets/plugin/`.
- `app-server/app/src/main/kotlin/org/jellyfin/shieldserver/ServerService.kt` : `syncPlugin(datadir)` sur motif `syncTls` — copie byte-exact des fichiers `assets/plugin/*` vers `<datadir>/plugins/HostMetrics/` à CHAQUE démarrage ; log des remplacements ; aucune suppression de fichiers tiers (répertoire dédié au plugin).
- `ServerAssets.kt` : la liste des assets emporte `plugin/HostMetrics.dll` + `plugin/meta.json` (catalogue testé par `ServerAssetsTest`).

## 4. Interfaces isolées (testabilité)

- `IHostMetricsService` : unique seam public (noyau).
- `IProcSnapshotReader` (interne `Emby.Server.Implementations/Metrics/`) : abstraction lecture fichiers (`ReadAllText(path)`), mockée en tests pour injecter des fixtures ; l'implémentation réelle lit `/proc/...`. Permet de tester parsers sans root ni device.
- Parsers purs (`ProcStatParser`, `ProcMemInfoParser`, `ThermalParser`) : fonctions statiques pures `(string) → (modèle)`, testées unitairement avec fixtures et valeurs limites.

## 5. Gestion des erreurs (fail-fast mais dégradé gracieux)

- Un fichier/zone illisible → champ null (+ WARN une seule fois par fichier, `Dictionary<string,DateTime> _lastWarnAt` ou set d'id guard).
- Parse défensif : lignes courtes, séparateurs multiples, tokens non numériques ; exception parsage interceptée → null + WARN unique.
- L'endpoint ne lève jamais : 200 toujours, éventuellement partiellement null (l'UI affiche « n/d »).
- Logging via `ILogger`, niveaux DEBUG (échantillonnage), WARN (dégradations), aucun PII.

## 6. Tests (TDD, xUnit comme le reste du repo)

- `HostMetricsServiceTests` : fixtures procfs (stat multi-lignes, meminfo dense, thermal partielle, swap absent, tokens bizarres) ; delta CPU 2 échantillons (précision ±0.x %) ; null après aucun échantillon ; pas d'exception sur entrées illisibles ; WARN unique.
- `SystemControllerTests` : contrôleur avec `IHostMetricsService` mocké → mapping DTO asserted ; policy `[Authorize]` (tout utilisateur connecté, amendement 2026-10-29) documentée (convention du repo, pas de test d'autorisation dédié).
- Test `ServerAssetsTest.kt` mis à jour pour les nouveaux assets plugin (Kotlin, JUnit existant).
- `dotnet test` sur les projets de tests concernés ; tests Kotlin `./gradlew :app:testDebugUnitTest` pour le module APK.

## 7. Hors périmètre (délibément)

- Historisation/graphes long terme, alertes push, exposition Prometheus/OpenMetrics.
- Windows (champs null, documenté) ; fork orienté Linux/Android.
- Image docker unraid (templates existants pointent l'image upstream — le plugin/API n'existe que sur le build de ce fork).
- Modification du tarball web.

## Pistes écartées (et pourquoi)

1. **Approche 2 : endpoint seul + mini-page à URL fixe servie par une route maison** — écartée par l'utilisateur : la page aurait dû être mise en favori manuellement, hors du menu dashboard ; pas d'intégration au moteur de pages Jellyfin.
2. **Approche 3 : tout dans le plugin (endpoint inclus)** — les plugins 10.9+ n'ajoutent pas de controllers facilement ; l'API générique finirait inaccessible proprement aux clients externes (curl/sondes) et le noyau y gagnerait une dépendance indirecte.
3. **Option : températures non incluses** — écartée par l'utilisateur : le throttling thermique Shield est un risque réel en transcode, lisible quasi gratuitement via `/sys/class/thermal`.
4. **Historisation en base/graphiques au fil de l'eau** — YAGNI ; le snapshot instantané couvre le besoin, l'ajout d'historique est une évolution naturelle non bloquante.
5. **Modification direct de jellyfin-web (fork web ou patch du tarball)** — écartée : le tarball web est reconstruit à chaque `collect-assets.sh` depuis upstream (JF_WEB_TAG) ; un patch web serait absorbé/supprimé au build, et le mécanisme `configurationpage` existant suffit.
6. **Échantillon CPU calculé à chaque appel HTTP** (2 lectures `/proc/stat` par requête) — abandonné : latence +100-300 ms par appel et incohérence entre lectures concurrentes ; le BackgroundService 2 s rend l'endpoint à coût nul et CPU% cohérent.
7. **Mémoire uniquement via `GC.GetGCMemoryInfo()`** — insuffisant : l'utilisateur a subi des OOM killer Android sur le host ; la RAM **host** est la valeur informative, la mémoire process en complément.
