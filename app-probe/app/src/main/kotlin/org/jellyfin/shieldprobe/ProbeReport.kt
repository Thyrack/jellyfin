package org.jellyfin.shieldprobe

/** Rendu du rapport dlopen : une ligne par bibliothèque, séparées par retour à la ligne. */
object ProbeReport {
    fun render(dlopenProbe: (String) -> String?, tags: List<String>): String =
        tags.joinToString("\n") { tag ->
            val error = dlopenProbe(tag)
            (if (error == null) "OK" else "ÉCHEC") + "\t$tag"
        }
}
