# Patch web G4 — option MediaCodec (Android)

Patch minimal de jellyfin-web v12.3 (étiquette JF_WEB_TAG de collect-assets.sh) :
ajout de l'option mediacodec dans le formulaire de transcodage (dropdown + liste
isHwaSelected + types H264) — sinon la sauvegarde de la page Transcodage
réinitialiserait HardwareAccelerationType=mediacodec (G4) en none.

Application : gérée par collect-assets.sh (étape 2-bis) via patch -p1, échec = échec
du build. Adapté à v12.3 uniquement (recréer sur montée de tag).
