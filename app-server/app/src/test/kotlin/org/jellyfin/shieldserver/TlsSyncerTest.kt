package org.jellyfin.shieldserver

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Tests M6-revue : comparaison diff+copie des assets TLS (classe pure). */
class TlsSyncerTest {
    private val sources = mutableMapOf<String, ByteArray>()
    private val cibles = mutableMapOf<String, ByteArray>()
    private val ecritures = mutableListOf<Pair<String, Int>>()
    private val objections = mutableListOf<String>()

    private val syncer = TlsSyncer(
        source = { nom -> sources.getValue(nom).inputStream() },
        lireCible = { nom -> cibles[nom] },
        ecrireCible = { nom, contenu -> cibles[nom] = contenu; ecritures.add(nom to contenu.size) },
    )

    private fun remplit(asset: String, contenu: String) {
        sources[asset] = contenu.toByteArray()
    }

    @Test fun `fichier cible identique provoque aucune écriture`() {
        remplit("tlsprovider/a.dll", "abc")
        cibles["a.dll"] = "abc".toByteArray()
        val remplaces = syncer.synchroniser(listOf("tlsprovider/a.dll" to "a.dll"))
        assertTrue(remplaces.isEmpty())
        assertEquals(0, ecritures.size)
    }

    @Test fun `contenu différent provoque une écriture de remplacement`() {
        remplit("tlsprovider/a.dll", "abc")
        cibles["a.dll"] = "xyz".toByteArray()
        val remplaces = syncer.synchroniser(listOf("tlsprovider/a.dll" to "a.dll"))
        assertEquals(1, remplaces.size)
        assertEquals("a.dll" to 3, ecritures.single())
        assertEquals("abc", cibles["a.dll"]!!.decodeToString())
    }

    @Test fun `fichier cible manquant est créé`() {
        remplit("tlsprovider/a.so", "binaire")
        val remplaces = syncer.synchroniser(listOf("tlsprovider/a.so" to "a.so"))
        assertEquals(1, remplaces.size)
        assertEquals("binaire", cibles["a.so"]!!.decodeToString())
    }

    @Test fun `asset absent est ignoré sans exception (fail-open)`() {
        val remplaces = syncer.synchroniser(listOf("tlsprovider/absent.dll" to "absent.dll")) { nom, _ ->
            objections.add(nom)
        }
        assertTrue(remplaces.isEmpty())
        assertTrue(cibles.isEmpty())
        assertEquals(listOf("tlsprovider/absent.dll"), objections)
    }

    @Test fun `fichier cible tronqué est écrasé`() {
        remplit("tlsprovider/a.so", "contenu long necessaire")
        cibles["a.so"] = "tronq".toByteArray()
        val remplaces = syncer.synchroniser(listOf("tlsprovider/a.so" to "a.so"))
        assertEquals(1, remplaces.size)
        assertEquals("contenu long necessaire", cibles["a.so"]!!.decodeToString())
    }

    @Test fun `décompte honnête sur plusieurs assets`() {
        remplit("tlsprovider/1.dll", "un")
        remplit("tlsprovider/2.dll", "deux")
        remplit("tlsprovider/3.so", "trois")
        cibles["1.dll"] = "un".toByteArray()
        cibles["3.so"] = "".toByteArray()
        val remplaces = syncer.synchroniser(
            listOf("tlsprovider/1.dll" to "1.dll", "tlsprovider/2.dll" to "2.dll", "tlsprovider/3.so" to "3.so"))
        assertEquals(listOf("tlsprovider/2.dll", "tlsprovider/3.so"), remplaces.map { it.asset })
        assertEquals(2, ecritures.size)
    }
}
