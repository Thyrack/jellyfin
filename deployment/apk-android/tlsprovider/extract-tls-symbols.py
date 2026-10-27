#!/usr/bin/env python3
"""Régénère les listes de symboles tlsprovider : union exacte des noms P/Invoke
(ImplMap ECMA-335, sans artefacts de chaînes concaténées) ciblant
libSystem.Security.Cryptography.Native.OpenSsl et libSystem.Net.Security.Native
dans les IL linux des DLL : System.Security.Cryptography.dll et
System.Net.Security.dll (IL des packs runtime linux-arm64 substitués à la version
android dans le bundle — cf. entête de crypto_android_stub.c pour l'historique).

La version du pack est la VERROU UNIQUE pack-version.txt (M3-revue) : même
source que build-tlsprovider.sh, pour que les symboles extraits et les IL
copiées proviennent toujours de la même version.

Usage : cd <racine du dépôt> && python3 deployment/apk-android/tlsprovider/extract-tls-symbols.py
Prérequis : pip3 install --user --break-system-packages dnfile
Sorties (répertoire du script) : symbols-openssl.txt, symbols-netsecurity.txt
"""
import os

import dnfile

REPERTOIRE = os.path.dirname(os.path.abspath(__file__))

with open(os.path.join(REPERTOIRE, "pack-version.txt")) as _version:
    TLS_PACK_VERSION = _version.read().strip()

PACK = os.path.join(
    os.environ["HOME"], ".nuget/packages/microsoft.netcore.app.runtime.linux-arm64",
    TLS_PACK_VERSION, "runtimes/linux-arm64/lib/net10.0")
SOURCES = [
    "System.Security.Cryptography.dll",
    "System.Net.Security.dll",
]
RELIB = {
    "libSystem.Security.Cryptography.Native.OpenSsl": "symbols-openssl.txt",
    "libSystem.Net.Security.Native": "symbols-netsecurity.txt",
}


def imports_cibles(dll):
    """Noms d'entrée point du DLL ciblant l'un des deux libs cibles."""
    importes = {lib: set() for lib in RELIB}
    dn = dnfile.dnPE(dll)
    tables = dn.net.mdtables
    if tables is not None and tables.ImplMap is not None:
        for ligne in tables.ImplMap:
            if not ligne.ImportName or not ligne.ImportScope:
                continue
            lib = ligne.ImportScope.row.Name.value
            nom = ligne.ImportName.value
            if lib in RELIB:
                importes[lib].add(nom)
    dn.close()
    return importes


def main():
    """Point d'entrée : collecte l'union par librairie cible et écrit les deux listes."""
    union = {lib: set() for lib in RELIB}
    for dll in SOURCES:
        pour = imports_cibles(os.path.join(PACK, dll))
        for lib in RELIB:
            union[lib] |= pour[lib]
    for lib, nom_sortie in RELIB.items():
        chemin = os.path.join(REPERTOIRE, nom_sortie)
        with open(chemin, "w") as sortie:
            sortie.write("\n".join(sorted(union[lib])) + "\n")
        print(f"{chemin} : {len(union[lib])} symboles")


if __name__ == "__main__":
    main()
