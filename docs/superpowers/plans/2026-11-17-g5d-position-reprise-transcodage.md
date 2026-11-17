# Position de reprise & contrat byte-range G5-D — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corriger la perte de position en transcodage HW byte-range (G5-D) : démarrage du job à la position demandée, offsets globaux cohérents entre playlist et serving, timeline honnête, log des exceptions avalées.

**Architecture:** La playlist byte-range est régénérée à chaque fetch depuis l'état du job (`HwByteRangePlaylistBuilder.Build`). L'ancre du job (`HwByteRangeAnchorOffset/AnchorTimeSec`) est la vérité du mapping offset↔temps global. Aujourd'hui : (1) la route playlist ignore `startTimeTicks` (le job démarre toujours à 0) ; (2) le builder publie des offsets locaux alors que la route `hwbyterange` les traite comme globaux (`localOffset = demandé − ancre` → 404 après re-anchor) ; (3) les `runtimeTicks` des blocs sont locaux après re-anchor (timeline client fausse) ; (4) l'exception des réponses interrompues est loggée sans texte.

**Tech Stack:** C# / .NET 10 (net10.0), xUnit statique (style projet), ffmpeg jellyfin-ffmpeg v7.1.3 (le `-ss` entrée est déjà câblé via `state.Request.StartTimeTicks` — cf. commande re-anchor constatée).

**Spec:** Diagnostic du 2026-11-16/17 (spike live + logs Shield 192.168.50.29) — cf. conversation : `main.m3u8` ignorait la position (ancre 0@0 constatée), 404 sur tous les blocs publiés après re-anchor, playlist post-re-anchor repartie à `MEDIA-SEQUENCE:0`/`runtimeTicks=0`, WRN middleware sans texte (logs `log_20261116.log` 20:59:44.569).

## Global Constraints

- Commentaires et messages de log en français (AGENTS.md).
- Style xUnit du projet : classe statique, méthodes de test statiques nommées en français (`tests/Jellyfin.Api.Tests/HwByteRangePlaylistTests.cs`).
- Non-régression : les 7 tests builder existants et les tests Controller/API (234+185) doivent rester verts.
- Comportement inchangé quand l'ancre vaut 0 (toutes les sessions actuelles).
- 1 s = 10 000 000 ticks ; `BytesAt/SecondsAt` restent les seules conversions bitrate↔octets/temps.

## Review Focus

- Anchor ≠ 0 : le 1er bloc publié doit être servable (`demandé ≥ ancre`) — test `Ancre_NonNulle_PremierBlocServable` (Task 1).
- Couverture sans trou avec ancre : fin du bloc N = début du bloc N+1 en offsets globaux — test `Ancre_CouvertureContinue_Globale` (Task 1).
- `startTimeTicks=0` ou absent : sortie identique à l'existant (les 7 tests actuels l'épinglent).
- Seek en cours de session via fetch de range lointain : inchangé (le re-anchor existe déjà, validé au spike — 206 après `-ss`).

---

### Task 1: Builder ancré (offsets globaux + temps globaux)

**Files:**
- Modify: `Jellyfin.Api/Helpers/HwByteRangePlaylistBuilder.cs` (méthode `Build`)
- Test: `tests/Jellyfin.Api.Tests/HwByteRangePlaylistTests.cs`

**Interfaces:**
- Consumes: `job.HwByteRangeAnchorOffset` (long, octets globaux du début du fichier TS courant), `job.HwByteRangeAnchorTimeSec` (double, temps global) — déjà portés par `TranscodingJob`.
- Produces: blocs `#EXT-X-BYTERANGE:len@anchorOffset+BytesAt(i*block)` et URLs `runtimeTicks=(anchorSec+i*block)*10^7` ; `job.HwByteRangeTotalEstimateBytes` inchangé (total du film).

- [x] **Step 1: Écrire les tests qui échouent** (dans `HwByteRangePlaylistTests.cs`, style existant)

```csharp
[Fact]
public static void Ancre_NonNulle_PremierBlocServable()
{
    // Après re-anchor/position de départ : le 1er bloc publié doit être servable
    // par la route (demandé ≥ ancre) et porter le temps global.
    var job = MakeJob();
    var anchorOffset = TotalBytesForTicks(400_000_000L); // 40 s au bitrate de test
    job.HwByteRangeAnchorOffset = anchorOffset;
    job.HwByteRangeAnchorTimeSec = 40.0;
    var text = HwByteRangePlaylistBuilder.Build(job, "hash", null, 10_000_000L, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

    var firstRange = text.Split('\n').First(line => line.StartsWith("#EXT-X-BYTERANGE", StringComparison.Ordinal));
    Assert.EndsWith("@" + anchorOffset, firstRange, StringComparison.Ordinal);
    Assert.Contains("runtimeTicks=400000000&", text, StringComparison.Ordinal);
}

[Fact]
public static void Ancre_CouvertureContinue_Globale()
{
    var job = MakeJob();
    job.HwByteRangeAnchorOffset = TotalBytesForTicks(400_000_000L);
    job.HwByteRangeAnchorTimeSec = 400.0;
    var text = HwByteRangePlaylistBuilder.Build(job, "hash", null, TotalBytesForTicks(TestDurationTicks) / 4, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

    var ranges = text.Split('\n').Where(line => line.StartsWith("#EXT-X-BYTERANGE", StringComparison.Ordinal)).ToList();
    Assert.True(ranges.Count > 1);
    long lastEnd = job.HwByteRangeAnchorOffset;
    foreach (var range in ranges)
    {
        var spec = range["#EXT-X-BYTERANGE:".Length..];
        var at = spec.IndexOf('@', StringComparison.Ordinal);
        var len = long.Parse(spec[..at], System.Globalization.CultureInfo.InvariantCulture);
        var offset = long.Parse(spec[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(lastEnd, offset);
        lastEnd = offset + len;
    }
}
```

