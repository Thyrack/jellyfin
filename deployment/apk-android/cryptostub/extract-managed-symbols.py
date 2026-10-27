#!/usr/bin/env python3
"""Régénère managed-symbols.txt : l'union des symboles CryptoNative_/AndroidCryptoNative_
réellement importés (P/Invoke, table ImplMap ECMA-335) par les DLL embarquées du bundle
app-server/app/src/main/assets/server/jellyfin-server.tar.xz.

Contrairement à un scan `strings`, le parsing ImplMap ne produit pas d'artefacts
(suffixes fallacieux --, TU/a/Y/3 provenant de chaînes métadonnées concaténées) et ne
peut rien omettre : toute entrée DllImport/LibraryImport y figure, et toute entrée
ImplMap est un symbole que le géré résoudra dans le .so natif.

Prérequis : pip3 install --user --break-system-packages dnfile
Usage : cd <racine du dépôt> && python3 deployment/apk-android/cryptostub/extract-managed-symbols.py
La sortie est écrite à côté de ce script ; dérouler le tar dans un répertoire temporaire
qu'aucun état du dépôt ne voit.
"""
import os
import shutil
import subprocess
import sys
import tempfile

try:
    import dnfile
except ImportError:
    sys.exit("dnfile requis : pip3 install --user --break-system-packages dnfile")

RACINE = subprocess.check_output(["git", "-C", os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "rev-parse", "--show-toplevel"]).decode().strip()
TAR = os.path.join(RACINE, "app-server", "app", "src", "main", "assets", "server", "jellyfin-server.tar.xz")
SORTIE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "managed-symbols.txt")
PREFIXES = ("CryptoNative_", "AndroidCryptoNative_")


def importer_crypto(dll):
    """Rend les noms d'entrée point (colonne ImportName) du DLL ciblant la crypto natif."""
    importes = set()
    dn = dnfile.dnPE(dll)
    tables = dn.net.mdtables
    if tables is not None and tables.ImplMap is not None:
        for ligne in tables.ImplMap:
            nom = ligne.ImportName.value if ligne.ImportName else None
            if nom and nom.startswith(PREFIXES):
                importes.add(nom)
    dn.close()
    return importes


def main():
    if not os.path.exists(TAR):
        sys.exit(f"bundle introuvable : {TAR}")
    union = set()
    with tempfile.TemporaryDirectory(prefix="jf-stub-symboles-") as tmp:
        subprocess.run(["tar", "-xJf", TAR, "-C", tmp], check=True)
        for nom in sorted(os.listdir(tmp)):
            if nom.endswith(".dll"):
                union |= importer_crypto(os.path.join(tmp, nom))
    symboles = sorted(union)
    with open(SORTIE, "w") as sortie:
        sortie.write("\n".join(symboles) + "\n")
    print(f"{SORTIE} régénéré : {len(symboles)} symboles")


if __name__ == "__main__":
    main()
