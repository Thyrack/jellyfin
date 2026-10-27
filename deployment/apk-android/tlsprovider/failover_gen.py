#!/usr/bin/env python3
"""Génère crypto_failover.c : les symboles CryptoNative_* restant de
symbols-openssl.txt NON pas implémentés réellement dans crypto_openssl_provider.c
échouent proprement (retour 0/NULL). Le géré lève alors des exceptions
CryptographicException attrapables — jamais des EntryPointNotFound.

Usage : cd <répertoire> && python3 failover_gen.py
Couverture verrouillée par build-tlsprovider.sh (nm -D vs symbols-openssl.txt).
"""
import os
import re

REPERTOIRE = os.path.dirname(os.path.abspath(__file__))
LISTE_OPENSSL = os.path.join(REPERTOIRE, "symbols-openssl.txt")
IMPL = os.path.join(REPERTOIRE, "crypto_openssl_provider.c")
SORTIE = os.path.join(REPERTOIRE, "crypto_failover.c")

with open(IMPL) as f:
    corps = f.read()
impl = set(re.findall(r"PAL\s+[^;{\n]*?\b(CryptoNative_\w+)\s*\(", corps))

avec = []
sans = []
with open(LISTE_OPENSSL) as f:
    for ligne in f:
        nom = ligne.strip()
        if not nom or not nom.startswith("CryptoNative_"):
            continue
        nom_sans = nom[len("CryptoNative_"):]
        (avec if nom in impl else sans).append(nom)

with open(SORTIE, "w") as sortie:
    sortie.write("/* Échec propre généré : symboles CryptoNative_* hors périmètre du "
                 "provider (PQ/OCSP/PKCS7/PKey RSA-EC-DSA/SHA3/KEM…) cf. entête de "
                 "crypto_openssl_provider.c. Généré par failover_gen.py — NE PAS "
                 "ÉDITER ; rejouer après toute modification d'implémentation. Le "
                 "retour 0/NULL lève une CryptographicException attrapable côté "
                 "géré (jamais EntryPointNotFound). */\n\n"
                 "#include \"crypto_export.h\"\n\n")
    for nom in sans:
        sortie.write(f"PAL int32_t {nom}(void) {{ return 0; }}\n")
print(f"{len(avec)} réelles, {len(sans)} échec propre")

# liste réelle versionnée pour le verrou de build
with open(os.path.join(REPERTOIRE, "liste-reelle-implementee.txt"), "w") as reel:
    reel.write("\n".join(sorted(avec)) + "\n")
print("liste-reelle-implementee.txt régénérée")