- [x] **Step 2: Vérifier l'échec** — `dotnet test tests/Jellyfin.Api.Tests --filter "FullyQualifiedName~Ancre"` → FAIL (offsets 0 publiés).

- [x] **Step 3: Implémenter dans `Build`** — lire `anchorOffset = job.HwByteRangeAnchorOffset`, `anchorSec = job.HwByteRangeAnchorTimeSec` ; bloc i : `startSec = anchorSec + i*blockSeconds` (clampé à `totalDurationSec`), `startOffset = anchorOffset + BytesAt(i*block)` (local), `endOffset = anchorOffset + Math.Min(BytesAt((i+1)*block), availableBytes)` ; `runtimeTicks = startSec*10^7` ; `blocksPublished` reste dérivé d'`availableBytes` (fenêtre locale).

- [x] **Step 4: Vérifier le vert** — `dotnet test tests/Jellyfin.Api.Tests --filter "FullyQualifiedName~HwByteRangePlaylistTests"` → les 9 tests PASS (les 7 anciens épinglent le cas ancre 0).

- [x] **Step 5: Commit** — `feat(transcode): playlist byte-range ancree - offsets et temps globaux (G5-E v20)`

### Task 2: Démarrage du job à la position demandée

**Files:**
- Modify: `Jellyfin.Api/Controllers/DynamicHlsController.cs` (`GetHwByteRangePlaylistAsync`)

**Interfaces:**
- Consumes: `state.Request.StartTimeTicks` (lié de la query `startTimeTicks` de `main.m3u8`, cf. `GetVariantHlsVideoPlaylist`, construction `StreamingRequestDto` — binding `[FromQuery] long? startTimeTicks` et affectation dans le DTO) ; `state.OutputVideoBitrate/OutputAudioBitrate`.
- Produces: job démarré avec ancre `BytesAt(startTicks)@startSec` → la 1re playlist publiée couvre la position demandée ; la commande ffmpeg porte `-ss` (déjà câblé).

- [x] **Step 1: Monter `vBitrate`/`aBitrate` avant le lock** (actuellement lus lignes ~1664-1665 après `StartFfMpeg` — la valeur vient de l'état, disponible avant).
- [x] **Step 2: Dans la branche de démarrage (playlist absente), après `job.HwByteRange = true`** : si `(state.Request.StartTimeTicks ?? 0) > 0` → `job.HwByteRangeAnchorTimeSec = startTicks / 10_000_000.0;` `job.HwByteRangeAnchorOffset = HwByteRangePlaylistBuilder.BytesAt(startTicks, vBitrate, aBitrate);` + log Debug `G5-D start ancré : {sec}s (offset {offset})`.
- [x] **Step 3: Vérifier** — `dotnet build Jellyfin.Api` OK ; tests API verts. Vérification réelle au Task 4 (curl `startTimeTicks` sur la Shield).

- [x] **Step 4: Commit** — `feat(transcode): demarrage du job byte-range a la position demandee (G5-E v20)`

### Task 3: Log de l'exception des réponses interrompues

**Files:**
- Modify: `Jellyfin.Api/Middleware/ExceptionMiddleware.cs:61`

- [x] **Step 1:** `_logger.LogWarning(ex, "The response has already started, the exception middleware will not be executed.")` — le type et la stack de l'exception (reset client vs bug serveur) deviennent visibles dans le log serveur.
- [x] **Step 2: Vérifier** — `dotnet build` OK.
- [x] **Step 3: Commit** — `feat(api): log de l'exception des reponses deja demarrees`

### Task 4: Vérification réelle sur la Shield

**Files:** aucun (déploiement).

- [x] **Step 1:** `dotnet build` solution + tests verts (suite complète).
- [x] **Step 2:** Déployer : `DRY_RUN=0 bash deployment/apk-android/collect-assets.sh` puis `cd app-server && JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64 ./gradlew :app:assembleDebug` puis `DRY_RUN=0 bash deployment/apk-android/install-server.sh`.
- [x] **Step 3:** curl `main.m3u8` avec `startTimeTicks=<position>` → la playlist publie `runtimeTicks=<position>&…` et `BYTERANGE@<ancre+offset>` ; fetch du 1er bloc → 206 (plus de 404 post-ancre).
- [x] **Step 4:** Nettoyer le job de test (DELETE /Videos/ActiveEncodings).

## Pistes écartées (et pourquoi)

- **Playlist post-re-anchor « continue »** (blocs gen-0 + discontinuity + blocs gen-1) : la route sert un seul fichier par génération ; mixer deux générations exige un servage par bloc — complexité disproportionnée vs le gain (les clients re-ancrés lisent le flux servi directement).
- **Fallback sur la position de session** quand `startTimeTicks` est absent : source faillible (sessions stalées, multi-sessions, le POST /Sessions/Playing de l'iPhone a été reset) — on reste sur le contrat standard Jellyfin.
- **Bitrate mesuré via ffprobe dans la route playlist (T4 du plan initial)** : appel subprocess dans le chemin des requêtes, échecs à gérer ; indépendant du bug de position — reporté.
- **Appariement Begin/EndRequest après re-anchor (T6)** : intriguant mais orthogonal (fuite de kill timer), à traiter séparément avec ses propres tests.
