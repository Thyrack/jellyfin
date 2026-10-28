package org.jellyfin.shieldserver

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.File
import java.nio.file.Files.createTempDirectory

class ServerAssetsTest {
    private var tempDir: File? = null

    @After
    fun nettoie() {
        tempDir?.deleteRecursively()
    }

    private fun nouveauRepertoire(): File =
        createTempDirectory("jf-server-test").toFile().also { tempDir = it }

    @Test fun `les 8 noms d'assets attendus`() {
        assertEquals(
            listOf(
                "server/jellyfin-server.tar.xz", "web/jellyfin-web.tar.xz",
                "bin/ffmpeg.xz", "bin/ffprobe.xz",
                // Provider TLS G4-bis : IL linux substituées + natives BoringSSL
                "tlsprovider/System.Security.Cryptography.dll",
                "tlsprovider/System.Net.Security.dll",
                "tlsprovider/libSystem.Security.Cryptography.Native.OpenSsl.so",
                "tlsprovider/libSystem.Net.Security.Native.so"
            ),
            ServerAssets.assetFilenames())
    }
    @Test fun `les args CLI exacts du serveur`() {
        val dir = File("/tmp/x")
        assertEquals(listOf(
            "--datadir", "/tmp/x/jf-server/data",
            "--configdir", "/tmp/x/jf-server/config",
            "--cachedir", "/tmp/x/jf-server/cache",
            "--logdir", "/tmp/x/jf-server/logs",
            "--webdir", "/tmp/x/jf-server/web",
            "--ffmpeg", "/tmp/x/jf-server/bin/ffmpeg"
        ), ServerAssets.buildArgs(dir))
    }
    @Test fun `les args CLI acceptent le choix datadir externe`() {
        val dir = File("/tmp/x")
        val choix = DataDirSelector.Choix(
            datadir = File("/storage/emulated/0/JellyfinServer/data"),
            cachedir = File("/storage/emulated/0/JellyfinServer/cache"),
            externe = true)
        assertEquals(listOf(
            "--datadir", "/storage/emulated/0/JellyfinServer/data",
            "--configdir", "/tmp/x/jf-server/config",
            "--cachedir", "/storage/emulated/0/JellyfinServer/cache",
            "--logdir", "/tmp/x/jf-server/logs",
            "--webdir", "/tmp/x/jf-server/web",
            "--ffmpeg", "/tmp/x/jf-server/bin/ffmpeg"
        ), ServerAssets.buildArgs(dir, choix))
    }
    @Test fun `entrée directe quand l'exécutif jellyfin existe`() {
        val dir = nouveauRepertoire()
        File(dir, "jellyfin").apply { createNewFile(); setExecutable(true) }
        assertEquals(
            File(dir, "jellyfin").path to emptyList<String>(),
            ServerAssets.entryCommand(dir))
    }
    @Test fun `repli sur dotnet quand l'exécutif jellyfin est absent`() {
        val dir = nouveauRepertoire()
        assertEquals(
            File(dir, "dotnet").path to listOf(File(dir, "jellyfin.dll").path),
            ServerAssets.entryCommand(dir))
    }
    @Test fun `repli sur dotnet quand jellyfin n'est pas exécutable`() {
        val dir = nouveauRepertoire()
        File(dir, "jellyfin").createNewFile()
        assertEquals(
            File(dir, "dotnet").path to listOf(File(dir, "jellyfin.dll").path),
            ServerAssets.entryCommand(dir))
    }
    @Test fun `voie apphost dotnet quand le muxer et la dll sont présents`() {
        val dir = nouveauRepertoire()
        File(dir, "dotnet").apply { createNewFile(); setExecutable(true) }
        File(dir, "jellyfin.dll").createNewFile()
        assertEquals(
            File(dir, "dotnet").path to listOf(File(dir, "jellyfin.dll").path),
            ServerAssets.entryCommand(dir))
    }
    @Test fun `priorité au muxer quand les deux exécutifs coexistent`() {
        val dir = nouveauRepertoire()
        File(dir, "dotnet").apply { createNewFile(); setExecutable(true) }
        File(dir, "jellyfin.dll").createNewFile()
        File(dir, "jellyfin").apply { createNewFile(); setExecutable(true) }
        assertEquals(
            File(dir, "dotnet").path to listOf(File(dir, "jellyfin.dll").path),
            ServerAssets.entryCommand(dir))
    }
}
