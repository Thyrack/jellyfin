package org.jellyfin.shieldserver

import java.io.File

/** Catalogue des assets embarqués et construction de la ligne de commande du serveur. */
object ServerAssets {
    fun assetFilenames(): List<String> = listOf(
        "server/jellyfin-server.tar.xz",
        "web/jellyfin-web.tar.xz",
        "bin/ffmpeg.xz",
        "bin/ffprobe.xz",
        // Provider TLS (G4-bis) : IL linux + natives BoringSSL (cf. tlsprovider/)
        // — le tar contient les IL android (pack runtime android) ; ils sont
        // appliqués par-dessus à chaque démarrage par syncTls (cf. ServerService).
        "tlsprovider/System.Security.Cryptography.dll",
        "tlsprovider/System.Net.Security.dll",
        "tlsprovider/libSystem.Security.Cryptography.Native.OpenSsl.so",
        "tlsprovider/libSystem.Net.Security.Native.so",
        // Plugin page de configuration « Métriques host » (déployé dans datadir/plugins/HostMetrics)
        "plugin/hostmetrics-plugin.tar.xz",
    )

    /**
     * Ligne d'arguments du serveur : datadir/cachedir choisis par [DataDirSelector]
     * (externe = partition contenu 82 GiB si la migration est en place — sentinelle
     * .migration-ok + marqueur jellyfin.db présents —, interne historique sinon) ;
     * config/logs/web/bin restent sous filesDir (gérés par le bundle).
     */
    fun buildArgs(filesDir: File, choix: DataDirSelector.Choix = DataDirSelector.choisir(filesDir)): List<String> = listOf(
        "--datadir", choix.datadir.path,
        "--configdir", "${filesDir}/jf-server/config",
        "--cachedir", choix.cachedir.path,
        "--logdir", "${filesDir}/jf-server/logs",
        "--webdir", "${filesDir}/jf-server/web",
        "--ffmpeg", "${filesDir}/jf-server/bin/ffmpeg",
    )

    /**
     * Choix du point d'entrée d'après le contenu réel du bundle extrait.
     * Priorité 1 : le muxer `dotnet` mergé dans le tar (plan-réplique « hôte mergé » ;
     * l'apphost `dotnet` provient du dépaquetage termux dotnet-host-10.0, ELF bionic,
     * le pack NuGet android n'embarquant aucun hôte) appliqué à `jellyfin.dll` ;
     * priorité 2 : exécutif direct `jellyfin` (apphost dédié, pack android futur).
     * À défaut, le repli historique `dotnet jellyfin.dll` est renvoyé tel quel :
     * l'échec d'exec est alors remonté par le service (notifyError).
     */
    fun entryCommand(serverDir: File): Pair<String, List<String>> {
        val dotnet = File(serverDir, "dotnet")
        val dll = File(serverDir, "jellyfin.dll")
        if (dotnet.isFile && dotnet.canExecute() && dll.isFile) return dotnet.path to listOf(dll.path)
        val direct = File(serverDir, "jellyfin")
        if (direct.isFile && direct.canExecute()) return direct.path to emptyList()
        return dotnet.path to listOf(dll.path)
    }
}
