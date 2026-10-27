/* Implémentation du parser DER X509 (M5-revue) — fonctions pures, aucune
 * dépendance BoringSSL : cf. der_x509_parser.h pour le contrat. */
#include "der_x509_parser.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* Champ DER : structure parseuse minimale (contenu + taille contenu). */
struct tlv { const unsigned char* contenu; int len; };

/* En-tête DER short/long-form ; avance *pp après l'en-tête, retourne la taille
 * du contenu (−1 si forme interdite : length indéfinie ou hors bornes). */
static int der_tete(const unsigned char** pp, int restant, struct tlv* sortie)
{
    int taille = 0;
    unsigned char b1;
    struct tlv vide = { NULL, 0 };
    if (pp == NULL || *pp == NULL || restant < 2) return -1;
    if (sortie != NULL) *sortie = vide;
    b1 = (*pp)[1];
    if (b1 == 0x80) return -1;
    if (b1 < 0x80)
    {
        taille = b1;
        *pp += 2;
        restant -= 2;
    }
    else
    {
        int nb = b1 & 0x7F;
        int i;
        if (nb > 4 || restant < 2 + nb) return -1;
        for (i = 0; i < nb; i++) taille = (taille << 8) | (*pp)[2 + i];
        *pp += 2 + nb;
        restant -= 2 + nb;
    }
    if (taille > restant) return -1;
    if (sortie != NULL)
    {
        sortie->contenu = *pp;
        sortie->len = taille;
    }
    return taille;
}

/* Libération complète vue + DER + coquille (remise à zéro). */
void vue_der_liberer(vue_der_t* v)
{
    if (v == NULL) return;
    free(v->der);
    free(v->coquille_cle);
    memset(v, 0, sizeof(*v));
}

/* Vidage logique d'une vue (le tampon der reste propriété de l'appelant). */
static void vue_vide(vue_der_t* v)
{
    v->version = -1;
    v->spki = NULL; v->spki_len = 0;
    v->cle = NULL; v->cle_len = 0;
    v->parametres = NULL; v->parametres_len = 0;
    v->algide = NULL; v->algide_len = 0;
}

/* Champ [0] explicite de version ; retourne la taille consommée depuis tbs
 * (0 si v1 sans champ), −1 si le champ existe mais est illisible. */
static int lire_version(vue_der_t* v, const unsigned char* tbs, int rest)
{
    struct tlv versionCtx;
    struct tlv entier;
    const unsigned char* p = tbs;
    int taille;
    v->version = 0;
    if (rest < 2 || tbs[0] != 0xA0) return 0;
    taille = der_tete(&p, rest, &versionCtx);
    if (taille < 0) { v->version = -1; return -1; }
    taille = der_tete(&p, versionCtx.len, &entier);
    if (taille <= 0) { v->version = -1; return -1; }
    {
        int i;
        for (i = 0; i < entier.len; i++) v->version = (v->version << 8) | entier.contenu[i];
    }
    p += entier.len;
    return (int)(p - tbs);
}

/* Les 5 champs fixes entre la version et le SPKI (serial, signature, issuer,
 * validity, subject) ; retourne la position du SPKI (0x30 garanté au début du
 * reste) ou NULL si le flux n'est pas conforme. */
static const unsigned char* sauter_champs_fixes(const unsigned char* tbs, int tbs_len, int apres_version)
{
    const unsigned char* cur = tbs + apres_version;
    struct tlv champ;
    int i;
    for (i = 0; i < 5; i++)
    {
        if (der_tete(&cur, tbs_len - (int)(cur - tbs), &champ) < 0) return NULL;
        cur += champ.len;
    }
    if (tbs_len - (int)(cur - tbs) <= 1 || cur[0] != 0x30) return NULL;
    return cur;
}

/* AlgorithmIdentifier du SPKI (M5-revue) : capture l'OID de contenu brut
 * (v->algide, sans hypothèse sur la longueur d'en-tête du SEQUENCE) et la
 * zone des paramètres ; retourne les octets consommés par l'algid, ou −1. */
static int lire_spki_algide(vue_der_t* v, const unsigned char* cur, int rest)
{
    struct tlv algid;
    struct tlv oid;
    const unsigned char* debut = cur;
    if (der_tete(&cur, rest, &algid) < 0) return -1;
    if (der_tete(&cur, algid.len, &oid) < 0) return -1;
    v->algide = oid.contenu;
    v->algide_len = oid.len;
    v->parametres_len = (int)((algid.contenu + algid.len) - (oid.contenu + oid.len));
    if (v->parametres_len > 0) v->parametres = oid.contenu + oid.len;
    return (algid.contenu - debut) + algid.len;
}

