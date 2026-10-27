/* Parser DER autonome pour la vue X509 (M5-revue) : découplé du provider, aucun
 * global ni symbole dlopen — fonctions pures sur les octets DER. Utilisé par
 * crypto_openssl_provider.c (cache registre + i2d) et testable nativement seul.
 *
 * Motivation : X509_get_version / X509_get_X509_PUBKEY / SSL_SESSION_get0_hostname
 * sont absents du BoringSSL Android (sondes probe1..3) ; les champs nécessaires au
 * géré (version, SPKI, OID algorithme, clé publique) sont lus directement dans le
 * DER. Ragot : ne jamais libérer les pointeurs const (ils pointent dans v->der).
 */
#ifndef DER_X509_PARSER_H
#define DER_X509_PARSER_H

#include <stddef.h>

struct asn1_string_st;   /* coquille ASN1_STRING BoringSSL (définie côté provider) */

/* Vue décodée d'un certificat : pointeurs sur v->der, libérée par vue_der_liberer. */
typedef struct vue_der {
    unsigned char* der;
    int der_len;
    int version;
    const unsigned char* spki;
    int spki_len;                    /* TLV SEQUENCE complet (en-tête inclus) */
    const unsigned char* cle;        /* contenu BIT STRING sans l'octet d'inutilisé */
    int cle_len;
    const unsigned char* parametres; /* zone après l'OID de l'algid (NULL si aucune) */
    int parametres_len;
    const unsigned char* algide;     /* contenu brut de l'OID premier du SPKI */
    int algide_len;
    struct asn1_string_st* coquille_cle;  /* alloc différée, libérée au Destroy */
} vue_der_t;

/* Décodage pur : remplit v à partir du DER d'un certificat (échecs silencieux :
 * champs laissés NULL/-1, jamais d'accès hors bornes). vue_vide appliqué au départ. */
void der_parse_cert_vue(const unsigned char* der, int der_len, vue_der_t* v);

/* Libération complète vue + DER + coquille (remise à zéro). */
void vue_der_liberer(vue_der_t* v);

/* Décodage OID (contenu base-128) vers « 1.2.3 » dans buf ; retourne 0 (échec :
 * tronqué/débordement/vide) ou 1. Aucun octet lu hors [pers, pers+len[. */
int der_oid_dotted(const unsigned char* pers, int len, char* buf, size_t taille_buf);

#endif /* DER_X509_PARSER_H */