/* Clé publique BIT STRING du SPKI : v->cle sans l'octet d'inutilisé. */
static void lire_spki_cle(vue_der_t* v, const unsigned char* cur, int rest)
{
    struct tlv clebit;
    if (der_tete(&cur, rest, &clebit) < 0) return;
    if (clebit.len > 0 && clebit.contenu[0] == 0x00)
    {
        v->cle = clebit.contenu + 1;
        v->cle_len = clebit.len - 1;
    }
    else
    {
        v->cle = clebit.contenu;
        v->cle_len = clebit.len > 0 ? clebit.len : 0;
    }
}

/* Corps du SPKI : algid (OID + paramètres) puis clé publique BIT STRING. */
static void lire_spki(vue_der_t* v, const unsigned char* spki_debut, int rest)
{
    const unsigned char* cur = spki_debut;
    struct tlv seqSpki;
    int consomme;
    if (der_tete(&cur, rest, &seqSpki) < 0) return;
    v->spki = spki_debut;
    v->spki_len = (int)(cur - spki_debut) + seqSpki.len;   /* TLV complet (revue : tailles d'en-tête seulement avant) */
    consomme = lire_spki_algide(v, cur, seqSpki.len);
    if (consomme < 0) return;
    lire_spki_cle(v, cur + consomme, seqSpki.len - consomme);
}

/* tbsCertificate : version, saut des champs fixes, SPKI. */
static void der_tbs_vue(vue_der_t* v, const unsigned char* tbs, int tbs_len)
{
    int apres_version = lire_version(v, tbs, tbs_len);
    const unsigned char* spki;
    if (apres_version < 0) return;
    spki = sauter_champs_fixes(tbs, tbs_len, apres_version);
    if (spki != NULL) lire_spki(v, spki, tbs_len - (int)(spki - tbs));
}

void der_parse_cert_vue(const unsigned char* der, int der_len, vue_der_t* v)
{
    struct tlv tlvCert;
    struct tlv tlvTbs;
    const unsigned char* tbs;
    if (v == NULL || der == NULL || der_len <= 0) { if (v != NULL) vue_vide(v); return; }
    vue_vide(v);
    tbs = der;
    if (der_tete(&tbs, der_len, &tlvCert) < 0) return;
    tbs = tlvCert.contenu;
    if (der_tete(&tbs, tlvCert.len, &tlvTbs) < 0) return;
    der_tbs_vue(v, tlvTbs.contenu, tlvTbs.len);
}

/* Décode une composante base-128 à l'indice *i ; avance *i derrière l'octet
 * final, retourne 0 si tronqué (bit de continuation sans octet suivant). */
static int oid_lire_composante(const unsigned char* pers, int len, int* i, unsigned long* val)
{
    unsigned long acc = 0;
    unsigned char b;
    do
    {
        if (*i >= len) return 0;
        b = pers[(*i)++];
        acc = (acc << 7) | (b & 0x7F);
    } while (b & 0x80);
    *val = acc;
    return 1;
}

/* Accumule une composante « .%lu » dans le tampon ; 0 si débordement. */
static int oid_ajout_composante(char** cur, const char* fin, unsigned long val)
{
    char* depart = *cur;
    size_t reel;
    if (*cur >= fin) return 0;
    reel = snprintf(*cur, (size_t)(fin - *cur), ".%lu", val);
    if (*cur + reel >= fin)
    {
        *cur = depart;
        return 0;
    }
    *cur += reel;
    return 1;
}

int der_oid_dotted(const unsigned char* pers, int len, char* buf, size_t taille_buf)
{
    char* cur = buf;
    char* fin = buf + taille_buf - 1;
    int premier;
    int place;
    int i = 1;
    unsigned long val;
    if (len <= 0 || len > 50 || pers == NULL || buf == NULL || taille_buf < 8) return 0;
    premier = pers[0];
    place = snprintf(cur, (size_t)(fin - cur), "%d.%d",
                     premier / 40 > 2 ? 2 : premier / 40,
                     premier / 40 > 2 ? premier - 80 : premier % 40);
    if (place < 0 || cur + place >= fin) return 0;
    cur += place;
    while (i < len)
    {
        if (!oid_lire_composante(pers, len, &i, &val)) return 0;
        if (!oid_ajout_composante(&cur, fin, val)) return 0;
    }
    return 1;
}
