/* Provider crypto linux pour .NET sur Android sans JVM — un seul TU.
 *
 * Les IL linux du bundle (System.Security.Cryptography.dll + System.Net.Security.dll,
 * cf. extract-tls-symbols.py) P/Invoken CryptoNative_* vers le nom de bibliothèque
 * libSystem.Security.Cryptography.Native.OpenSsl (constante Libraries.CryptoNative
 * du géré 10.0) — en remplacement du provider AndroidCrypto (JNI/JVM, cf. historique
 * stub v2 certifié). Le .so officiel linux ne peut pas tourner sur bionic : glibc.
 *
 * Architecture (BoringSSL système /system/lib64, dlopen — sondes probe1..3 OK) :
 *   - TLS client, BIO mémoire, chaîne X509, digest/HMAC/PBKDF2/RNG, AES/DES3 :
 *     implémentation réelle ; signatures miroir des pal_*.c/h de dotnet/runtime
 *     release/10.0 (pal_ssl.h, pal_x509.h, pal_bio.h, pal_evp*.h, pal_err.h,
 *     openssl.h) — vérifiées ligne à ligne pour caching/création de session ;
 *   - ex_data BoringSSL absente (SSL/CTX) : registre local clé→valeur ;
 *     SSL_SESSION : struct d'état dédiée (C1-revue : hostname strdup et
 *     appData sur des champs distincts, sentinelle supprimée) — les
 *     libérations passent obligatoirement par nos X*Destroy / SslSessionFree
 *     (contrat SafeHandle géré), donc aucune fuite ;
 *   - resumption TLS (M1-revue) : CryptoNative_SslCtxSetCaching installe les
 *     callbacks gérés (NewSessionCallback/RemoveSessionCallback) via les
 *     symboles dlopen'és et passe le mode tel quel au miroir pal (BOTH) —
 *     le cache reste celui du géré (SslSessionsCache), pas un cache interne
 *     BoringSSL ad hoc ;
 *   - absences du BoringSSL Android et adaptations (sondes probe1..3) :
 *     X509_get_version/X509_get_X509_PUBKEY → lecture DER directe (parser
 *     pur dans der_x509_parser.c) ;
 *     SSL_get_sigalgs → table par défaut codée ; SSL_CIPHER_find → parcours
 *     SSL_get_ciphers ; set_ciphersuites → ignoré (suites TLS 1.3 par défaut) ;
 *     SHA3/SHAKE/CCM/RC2_ecb/chacha_EVP → NULL (échec propre géré) ;
 *   - reste de l'union (PQ/OCSP/PKCS7/PKey RSA-EC-DSA… hors périmètre
 *     TMDB/OMDb/plugin-repo) : échec propre généré dans crypto_failover.c —
 *     le géré lève des CryptographicException attrapables, jamais de segfault ;
 *   - couverture totale verrouillée au build : nm -D <=> symbols-openssl.txt.
 *
 * ABI : pointeurs=intptr géré ; size_t BoringSSL via tampons internes (pal).
 * Build : build-tlsprovider.sh (NDK clang, -fvisibility=hidden, Android 26).
 */
#include <dirent.h>
#include <dlfcn.h>
#include <errno.h>
#include <fcntl.h>
#include <pthread.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <stdint.h>
#include <stdio.h>

#include "der_x509_parser.h"

#define PAL __attribute__((__visibility__("default")))

/* ---------- Types BoringSSL opaques ---------- */
typedef struct bio_st BIO;
typedef struct bio_method_st BIO_METHOD;
typedef struct evp_md_st EVP_MD;
typedef struct evp_md_ctx_st EVP_MD_CTX;
typedef struct evp_cipher_st EVP_CIPHER;
typedef struct evp_cipher_ctx_st EVP_CIPHER_CTX;
typedef struct hmac_ctx_st HMAC_CTX;
typedef struct bignum_st BIGNUM;
/* ASN1_STRING : layout public BoringSSL requis pour la coquille (GetX509PublicKeyBytes). */
struct asn1_string_st {
    unsigned char* data;
    int length;
    int type;
    unsigned long flags;
};
typedef struct asn1_string_st ASN1_STRING;
typedef struct asn1_string_st ASN1_OCTET_STRING;
typedef struct asn1_object_st ASN1_OBJECT;
typedef struct ssl_st SSL;
typedef struct ssl_ctx_st SSL_CTX;
typedef struct ssl_method_st SSL_METHOD;
typedef struct ssl_session_st SSL_SESSION;
typedef struct ssl_cipher_st SSL_CIPHER;
typedef struct x509_st X509;
typedef struct x509_crl_st X509_CRL;
typedef struct X509_verify_param_st X509_VERIFY_PARAM;
typedef struct x509_store_st X509_STORE;
typedef struct x509_store_ctx_st X509_STORE_CTX;
typedef struct x509_pubkey_st X509_PUBKEY;
typedef struct x509_name_st X509_NAME;
typedef struct x509_name_entry_st X509_NAME_ENTRY;
typedef struct x509_extension_st X509_EXTENSION;
typedef struct evp_pkey_st EVP_PKEY;
typedef struct stack_st STACK;
typedef int (*X509StoreVerifyCallback)(int, X509_STORE_CTX*);  /* pal : fnptr géré */


/* ---------- Constantes contrat (pal release/10.0) ---------- */
#define PAL_ECHEC 0
#define PAL_SUCCES 1
#define MD_TAILLE_MAX 64
#define SSL_ERROR_NONE 0
#define SSL_ERROR_SSL 1
#define SSL_ERROR_WANT_READ 2
#define SSL_ERROR_WANT_WRITE 3
#define SSL_ERROR_WANT_X509_LOOKUP 4
#define SSL_ERROR_SYSCALL 5
#define SSL_ERROR_ZERO_RETURN 6
#define POLICE_REQUIRE 0
#define POLICE_ALLOW_NO_ENC 1
#define POLICE_NO_ENC 2
#define VERSION_SSL3_0 0x0300
#define VERSION_TLS1_0 0x0301
#define VERSION_TLS1_1 0x0302
#define VERSION_TLS1_2 0x0303
#define VERSION_TLS1_3 0x0304
#define CTRL_GCM_SET_IVLEN 9
#define CTRL_GCM_GET_TAG 16
#define CTRL_GCM_SET_TAG 17
#define NID_COMMON_NAME 13
#define NID_SUBJECT_ALT_NAME 85
#define X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS 4
#define BIO_C_GET_MEM_DATA 3
#define BIO_CTRL_PENDING 10
#define BIO_C_FILE_SEEK 132
#define BIO_C_FILE_TELL 133

/* Types contrat SSL (pal_ssl.h) */
typedef enum {
    PAL_SSL_NONE = 0, PAL_SSL_SSL2 = 12, PAL_SSL_SSL3 = 48, PAL_SSL_TLS = 192,
    PAL_SSL_TLS11 = 768, PAL_SSL_TLS12 = 3072, PAL_SSL_TLS13 = 12288
} SslProtocols_e;
typedef enum { POLICE_ENC_REQUIRE = 0, POLICE_ENC_ALLOW_NO = 1, POLICE_ENC_NO = 2 } EncryptionPolicy_e;
typedef int32_t (*SslCtxNewSessionCallback)(SSL*, SSL_SESSION*);
typedef void (*SslCtxRemoveSessionCallback)(SSL_CTX*, SSL_SESSION*);

/* X509RevocationFlag (pal_x509.h) */
typedef enum {
    EndCertificateOnly = 0,
    EntireChain = 1,
    ExcludeRoot = 2
} X509RevocationFlag;

/* ---------- Chargement paresseux ---------- */
static void* g_libcrypto;
static void* g_libssl;
static int g_indisponible;
static pthread_once_t g_une_fois = PTHREAD_ONCE_INIT;

static void charger(void)
{
    g_libcrypto = dlopen("/system/lib64/libcrypto.so", RTLD_NOW | RTLD_LOCAL);
    if (g_libcrypto == NULL) { g_indisponible = 1; return; }
    g_libssl = dlopen("/system/lib64/libssl.so", RTLD_NOW | RTLD_LOCAL);
    if (g_libssl == NULL) { g_indisponible = 1; return; }
}

static pthread_once_t g_resolu_une_fois = PTHREAD_ONCE_INIT;
static void resoudre(void);   /* définition plus bas */

/* Résolution de la table (une fois). */
static void resolu_pret(void)
{
    if (!g_indisponible) resoudre();
}

static int pret(void)
{
    pthread_once(&g_une_fois, charger);
    pthread_once(&g_resolu_une_fois, resolu_pret);
    return !g_indisponible;
}

static void* symc(const char* nom)
{
    void* p;
    if (g_indisponible) return NULL;
    p = dlsym(g_libcrypto, nom);
    if (p == NULL) p = dlsym(g_libssl, nom);
    return p;
}

/* ---------- Table des primitives BoringSSL ---------- */
static struct {
    const BIO_METHOD* (*bio_s_mem)(void); BIO* (*bio_new)(const BIO_METHOD*);
    int (*bio_free)(BIO*); long (*bio_ctrl)(BIO*, int, long, void*);
    int (*bio_gets)(BIO*, char*, int); int (*bio_read)(BIO*, void*, int);
    int (*bio_write)(BIO*, const void*, int); BIO* (*bio_new_file)(const char*, const char*);
    int (*bio_up_ref)(BIO*); int (*rand_bytes)(unsigned char*, int);
    const EVP_MD* (*md5)(void); const EVP_MD* (*sha1)(void);
    const EVP_MD* (*sha256)(void); const EVP_MD* (*sha384)(void);
    const EVP_MD* (*sha512)(void);
    EVP_MD* (*sha3_256)(void); EVP_MD* (*sha3_384)(void); EVP_MD* (*sha3_512)(void);
    EVP_MD* (*shake128)(void); EVP_MD* (*shake256)(void);
    size_t (*md_size)(const EVP_MD*);
    EVP_MD_CTX* (*md_ctx_new)(void); void (*md_ctx_free)(EVP_MD_CTX*);
    int (*md_ctx_copy_ex)(EVP_MD_CTX*, const EVP_MD_CTX*);
    int (*digest_init_ex)(EVP_MD_CTX*, const EVP_MD*, void*);
    int (*digest_update)(EVP_MD_CTX*, const void*, size_t);
    int (*digest_final_ex)(EVP_MD_CTX*, unsigned char*, size_t*);
    HMAC_CTX* (*hmac_new)(void); void (*hmac_free)(HMAC_CTX*);
    int (*hmac_init_ex)(HMAC_CTX*, const void*, int, const EVP_MD*, void*);
    void (*hmac_update)(HMAC_CTX*, const unsigned char*, size_t);
    int (*hmac_final)(HMAC_CTX*, unsigned char*, size_t*);
    HMAC_CTX* (*hmac_copy)(HMAC_CTX*);
    int (*pbkdf2)(const char*, size_t, const unsigned char*, size_t, unsigned long, const EVP_MD*, size_t, unsigned char*);
    void (*err_clear)(void); uint64_t (*err_peek)(void); uint64_t (*err_peek_last)(void);
    const char* (*err_reason_string)(uint64_t); void (*err_error_string_n)(uint64_t, char*, size_t);
    ASN1_OBJECT* (*obj_txt2obj)(const char*, int);
    int (*obj_obj2txt)(char*, int, const ASN1_OBJECT*, int);
    int (*obj_txt2nid)(const char*); ASN1_OBJECT* (*obj_nid2obj)(int);
    int (*obj_obj2nid)(const ASN1_OBJECT*); void (*asn1_object_free)(ASN1_OBJECT*);
    ASN1_OCTET_STRING* (*octet_new)(void);
    int (*octet_set)(ASN1_OCTET_STRING*, const unsigned char*, int);
    void (*octet_free)(ASN1_OCTET_STRING*);
    int (*string_length)(const ASN1_STRING*); const unsigned char* (*string_data0)(const ASN1_STRING*);
    int (*string_print_ex)(BIO*, const ASN1_STRING*, unsigned long);
    int (*i2d_asn1_integer)(const ASN1_STRING*, unsigned char**);
    int (*asn1_integer_get)(const ASN1_STRING*);
    void (*bn_clear_free)(BIGNUM*); int (*bn_bn2bin)(const BIGNUM*, unsigned char*);
    BIGNUM* (*bn_bin2bn)(const unsigned char*, int, BIGNUM*); int (*bn_num_bits)(const BIGNUM*);
    X509* (*d2i_x509)(X509**, const unsigned char**, long);
    int (*i2d_x509)(X509*, unsigned char**);
    void (*x509_free)(X509*); int (*x509_up_ref)(X509*); X509* (*x509_dup)(X509*);
    X509* (*pem_read_bio_X509)(BIO*, X509**, void*, void*);
    X509* (*pem_read_bio_X509_aux)(BIO*, X509**, void*, void*);
    int (*x509_digest)(X509*, const EVP_MD*, unsigned char*, unsigned int*);
    void* (*x509_get0_notbefore)(X509*); void* (*x509_get0_notafter)(X509*);
    EVP_PKEY* (*x509_get_pubkey)(X509*);
    void* (*x509_get0_tbs_sigalg)(X509*); ASN1_STRING* (*x509_get_serial)(X509*);
    X509_NAME* (*x509_get_issuer)(X509*); X509_NAME* (*x509_get_subject)(X509*);
    X509_NAME_ENTRY* (*x509_name_entry)(X509_NAME*, int);
    int (*x509_check_ip)(X509*, const unsigned char*, size_t, unsigned int);
    unsigned long (*x509_issuer_name_hash)(X509*); int (*x509_check_purpose)(X509*, int, int);
    int (*i2d_x509_name)(X509_NAME*, unsigned char**);
    int (*name_entry_count)(const X509_NAME*); int (*name_index_by_nid)(const X509_NAME*, int, int);
    void* (*name_entry_object)(const X509_NAME_ENTRY*); ASN1_STRING* (*name_entry_data)(const X509_NAME_ENTRY*);
    int (*x509_get_ext_count)(const X509*); int (*x509_get_ext_by_nid)(X509*, int, int); X509_EXTENSION* (*x509_get_ext)(const X509*, int);
    void* (*ext_get_object)(X509_EXTENSION*); ASN1_OCTET_STRING* (*ext_get_data)(X509_EXTENSION*);
    int (*ext_get_critical)(X509_EXTENSION*);
    void* (*x509_get_ext_d2i)(X509*, int, int*, int*);
    void (*general_names_free)(void*);
    int (*x509_check_host)(X509*, const char*, size_t, unsigned int, char**);
    unsigned char* (*i2d_X509_PUBKEY)(X509_PUBKEY*, unsigned char**);
    X509* (*d2i_x509_bio)(BIO*, X509**);
    X509_CRL* (*d2i_x509_crl)(X509_CRL**, const unsigned char**, long);
    void (*x509_crl_free)(X509_CRL*);
    X509_CRL* (*pem_read_bio_X509_CRL)(BIO*, X509_CRL**, void*, void*);
    int (*pem_write_bio_X509_CRL)(BIO*, X509_CRL*);
    STACK* (*sk_new_null)(void); int (*sk_push)(STACK*, void*); void* (*sk_pop)(STACK*);
    int (*sk_num)(const STACK*); void* (*sk_value)(const STACK*, int);
    void (*sk_free)(STACK*); void (*sk_pop_free)(STACK*, void (*)(void*));
    X509_STORE* (*store_new)(void); void (*store_free)(X509_STORE*);
    int (*store_add_cert)(X509_STORE*, X509*); int (*store_add_crl)(X509_STORE*, X509_CRL*);
    void (*store_set_flags)(X509_STORE*, unsigned long);
    X509_VERIFY_PARAM* (*store_get0_param)(X509_STORE*);
    void (*vp_set_time)(X509_VERIFY_PARAM*, long);
    X509_STORE_CTX* (*store_ctx_new)(void); void (*store_ctx_free)(X509_STORE_CTX*);
    int (*store_ctx_init)(X509_STORE_CTX*, X509_STORE*, X509*, STACK*);
    int (*x509_verify_cert)(X509_STORE_CTX*);
    STACK* (*store_ctx_get1_chain)(X509_STORE_CTX*);
    STACK* (*store_ctx_get0_untrusted)(X509_STORE_CTX*);
    void* (*store_ctx_get0_cert)(X509_STORE_CTX*);
    void* (*store_ctx_get_current_cert)(X509_STORE_CTX*);
    int (*store_ctx_get_error)(X509_STORE_CTX*); int (*store_ctx_get_error_depth)(X509_STORE_CTX*);
    const char* (*x509_verify_cert_error_string)(long);
    void (*store_ctx_set_verify_cb)(X509_STORE_CTX*, X509StoreVerifyCallback);
    EVP_CIPHER_CTX* (*cipher_ctx_new)(void); void (*cipher_ctx_free)(EVP_CIPHER_CTX*);
    int (*cipher_ctx_reset)(EVP_CIPHER_CTX*); int (*cipher_ctx_ctrl)(EVP_CIPHER_CTX*, int, int, void*);
    int (*cipher_init_ex)(EVP_CIPHER_CTX*, const EVP_CIPHER*, void*, const unsigned char*, const unsigned char*, int);
    int (*cipher_update)(EVP_CIPHER_CTX*, unsigned char*, int*, const unsigned char*, int);
    int (*cipher_final_ex)(EVP_CIPHER_CTX*, unsigned char*, int*);
    int (*cipher_ctx_set_padding)(EVP_CIPHER_CTX*, int);
    const EVP_CIPHER* (*evp_aes_128_ecb)(void); const EVP_CIPHER* (*evp_aes_128_cbc)(void);
    const EVP_CIPHER* (*evp_aes_128_gcm)(void); const EVP_CIPHER* (*evp_aes_192_ecb)(void);
    const EVP_CIPHER* (*evp_aes_192_cbc)(void); const EVP_CIPHER* (*evp_aes_192_gcm)(void);
    const EVP_CIPHER* (*evp_aes_256_ecb)(void); const EVP_CIPHER* (*evp_aes_256_cbc)(void);
    const EVP_CIPHER* (*evp_aes_256_gcm)(void); const EVP_CIPHER* (*evp_des_ecb)(void);
    const EVP_CIPHER* (*evp_des_cbc)(void); const EVP_CIPHER* (*evp_des_ede3_ecb)(void);
    const EVP_CIPHER* (*evp_des_ede3_cbc)(void); const EVP_CIPHER* (*evp_rc2_cbc)(void);
    int (*ssl_is_init_finished)(const SSL*);
    int (*ssl_renegotiate)(SSL*);
    const char* (*ssl_get_servername)(const SSL*, int);
    int (*ssl_ctx_set_min_proto_version)(SSL_CTX*, unsigned int);
    int (*ssl_ctx_set_max_proto_version)(SSL_CTX*, unsigned int);
    const SSL_METHOD* (*tls_client_method)(void); const SSL_METHOD* (*tls_method)(void);
    SSL_CTX* (*ssl_ctx_new)(const SSL_METHOD*); void (*ssl_ctx_free)(SSL_CTX*);
    SSL* (*ssl_new)(SSL_CTX*); void (*ssl_free)(SSL*);
    void (*ssl_set_connect_state)(SSL*); void (*ssl_set_accept_state)(SSL*);
    const char* (*ssl_get_version)(SSL*);
    int (*ssl_write)(SSL*, const void*, int); int (*ssl_read)(SSL*, void*, int);
    int (*ssl_peek)(SSL*, void*, int); int (*ssl_get_error)(SSL*, int);
    int (*ssl_shutdown)(SSL*); void (*ssl_set_bio)(SSL*, BIO*, BIO*);
    int (*ssl_do_handshake)(SSL*);
    X509* (*ssl_get_peer_certificate)(SSL*); X509* (*ssl_get_certificate)(SSL*);
    STACK* (*ssl_get_peer_cert_chain)(SSL*); void (*ssl_set_verify)(SSL*, int, X509StoreVerifyCallback);
    int (*ssl_use_certificate)(SSL*, X509*); int (*ssl_use_private_key)(SSL*, EVP_PKEY*);
    int (*ssl_ctx_use_certificate)(SSL_CTX*, X509*); int (*ssl_ctx_use_private_key)(SSL_CTX*, EVP_PKEY*);
    int (*ssl_ctx_check_private_key)(SSL_CTX*); STACK* (*ssl_get_client_CA_list)(SSL*);
    const SSL_CIPHER* (*ssl_get_current_cipher)(SSL*);
    unsigned long (*cipher_get_id)(const SSL_CIPHER*); const char* (*cipher_get_name)(const SSL_CIPHER*);
    const char* (*cipher_get_version)(const SSL_CIPHER*);
    const SSL_CIPHER* (*cipher_find)(const SSL*, const unsigned char*);
    int (*ssl_get_finished)(SSL*, void*, size_t); int (*ssl_get_peer_finished)(SSL*, void*, size_t);
    int (*ssl_session_reused)(SSL*); SSL_SESSION* (*ssl_get_session)(SSL*);
    int (*ssl_set_session)(SSL*, SSL_SESSION*); void (*ssl_session_free)(SSL_SESSION*);
    int (*ssl_session_up_ref)(SSL_SESSION*); int (*ssl_renegotiate_pending)(SSL*);
    int (*ssl_set_tlsext_host_name)(SSL*, const char*);
    int (*ssl_set_alpn_protos)(SSL*, const unsigned char*, unsigned int);
    void (*ssl_get0_alpn_selected)(SSL*, const unsigned char**, unsigned int*);
    int (*ssl_set_tlsext_status_type)(SSL*, int);
    int (*ssl_ctx_set_cipher_list)(SSL_CTX*, const char*);
    int (*ssl_set_cipher_list)(SSL*, const char*);
    int (*ssl_ctx_set_ciphersuites)(SSL_CTX*, const char*);
    int (*ssl_set_ciphersuites)(SSL*, const char*);
    int (*ssl_ctx_set_session_cache_mode)(SSL_CTX*, int);
    long (*ssl_ctx_sess_set_cache_size)(SSL_CTX*, long);
    void (*ssl_ctx_sess_set_new_cb)(SSL_CTX*, void*);
    void (*ssl_ctx_sess_set_remove_cb)(SSL_CTX*, void*);
    int (*ssl_ctx_set_session_id_context)(SSL_CTX*, const unsigned char*, unsigned int);
    void (*ssl_ctx_remove_session)(SSL_CTX*, SSL_SESSION*);
    STACK* (*ssl_get_ciphers)(const SSL*);
    int (*ssl_set1_sigalgs_list)(SSL*, const char*);
} bssl;

#define EST(champ, sym) \
    do { if (bssl.champ == NULL) bssl.champ = (__typeof__(bssl.champ))symc(#sym); \
         if (bssl.champ == NULL) g_indisponible = 1; } while (0)
#define OPT(champ, sym) \
    do { if (bssl.champ == NULL) bssl.champ = (__typeof__(bssl.champ))symc(#sym); } while (0)

/* Découpage M5-revue : une miette par famille, chaque fonction ≤ 30 lignes ;
 * l'ordre global de résolution est conservé (diagnostic déterministe). */
static void resolu_bio_rng(void)
{
    EST(bio_s_mem, BIO_s_mem); EST(bio_new, BIO_new); EST(bio_free, BIO_free); EST(bio_ctrl, BIO_ctrl); EST(bio_gets, BIO_gets);
    EST(bio_read, BIO_read); EST(bio_write, BIO_write); EST(bio_new_file, BIO_new_file); EST(bio_up_ref, BIO_up_ref);
    EST(rand_bytes, RAND_bytes);
}

static void resolu_digest_hmac(void)
{
    EST(md5, EVP_md5); EST(sha1, EVP_sha1); EST(sha256, EVP_sha256); EST(sha384, EVP_sha384); EST(sha512, EVP_sha512); EST(md_size, EVP_MD_size);
    EST(md_ctx_new, EVP_MD_CTX_new); EST(md_ctx_free, EVP_MD_CTX_free); EST(md_ctx_copy_ex, EVP_MD_CTX_copy_ex);
    EST(digest_init_ex, EVP_DigestInit_ex); EST(digest_update, EVP_DigestUpdate); EST(digest_final_ex, EVP_DigestFinal_ex);
    EST(hmac_new, HMAC_CTX_new); EST(hmac_free, HMAC_CTX_free); EST(hmac_init_ex, HMAC_Init_ex); EST(hmac_update, HMAC_Update);
    EST(hmac_final, HMAC_Final); EST(hmac_copy, HMAC_CTX_copy); EST(pbkdf2, PKCS5_PBKDF2_HMAC);
}

static void resolu_err_asn1_bignum(void)
{
    EST(err_clear, ERR_clear_error); EST(err_peek, ERR_peek_error); EST(err_peek_last, ERR_peek_last_error);
    EST(err_reason_string, ERR_reason_error_string); EST(err_error_string_n, ERR_error_string_n);
    EST(obj_txt2obj, OBJ_txt2obj); EST(obj_obj2txt, OBJ_obj2txt); EST(obj_txt2nid, OBJ_txt2nid); EST(obj_nid2obj, OBJ_nid2obj);
    EST(obj_obj2nid, OBJ_obj2nid); EST(asn1_object_free, ASN1_OBJECT_free);
    EST(octet_new, ASN1_OCTET_STRING_new); EST(octet_set, ASN1_OCTET_STRING_set); EST(octet_free, ASN1_OCTET_STRING_free);
    EST(string_length, ASN1_STRING_length); EST(string_data0, ASN1_STRING_get0_data); EST(string_print_ex, ASN1_STRING_print_ex);
    EST(i2d_asn1_integer, i2d_ASN1_INTEGER); EST(asn1_integer_get, ASN1_INTEGER_get);
    EST(bn_clear_free, BN_clear_free); EST(bn_bn2bin, BN_bn2bin); EST(bn_bin2bn, BN_bin2bn); EST(bn_num_bits, BN_num_bits);
}

static void resolu_x509_cert(void)
{
    EST(d2i_x509, d2i_X509); EST(i2d_x509, i2d_X509); EST(x509_free, X509_free); EST(x509_up_ref, X509_up_ref); EST(x509_dup, X509_dup);
    EST(pem_read_bio_X509, PEM_read_bio_X509); EST(pem_read_bio_X509_aux, PEM_read_bio_X509_AUX); EST(x509_digest, X509_digest);
    EST(x509_get0_notbefore, X509_get0_notBefore); EST(x509_get0_notafter, X509_get0_notAfter); EST(x509_get_pubkey, X509_get_pubkey);
    EST(x509_get0_tbs_sigalg, X509_get0_tbs_sigalg); EST(x509_get_serial, X509_get_serialNumber);
    EST(x509_get_issuer, X509_get_issuer_name); EST(x509_get_subject, X509_get_subject_name); EST(x509_issuer_name_hash, X509_issuer_name_hash);
    EST(x509_check_purpose, X509_check_purpose);
    OPT(i2d_x509_name, i2d_X509_NAME);
    EST(name_entry_count, X509_NAME_entry_count); EST(name_index_by_nid, X509_NAME_get_index_by_NID);
    EST(name_entry_object, X509_NAME_ENTRY_get_object); EST(name_entry_data, X509_NAME_ENTRY_get_data);
    EST(x509_get_ext_count, X509_get_ext_count); EST(x509_get_ext_by_nid, X509_get_ext_by_NID); EST(x509_get_ext, X509_get_ext); EST(ext_get_object, X509_EXTENSION_get_object);
    EST(ext_get_data, X509_EXTENSION_get_data); EST(ext_get_critical, X509_EXTENSION_get_critical); EST(x509_get_ext_d2i, X509_get_ext_d2i);
    EST(general_names_free, GENERAL_NAMES_free); EST(x509_check_host, X509_check_host); EST(i2d_X509_PUBKEY, i2d_X509_PUBKEY);
    EST(d2i_x509_bio, d2i_X509_bio); EST(d2i_x509_crl, d2i_X509_CRL); EST(x509_crl_free, X509_CRL_free);
    EST(pem_read_bio_X509_CRL, PEM_read_bio_X509_CRL); EST(pem_write_bio_X509_CRL, PEM_write_bio_X509_CRL);
}

static void resolu_piles_stores(void)
{
    EST(sk_new_null, sk_new_null); EST(sk_push, sk_push); EST(sk_pop, sk_pop); EST(sk_num, sk_num);
    EST(sk_value, sk_value); EST(sk_free, sk_free); EST(sk_pop_free, sk_pop_free);
    EST(store_new, X509_STORE_new); EST(store_free, X509_STORE_free); EST(store_add_cert, X509_STORE_add_cert); EST(store_add_crl, X509_STORE_add_crl);
    EST(store_set_flags, X509_STORE_set_flags); EST(store_get0_param, X509_STORE_get0_param); EST(vp_set_time, X509_VERIFY_PARAM_set_time);
    EST(store_ctx_new, X509_STORE_CTX_new); EST(store_ctx_free, X509_STORE_CTX_free); EST(store_ctx_init, X509_STORE_CTX_init);
    EST(x509_verify_cert, X509_verify_cert); EST(store_ctx_get1_chain, X509_STORE_CTX_get1_chain);
    EST(store_ctx_get0_untrusted, X509_STORE_CTX_get0_untrusted); EST(store_ctx_get0_cert, X509_STORE_CTX_get0_cert);
    EST(store_ctx_get_current_cert, X509_STORE_CTX_get_current_cert); EST(store_ctx_get_error, X509_STORE_CTX_get_error);
    EST(store_ctx_get_error_depth, X509_STORE_CTX_get_error_depth); EST(x509_verify_cert_error_string, X509_verify_cert_error_string);
    EST(store_ctx_set_verify_cb, X509_STORE_CTX_set_verify_cb);
}

static void resolu_chiffreurs(void)
{
    EST(cipher_ctx_new, EVP_CIPHER_CTX_new); EST(cipher_ctx_free, EVP_CIPHER_CTX_free); EST(cipher_ctx_reset, EVP_CIPHER_CTX_reset);
    EST(cipher_ctx_ctrl, EVP_CIPHER_CTX_ctrl); EST(cipher_init_ex, EVP_CipherInit_ex); EST(cipher_update, EVP_CipherUpdate);
    EST(cipher_final_ex, EVP_CipherFinal_ex); EST(cipher_ctx_set_padding, EVP_CIPHER_CTX_set_padding);
    EST(evp_aes_128_ecb, EVP_aes_128_ecb); EST(evp_aes_128_cbc, EVP_aes_128_cbc); EST(evp_aes_128_gcm, EVP_aes_128_gcm);
    EST(evp_aes_192_ecb, EVP_aes_192_ecb); EST(evp_aes_192_cbc, EVP_aes_192_cbc); EST(evp_aes_192_gcm, EVP_aes_192_gcm);
    EST(evp_aes_256_ecb, EVP_aes_256_ecb); EST(evp_aes_256_cbc, EVP_aes_256_cbc); EST(evp_aes_256_gcm, EVP_aes_256_gcm);
    EST(evp_des_ecb, EVP_des_ecb); EST(evp_des_cbc, EVP_des_cbc); EST(evp_des_ede3_ecb, EVP_des_ede3_ecb);
    EST(evp_des_ede3_cbc, EVP_des_ede3_cbc); EST(evp_rc2_cbc, EVP_rc2_cbc);
}

static void resolu_ssl(void)
{
    EST(ssl_is_init_finished, SSL_is_init_finished); EST(ssl_get_servername, SSL_get_servername);
    OPT(ssl_renegotiate, SSL_renegotiate);
    EST(ssl_ctx_set_min_proto_version, SSL_CTX_set_min_proto_version); EST(ssl_ctx_set_max_proto_version, SSL_CTX_set_max_proto_version);
    EST(tls_client_method, TLS_client_method); OPT(tls_method, TLS_method);
    EST(ssl_ctx_new, SSL_CTX_new); EST(ssl_ctx_free, SSL_CTX_free); EST(ssl_new, SSL_new); EST(ssl_free, SSL_free);
    EST(ssl_set_connect_state, SSL_set_connect_state); EST(ssl_set_accept_state, SSL_set_accept_state); EST(ssl_get_version, SSL_get_version);
    EST(ssl_write, SSL_write); EST(ssl_read, SSL_read); EST(ssl_peek, SSL_peek); EST(ssl_get_error, SSL_get_error);
    EST(ssl_shutdown, SSL_shutdown); EST(ssl_set_bio, SSL_set_bio); EST(ssl_do_handshake, SSL_do_handshake);
    EST(ssl_get_peer_certificate, SSL_get_peer_certificate); EST(ssl_get_certificate, SSL_get_certificate);
    EST(ssl_get_peer_cert_chain, SSL_get_peer_cert_chain); EST(ssl_set_verify, SSL_set_verify);
    EST(ssl_use_certificate, SSL_use_certificate); EST(ssl_use_private_key, SSL_use_PrivateKey);
    EST(ssl_ctx_use_certificate, SSL_CTX_use_certificate); EST(ssl_ctx_use_private_key, SSL_CTX_use_PrivateKey);
    EST(ssl_ctx_check_private_key, SSL_CTX_check_private_key); EST(ssl_get_client_CA_list, SSL_get_client_CA_list);
}

static void resolu_ssl_suite_cache(void)
{
    EST(ssl_get_current_cipher, SSL_get_current_cipher); EST(cipher_get_id, SSL_CIPHER_get_id); EST(cipher_get_name, SSL_CIPHER_get_name);
    EST(cipher_get_version, SSL_CIPHER_get_version); OPT(cipher_find, SSL_CIPHER_find);
    EST(ssl_get_finished, SSL_get_finished); EST(ssl_get_peer_finished, SSL_get_peer_finished); EST(ssl_session_reused, SSL_session_reused);
    EST(ssl_get_session, SSL_get_session); EST(ssl_set_session, SSL_set_session); EST(ssl_session_free, SSL_SESSION_free);
    EST(ssl_session_up_ref, SSL_SESSION_up_ref); EST(ssl_renegotiate_pending, SSL_renegotiate_pending);
    EST(ssl_set_tlsext_host_name, SSL_set_tlsext_host_name); EST(ssl_set_alpn_protos, SSL_set_alpn_protos);
    EST(ssl_get0_alpn_selected, SSL_get0_alpn_selected); EST(ssl_set_tlsext_status_type, SSL_set_tlsext_status_type);
    EST(ssl_ctx_set_cipher_list, SSL_CTX_set_cipher_list); EST(ssl_set_cipher_list, SSL_set_cipher_list);
    /* OPT : absents du BoringSSL Android (sonde) — design « ignoré », jamais EST. */
    OPT(ssl_ctx_set_ciphersuites, SSL_CTX_set_ciphersuites); OPT(ssl_set_ciphersuites, SSL_set_ciphersuites);
    EST(ssl_ctx_set_session_cache_mode, SSL_CTX_set_session_cache_mode); EST(ssl_ctx_sess_set_cache_size, SSL_CTX_sess_set_cache_size);
    EST(ssl_ctx_sess_set_new_cb, SSL_CTX_sess_set_new_cb); EST(ssl_ctx_sess_set_remove_cb, SSL_CTX_sess_set_remove_cb);
    EST(ssl_ctx_set_session_id_context, SSL_CTX_set_session_id_context); EST(ssl_ctx_remove_session, SSL_CTX_remove_session);
    EST(ssl_get_ciphers, SSL_get_ciphers); EST(ssl_set1_sigalgs_list, SSL_set1_sigalgs_list);
}

/* Résolution de la table (une fois).
 * NB : un appel pret() ici serait un pthread_once réentrant → interblocage
 * (constat sonde on-device). charger() a déjà été passé par resolu_pret. */
static void resoudre(void)
{
    if (g_indisponible) return;
    resolu_bio_rng();
    resolu_digest_hmac();
    resolu_err_asn1_bignum();
    resolu_x509_cert();
    resolu_piles_stores();
    resolu_chiffreurs();
    resolu_ssl();
    resolu_ssl_suite_cache();
    /* BoringSSL Android : SHA3/SHAKE absents (sonde) → évaporation. */
    OPT(sha3_256, EVP_sha3_256); OPT(sha3_384, EVP_sha3_384); OPT(sha3_512, EVP_sha3_512); OPT(shake128, EVP_shake128); OPT(shake256, EVP_shake256);
#undef EST
#undef OPT
}

/* ---------- Registre ex_data ---------- */
typedef struct entree_reg {
    struct entree_reg* suivante;
    void* cle;
    void* valeur;
    void* secondaire;
} entree_reg_t;

static entree_reg_t* g_tete;
static pthread_mutex_t g_mutex_reg = PTHREAD_MUTEX_INITIALIZER;

static void reg_set(void* cle, void* valeur, void* secondaire)
{
    entree_reg_t* e;
    pthread_mutex_lock(&g_mutex_reg);
    for (e = g_tete; e != NULL; e = e->suivante)
    {
        if (e->cle == cle)
        {
            e->valeur = valeur;
            e->secondaire = secondaire;
            pthread_mutex_unlock(&g_mutex_reg);
            return;
        }
    }
    e = (entree_reg_t*)calloc(1, sizeof(entree_reg_t));
    if (e != NULL)
    {
        e->cle = cle; e->valeur = valeur; e->secondaire = secondaire;
        e->suivante = g_tete;
        g_tete = e;
    }
    pthread_mutex_unlock(&g_mutex_reg);
}

static void reg_del(void* cle)
{
    entree_reg_t** courant = &g_tete;
    pthread_mutex_lock(&g_mutex_reg);
    while (*courant != NULL)
    {
        if ((*courant)->cle == cle)
        {
            entree_reg_t* r = *courant;
            *courant = r->suivante;
            free(r);
            pthread_mutex_unlock(&g_mutex_reg);
            return;
        }
        courant = &(*courant)->suivante;
    }
    pthread_mutex_unlock(&g_mutex_reg);
}

static void* reg_get_valeur(void* cle)
{
    entree_reg_t* e;
    void* v = NULL;
    pthread_mutex_lock(&g_mutex_reg);
    for (e = g_tete; e != NULL; e = e->suivante)
        if (e->cle == cle) { v = e->valeur; break; }
    pthread_mutex_unlock(&g_mutex_reg);
    return v;
}

static void* reg_get_secondaire(void* cle)
{
    entree_reg_t* e;
    void* v = NULL;
    pthread_mutex_lock(&g_mutex_reg);
    for (e = g_tete; e != NULL; e = e->suivante)
        if (e->cle == cle) { v = e->secondaire; break; }
    pthread_mutex_unlock(&g_mutex_reg);
    return v;
}

/* ---------- État X509_STORE_CTX ---------- */
typedef struct etat_ctx {
    struct etat_ctx* suivante;
    X509_STORE_CTX* ctx;
    X509StoreVerifyCallback callback;
    void* appData;
    X509_STORE* store;   /* capture pour Reset */
    X509* cert;
    STACK* untrusted;
} etat_ctx_t;

static etat_ctx_t* g_tete_ctx;
static pthread_mutex_t g_mutex_ctx = PTHREAD_MUTEX_INITIALIZER;

static etat_ctx_t* etat_ctx_associer(X509_STORE_CTX* ctx)
{
    etat_ctx_t* e;
    pthread_mutex_lock(&g_mutex_ctx);
    for (e = g_tete_ctx; e != NULL; e = e->suivante)
        if (e->ctx == ctx) { pthread_mutex_unlock(&g_mutex_ctx); return e; }
    e = (etat_ctx_t*)calloc(1, sizeof(etat_ctx_t));
    if (e != NULL)
    {
        e->ctx = ctx;
        e->suivante = g_tete_ctx;
        g_tete_ctx = e;
    }
    pthread_mutex_unlock(&g_mutex_ctx);
    return e;
}

static etat_ctx_t* etat_ctx_chercher(X509_STORE_CTX* ctx)
{
    etat_ctx_t* e = g_tete_ctx;
    pthread_mutex_lock(&g_mutex_ctx);
    for (e = g_tete_ctx; e != NULL; e = e->suivante)
        if (e->ctx == ctx) break;
    pthread_mutex_unlock(&g_mutex_ctx);
    return e;
}

static void etat_ctx_retirer(X509_STORE_CTX* ctx)
{
    etat_ctx_t** courant = &g_tete_ctx;
    pthread_mutex_lock(&g_mutex_ctx);
    while (*courant != NULL)
    {
        if ((*courant)->ctx == ctx)
        {
            etat_ctx_t* r = *courant;
            *courant = r->suivante;
            free(r);
            pthread_mutex_unlock(&g_mutex_ctx);
            return;
        }
        courant = &(*courant)->suivante;
    }
    pthread_mutex_unlock(&g_mutex_ctx);
}

/* Pont verify_cb : BoringSSL → callback géré avec appData (miroir pal :
 * le callback géré délivre sa propre passerelle via X509StoreCtxGetAppData). */
static int pont_verify_cb(int preverify_ok, X509_STORE_CTX* ctx)
{
    etat_ctx_t* e = etat_ctx_chercher(ctx);
    if (e != NULL && e->callback != NULL) return e->callback(preverify_ok, ctx);
    return preverify_ok;
}

/* ---------- État SSL_SESSION (C1-revue) ----------
 * Un canal unique par session remplacé par une struct d'état : hostname
 * (strdup, libéré ici) et appData (opaque géré, JAMAIS libéré ici) vivent
 * dans des champs distincts. La sentinelle (void*)1 est supprimée : plus
 * de free((void*)1) (abort bionic) si l'ordre SetData → SetHostname est
 * inversé, plus d'appData écrasé si l'ordre inverse — séquence géré exacte
 * (pal release/10.0) : SslSessionSetData(session, cert) PUIS
 * SslSessionSetHostname(session, SNI) depuis NewSessionCallback. */
typedef struct etat_sess {
    struct etat_sess* suivante;
    SSL_SESSION* sess;
    char* hostname_alloc;   /* strdup maison, libéré par etat_sess_retirer */
    void* app_data;         /* pointeur opaque géré (cert GCHandle), non possédé */
} etat_sess_t;

static etat_sess_t* g_tete_sess;
static pthread_mutex_t g_mutex_sess = PTHREAD_MUTEX_INITIALIZER;

static etat_sess_t* etat_sess_associer(SSL_SESSION* sess)
{
    etat_sess_t* e;
    pthread_mutex_lock(&g_mutex_sess);
    for (e = g_tete_sess; e != NULL; e = e->suivante)
        if (e->sess == sess) { pthread_mutex_unlock(&g_mutex_sess); return e; }
    e = (etat_sess_t*)calloc(1, sizeof(etat_sess_t));
    if (e != NULL)
    {
        e->sess = sess;
        e->suivante = g_tete_sess;
        g_tete_sess = e;
    }
    pthread_mutex_unlock(&g_mutex_sess);
    return e;
}

static etat_sess_t* etat_sess_chercher(SSL_SESSION* sess)
{
    etat_sess_t* e;
    pthread_mutex_lock(&g_mutex_sess);
    for (e = g_tete_sess; e != NULL; e = e->suivante)
        if (e->sess == sess) break;
    pthread_mutex_unlock(&g_mutex_sess);
    return e;
}

/* Libération d'état : strdup hostname propre, appData jamais touché. */
static void etat_sess_retirer(SSL_SESSION* sess)
{
    etat_sess_t** courant = &g_tete_sess;
    pthread_mutex_lock(&g_mutex_sess);
    while (*courant != NULL)
    {
        if ((*courant)->sess == sess)
        {
            etat_sess_t* r = *courant;
            *courant = r->suivante;
            free(r->hostname_alloc);
            free(r);
            pthread_mutex_unlock(&g_mutex_sess);
            return;
        }
        courant = &(*courant)->suivante;
    }
    pthread_mutex_unlock(&g_mutex_sess);
}

/* Vue DER d'un certificat : cache registre (clé = X509*) ; le parseur pur
 * vit dans der_x509_parser.c (M5-revue), Appelant i2d + registre ici. */
static const vue_der_t* vue_de_x509(X509* x509)
{
    vue_der_t* v;
    unsigned char* tampon;
    unsigned char* curseur;
    int taille;

    if (x509 == NULL || !pret() || bssl.i2d_x509 == NULL) return NULL;
    v = (vue_der_t*)reg_get_valeur(x509);
    if (v != NULL) return v;

    taille = bssl.i2d_x509(x509, NULL);
    if (taille <= 0) return NULL;
    tampon = (unsigned char*)malloc((size_t)taille);
    if (tampon == NULL) return NULL;
    curseur = tampon;
    if (bssl.i2d_x509(x509, &curseur) != taille) { free(tampon); return NULL; }

    v = (vue_der_t*)calloc(1, sizeof(vue_der_t));
    if (v == NULL) { free(tampon); return NULL; }
    v->der = tampon;
    v->der_len = taille;
    der_parse_cert_vue(tampon, taille, v);

    reg_set(x509, v, NULL);
    /* Fuite évitée (revue) : si l'entrée registre n'a pas pu être créée (OOM),
     * aucune libération ne passera jamais par X509Destroy → abandon vue. */
    if (reg_get_valeur(x509) != v)
    {
        vue_der_liberer(v);
        return NULL;
    }
    return v;
}

/* ================= CORPS DES EXPORTS RÉELS ================= */

static void taille32_sortie(uint32_t* sortie, size_t taille)
{
    if (sortie != NULL) *sortie = (uint32_t)taille;
}

static void taille32_sortie_signed(int32_t* sortie, size_t taille)
{
    if (sortie != NULL) *sortie = (int32_t)taille;
}

/* ---------- ERR / Initialisation ---------- */

PAL void CryptoNative_ErrClearError(void)
{
    if (bssl.err_clear) bssl.err_clear();
}

PAL uint64_t CryptoNative_ErrPeekError(void) { return bssl.err_peek ? bssl.err_peek() : 0; }
PAL uint64_t CryptoNative_ErrPeekLastError(void) { return bssl.err_peek_last ? bssl.err_peek_last() : 0; }

/* Miroir pal_err.c : motif d'alloc = ERR_R_MALLOC_FAILURE (raison 4 BoringSSL). */
PAL uint64_t CryptoNative_ErrGetExceptionError(int32_t* isAllocFailure)
{
    uint64_t err = 0;
    if (bssl.err_peek_last)
    {
        err = bssl.err_peek_last();
        if (isAllocFailure != NULL) *isAllocFailure = (int32_t)((err & 0xFFF) == 4);
        bssl.err_clear();
    }
    return err;
}

PAL const char* CryptoNative_ErrReasonErrorString(uint64_t error)
{
    return bssl.err_reason_string ? bssl.err_reason_string(error) : NULL;
}

PAL void CryptoNative_ErrErrorStringN(uint64_t e, char* buf, int32_t len)
{
    if (!bssl.err_error_string_n)
    {
        if (buf != NULL && len > 0) buf[0] = '\0';
        return;
    }
    bssl.err_error_string_n(e, buf, len < 0 ? 0 : (size_t)len);
}

/* BoringSSL système : aucune init requise (OPENSSL_init_* absent — sonde) ;
 * retour 0 = succès pal contractuel (non nul → InvalidOperationException géré). */
PAL int32_t CryptoNative_EnsureOpenSslInitialized(void) { return !pret() ? 1 : 0; }

PAL void CryptoNative_EnsureLibSslInitialized(void) { (void)pret(); }

PAL int32_t CryptoNative_OpenSslAvailable(void) { return pret(); }

PAL int64_t CryptoNative_OpenSslVersionNumber(void)
{
    int64_t (*fn)(void) = (int64_t(*)(void))symc("OpenSSL_version_num");
    return fn ? fn() : 0;
}

/* BoringSSL : algorithmes par défaut uniquement — aucune action. */
PAL void CryptoNative_RegisterLegacyAlgorithms(void) {}

/* ---------- RNG (/dev/urandom direct, indépendant de BoringSSL — stub v2) ---------- */

PAL int32_t CryptoNative_GetRandomBytes(uint8_t* buff, int32_t len)
{
    int fd;
    int total = 0;
    if (buff == NULL || len < 0) return PAL_ECHEC;
    if (len == 0) return PAL_SUCCES;
    fd = open("/dev/urandom", O_RDONLY);
    if (fd < 0) return PAL_ECHEC;
    while (total < len)
    {
        ssize_t lu = read(fd, buff + total, (size_t)(len - total));
        if (lu < 0 && errno == EINTR) continue;   /* revue : signal non fatal */
        if (lu <= 0) { close(fd); return PAL_ECHEC; }
        total += (int)lu;
    }
    close(fd);
    return PAL_SUCCES;
}

/* ---------- Empreintes ---------- */

PAL const EVP_MD* CryptoNative_EvpMd5(void) { return bssl.md5 ? bssl.md5() : NULL; }
PAL const EVP_MD* CryptoNative_EvpSha1(void) { return bssl.sha1 ? bssl.sha1() : NULL; }
PAL const EVP_MD* CryptoNative_EvpSha256(void) { return bssl.sha256 ? bssl.sha256() : NULL; }
PAL const EVP_MD* CryptoNative_EvpSha384(void) { return bssl.sha384 ? bssl.sha384() : NULL; }
PAL const EVP_MD* CryptoNative_EvpSha512(void) { return bssl.sha512 ? bssl.sha512() : NULL; }
PAL const EVP_MD* CryptoNative_EvpSha3_256(void) { return bssl.sha3_256 ? bssl.sha3_256() : NULL; }
PAL const EVP_MD* CryptoNative_EvpSha3_384(void) { return bssl.sha3_384 ? bssl.sha3_384() : NULL; }
PAL const EVP_MD* CryptoNative_EvpSha3_512(void) { return bssl.sha3_512 ? bssl.sha3_512() : NULL; }
PAL const EVP_MD* CryptoNative_EvpShake128(void) { return bssl.shake128 ? bssl.shake128() : NULL; }
PAL const EVP_MD* CryptoNative_EvpShake256(void) { return bssl.shake256 ? bssl.shake256() : NULL; }

PAL int32_t CryptoNative_EvpMdSize(const EVP_MD* md)
{
    return (bssl.md_size != NULL && md != NULL) ? (int32_t)bssl.md_size(md) : -1;
}

PAL int32_t CryptoNative_GetMaxMdSize(void) { return MD_TAILLE_MAX; }

PAL EVP_MD_CTX* CryptoNative_EvpMdCtxCreate(const EVP_MD* type)
{
    EVP_MD_CTX* ctx;
    if (type == NULL || bssl.md_ctx_new == NULL) return NULL;
    ctx = bssl.md_ctx_new();
    if (ctx == NULL) return NULL;
    if (bssl.digest_init_ex(ctx, type, NULL) != 1) { bssl.md_ctx_free(ctx); return NULL; }
    return ctx;
}

PAL void CryptoNative_EvpMdCtxDestroy(EVP_MD_CTX* ctx)
{
    if (ctx != NULL && bssl.md_ctx_free) bssl.md_ctx_free(ctx);
}

PAL EVP_MD_CTX* CryptoNative_EvpMdCtxCopyEx(const EVP_MD_CTX* ctx)
{
    EVP_MD_CTX* clone;
    if (ctx == NULL || bssl.md_ctx_new == NULL) return NULL;
    clone = bssl.md_ctx_new();
    if (clone == NULL) return NULL;
    if (bssl.md_ctx_copy_ex(clone, ctx) != 1) { bssl.md_ctx_free(clone); return NULL; }
    return clone;
}

PAL int32_t CryptoNative_EvpDigestReset(EVP_MD_CTX* ctx, const EVP_MD* type)
{
    if (ctx == NULL || type == NULL) return PAL_ECHEC;
    return bssl.digest_init_ex(ctx, type, NULL) == 1 ? PAL_SUCCES : PAL_ECHEC;
}

PAL int32_t CryptoNative_EvpDigestUpdate(EVP_MD_CTX* ctx, const void* d, int32_t cnt)
{
    if (ctx == NULL || cnt < 0 || (cnt > 0 && d == NULL)) return PAL_ECHEC;
    if (cnt == 0) return PAL_SUCCES;
    return bssl.digest_update(ctx, d, (size_t)cnt) == 1 ? PAL_SUCCES : PAL_ECHEC;
}

PAL int32_t CryptoNative_EvpDigestFinalEx(EVP_MD_CTX* ctx, uint8_t* md, uint32_t* s)
{
    size_t taille;
    if (ctx == NULL || md == NULL || bssl.digest_final_ex == NULL) return PAL_ECHEC;
    if (bssl.digest_final_ex(ctx, md, &taille) != 1) return PAL_ECHEC;
    taille32_sortie(s, taille);
    return PAL_SUCCES;
}

/* XOF (SHAKE) non exposé par le BoringSSL Android : échec propre. */
PAL int32_t CryptoNative_EvpDigestFinalXOF(EVP_MD_CTX* ctx, uint8_t* md, uint32_t len)
{
    (void)ctx; (void)md; (void)len;
    return PAL_ECHEC;
}

PAL int32_t CryptoNative_EvpDigestCurrent(const EVP_MD_CTX* ctx, uint8_t* md, uint32_t* s)
{
    EVP_MD_CTX* clone;
    int32_t ret;
    clone = CryptoNative_EvpMdCtxCopyEx(ctx);
    if (clone == NULL) return PAL_ECHEC;
    ret = CryptoNative_EvpDigestFinalEx(clone, md, s);
    CryptoNative_EvpMdCtxDestroy(clone);
    return ret;
}

PAL int32_t CryptoNative_EvpDigestCurrentXOF(const EVP_MD_CTX* ctx, uint8_t* md, uint32_t len)
{
    (void)ctx; (void)md; (void)len;
    return PAL_ECHEC;
}

PAL int32_t CryptoNative_EvpDigestOneShot(const EVP_MD* type, const void* source, int32_t sourceSize, uint8_t* md, uint32_t* mdSize)
{
    EVP_MD_CTX* ctx;
    int32_t ret;
    if (type == NULL || md == NULL || mdSize == NULL || sourceSize < 0 || (sourceSize > 0 && source == NULL))
        return PAL_ECHEC;
    ctx = CryptoNative_EvpMdCtxCreate(type);
    if (ctx == NULL) return PAL_ECHEC;
    ret = sourceSize == 0 ? PAL_SUCCES : CryptoNative_EvpDigestUpdate(ctx, source, sourceSize);
    if (ret == PAL_SUCCES) ret = CryptoNative_EvpDigestFinalEx(ctx, md, mdSize);
    CryptoNative_EvpMdCtxDestroy(ctx);
    return ret;
}

PAL int32_t CryptoNative_EvpDigestXOFOneShot(const EVP_MD* type, const void* source, int32_t sourceSize, uint8_t* md, uint32_t len)
{
    (void)type; (void)source; (void)sourceSize; (void)md; (void)len;
    return PAL_ECHEC;
}

/* BoringSSL : EVP_DigestSqueeze absent (sonde) ; haveFeature=0 informe le géré. */
PAL int32_t CryptoNative_EvpDigestSqueeze(EVP_MD_CTX* ctx, uint8_t* md, uint32_t len, int32_t* haveFeature)
{
    (void)ctx; (void)md; (void)len;
    if (haveFeature != NULL) *haveFeature = 0;
    return PAL_ECHEC;
}

/* ---------- HMAC ---------- */

PAL HMAC_CTX* CryptoNative_HmacCreate(const uint8_t* key, int32_t keyLen, const EVP_MD* md)
{
    HMAC_CTX* ctx;
    if (md == NULL || bssl.hmac_new == NULL || bssl.hmac_init_ex == NULL) return NULL;
    if (key == NULL && keyLen != 0) return NULL;
    ctx = bssl.hmac_new();
    if (ctx == NULL) return NULL;
    /* Clé vide = octet nul (miroir pal HMAC_Init_ex : l'alu HMAC couvre vide). */
    if (bssl.hmac_init_ex(ctx, keyLen > 0 ? (const void*)key : (const void*)"", keyLen > 0 ? keyLen : 0, md, NULL) != 1)
    {
        bssl.hmac_free(ctx);
        return NULL;
    }
    return ctx;
}

PAL void CryptoNative_HmacDestroy(HMAC_CTX* ctx)
{
    if (ctx != NULL && bssl.hmac_free) bssl.hmac_free(ctx);
}

PAL int32_t CryptoNative_HmacReset(HMAC_CTX* ctx)
{
    if (ctx == NULL || bssl.hmac_init_ex == NULL) return PAL_ECHEC;
    return bssl.hmac_init_ex(ctx, NULL, 0, NULL, NULL) == 1 ? PAL_SUCCES : PAL_ECHEC;
}

PAL int32_t CryptoNative_HmacUpdate(HMAC_CTX* ctx, const uint8_t* data, int32_t len)
{
    if (ctx == NULL || data == NULL || len <= 0) return PAL_ECHEC;
    bssl.hmac_update(ctx, data, (size_t)len);
    return PAL_SUCCES;
}

PAL int32_t CryptoNative_HmacFinal(HMAC_CTX* ctx, uint8_t* md, int32_t* len)
{
    size_t taille;
    if (ctx == NULL || md == NULL || len == NULL || bssl.hmac_final == NULL) return PAL_ECHEC;
    if (bssl.hmac_final(ctx, md, &taille) != 1) return PAL_ECHEC;
    taille32_sortie_signed(len, taille);
    return PAL_SUCCES;
}

PAL int32_t CryptoNative_HmacCurrent(const HMAC_CTX* ctx, uint8_t* md, int32_t* len)
{
    HMAC_CTX* copie;
    size_t taille;
    if (ctx == NULL || md == NULL || len == NULL || bssl.hmac_copy == NULL) return PAL_ECHEC;
    copie = bssl.hmac_copy((HMAC_CTX*)ctx);
    if (copie == NULL) return PAL_ECHEC;
    if (bssl.hmac_final(copie, md, &taille) != 1) { bssl.hmac_free(copie); return PAL_ECHEC; }
    bssl.hmac_free(copie);
    taille32_sortie_signed(len, taille);
    return PAL_SUCCES;
}

PAL HMAC_CTX* CryptoNative_HmacCopy(const HMAC_CTX* ctx)
{
    if (ctx == NULL || bssl.hmac_copy == NULL) return NULL;
    return bssl.hmac_copy((HMAC_CTX*)ctx);
}

PAL int32_t CryptoNative_HmacOneShot(const EVP_MD* type, const uint8_t* key, int32_t keySize, const uint8_t* source, int32_t sourceSize, uint8_t* md, int32_t* mdSize)
{
    HMAC_CTX* ctx;
    int32_t ret;
    if (type == NULL || md == NULL || mdSize == NULL || keySize < 0 || sourceSize < 0 ||
        (key == NULL && keySize != 0) || (source == NULL && sourceSize != 0))
        return -1;
    ctx = CryptoNative_HmacCreate(key, keySize, type);
    if (ctx == NULL) return PAL_ECHEC;
    ret = sourceSize == 0 ? PAL_SUCCES : CryptoNative_HmacUpdate(ctx, source, sourceSize);
    if (ret == PAL_SUCCES) ret = CryptoNative_HmacFinal(ctx, md, mdSize);
    CryptoNative_HmacDestroy(ctx);
    return ret;
}

/* ---------- PBKDF2 ---------- */

PAL int32_t CryptoNative_Pbkdf2(const char* password, int32_t passwordLength, const unsigned char* salt, int32_t saltLength, int32_t iterations, const EVP_MD* digest, unsigned char* destination, int32_t destinationLength)
{
    if (passwordLength < 0 || saltLength < 0 || iterations <= 0 || digest == NULL ||
        destination == NULL || destinationLength < 0) return -1;
    if (password == NULL && passwordLength != 0) return -1;
    if (salt == NULL && saltLength != 0) return -1;
    if (bssl.pbkdf2 == NULL) return PAL_ECHEC;
    return bssl.pbkdf2(password != NULL ? password : "",
                       (size_t)passwordLength,
                       salt != NULL ? salt : (const unsigned char*)"",
                       (size_t)saltLength,
                       (unsigned long)iterations,
                       digest,
                       (size_t)destinationLength,
                       destination);
}

/* ---------- BIO ---------- */

PAL BIO* CryptoNative_CreateMemoryBio(void)
{
    if (bssl.bio_s_mem == NULL || bssl.bio_new == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.bio_new(bssl.bio_s_mem());
}

PAL BIO* CryptoNative_BioNewFile(const char* filename, const char* mode)
{
    if (bssl.bio_new_file == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.bio_new_file(filename, mode);
}

PAL int32_t CryptoNative_BioDestroy(BIO* a)
{
    if (a == NULL) return PAL_SUCCES;
    if (bssl.bio_free == NULL) return PAL_ECHEC;
    return bssl.bio_free(a);
}

PAL int32_t CryptoNative_BioGets(BIO* b, char* buf, int32_t size)
{
    if (b == NULL || buf == NULL || size < 0 || bssl.bio_gets == NULL) return PAL_ECHEC;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.bio_gets(b, buf, size);
}

PAL int32_t CryptoNative_BioRead(BIO* b, void* buf, int32_t len)
{
    if (b == NULL || buf == NULL || len < 0 || bssl.bio_read == NULL) return PAL_ECHEC;
    if (len == 0) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.bio_read(b, buf, len);
}

PAL int32_t CryptoNative_BioWrite(BIO* b, const void* buf, int32_t len)
{
    if (b == NULL || buf == NULL || len < 0 || bssl.bio_write == NULL) return PAL_ECHEC;
    if (len == 0) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.bio_write(b, buf, len);
}

/* BIO_get_mem_data macro = BIO_ctrl(BIO_C_GET_MEM_DATA). */
PAL int32_t CryptoNative_GetMemoryBioSize(BIO* bio)
{
    if (bio == NULL || bssl.bio_ctrl == NULL) return 0;
    return (int32_t)bssl.bio_ctrl(bio, BIO_C_GET_MEM_DATA, 0, NULL);
}

/* BIO_ctrl_pending macro = BIO_ctrl(BIO_CTRL_PENDING). */
PAL int32_t CryptoNative_BioCtrlPending(BIO* bio)
{
    if (bio == NULL || bssl.bio_ctrl == NULL) return 0;
    return (int32_t)bssl.bio_ctrl(bio, BIO_CTRL_PENDING, 0, NULL);
}

/* Macros BIO_tell/seek = BIO_ctrl(BIO_C_FILE_TELL/SEEK). */
PAL int32_t CryptoNative_BioTell(BIO* bio)
{
    if (bio == NULL || bssl.bio_ctrl == NULL) return -1;
    return (int32_t)bssl.bio_ctrl(bio, BIO_C_FILE_TELL, 0, NULL);
}

PAL int32_t CryptoNative_BioSeek(BIO* bio, int32_t ofs)
{
    if (bio == NULL || ofs < 0 || bssl.bio_ctrl == NULL) return -1;
    return (int32_t)bssl.bio_ctrl(bio, BIO_C_FILE_SEEK, (long)ofs, NULL);
}

/* ---------- OID / ASN1 / BIGNUM ---------- */

PAL const ASN1_OBJECT* CryptoNative_ObjTxt2Obj(const char* s)
{
    return bssl.obj_txt2obj ? bssl.obj_txt2obj(s, 0) : NULL;
}

PAL int32_t CryptoNative_ObjObj2Txt(char* buf, int32_t buf_len, const ASN1_OBJECT* a)
{
    if (a == NULL || buf == NULL || buf_len < 0 || bssl.obj_obj2txt == NULL) return -1;
    return bssl.obj_obj2txt(buf, buf_len, a, 0);
}

PAL int32_t CryptoNative_ObjTxt2Nid(const char* sn)
{
    return bssl.obj_txt2nid ? bssl.obj_txt2nid(sn) : 0;
}

PAL const ASN1_OBJECT* CryptoNative_ObjNid2Obj(int32_t nid)
{
    return bssl.obj_nid2obj ? bssl.obj_nid2obj(nid) : NULL;
}

PAL void CryptoNative_Asn1ObjectFree(ASN1_OBJECT* a)
{
    if (a != NULL && bssl.asn1_object_free) bssl.asn1_object_free(a);
}

PAL ASN1_OCTET_STRING* CryptoNative_Asn1OctetStringNew(void)
{
    return bssl.octet_new ? bssl.octet_new() : NULL;
}

PAL int32_t CryptoNative_Asn1OctetStringSet(ASN1_OCTET_STRING* s, const uint8_t* data, int32_t len)
{
    if (s == NULL || len < 0 || (len > 0 && data == NULL) || bssl.octet_set == NULL) return PAL_ECHEC;
    return bssl.octet_set(s, len > 0 ? data : (const uint8_t*)"", len) == 1 ? PAL_SUCCES : PAL_ECHEC;
}

PAL void CryptoNative_Asn1OctetStringFree(ASN1_STRING* a)
{
    if (a != NULL && bssl.octet_free) bssl.octet_free(a);
}

/* Miroir pal_asn1.c : i2d(NULL) = taille nécessaire. */
PAL int32_t CryptoNative_GetAsn1IntegerDerSize(ASN1_STRING* i)
{
    if (i == NULL || bssl.i2d_asn1_integer == NULL) return -1;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.i2d_asn1_integer(i, NULL);
}

PAL int32_t CryptoNative_EncodeAsn1Integer(ASN1_STRING* i, uint8_t* buf)
{
    unsigned char* tampon;
    if (i == NULL || buf == NULL || bssl.i2d_asn1_integer == NULL) return -1;
    if (bssl.err_clear) bssl.err_clear();
    tampon = buf;
    return bssl.i2d_asn1_integer(i, &tampon);
}

/* Layout ASN1_STRING trivial partagé (coquilles + BoringSSL identiques). */
PAL int32_t CryptoNative_GetAsn1StringBytes(ASN1_STRING* asn1, uint8_t* pBuf, int32_t cBuf)
{
    int longueur;
    if (asn1 == NULL || pBuf == NULL || cBuf < 0) return -1;
    longueur = asn1->length;
    if (longueur < 0) return 0;
    if (longueur > cBuf) return -1;
    if (longueur > 0) memcpy(pBuf, asn1->data, (size_t)longueur);
    return longueur;
}

PAL void CryptoNative_BigNumDestroy(BIGNUM* a)
{
    if (a != NULL && bssl.bn_clear_free) bssl.bn_clear_free(a);
}

PAL int32_t CryptoNative_BigNumToBinary(const BIGNUM* a, uint8_t* to)
{
    if (a == NULL || to == NULL || bssl.bn_bn2bin == NULL) return -1;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.bn_bn2bin(a, to);
}

PAL BIGNUM* CryptoNative_BigNumFromBinary(const uint8_t* s, int32_t len)
{
    if ((len > 0 && s == NULL) || len < 0 || bssl.bn_bin2bn == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.bn_bin2bn(len > 0 ? s : (const uint8_t*)"", len, NULL);
}

PAL int32_t CryptoNative_GetBigNumBytes(const BIGNUM* a)
{
    if (a == NULL || bssl.bn_num_bits == NULL) return -1;
    if (bssl.err_clear) bssl.err_clear();
    return (bssl.bn_num_bits(a) + 7) / 8;
}

/* ---------- Suivi mémoire (pal trivial : jamais activé par le géré) ---------- */

PAL void CryptoNative_EnableMemoryTracking(int32_t enable) { (void)enable; }

PAL void CryptoNative_GetMemoryUse(uint64_t* totalUsed, uint64_t* allocationCount)
{
    if (totalUsed != NULL) *totalUsed = 0;
    if (allocationCount != NULL) *allocationCount = 0;
}

PAL void CryptoNative_ForEachTrackedAllocation(void (*callback)(void*, uint64_t, const char*, int32_t, void*), void* ctx)
{
    (void)callback; (void)ctx;
}

/* ---------- X509 : décodage / codage / lecture ---------- */

/* OID contents → NID via dotted decimal (X509_get_X509_PUBKEY absent du
 * BoringSSL Android — cf. vue_de_x509). Le démontage base-128 vit dans
 * der_x509_parser.c (M5-revue) — plus de lecture hors bornes (UB corrigée). */
static int oid_dotted_nid(const unsigned char* pers, int len)
{
    char buf[128];
    if (bssl.obj_txt2nid == NULL) return 0;
    if (!der_oid_dotted(pers, len, buf, sizeof(buf))) return 0;
    return bssl.obj_txt2nid(buf);
}

PAL int32_t CryptoNative_GetX509Version(X509* x509)
{
    const vue_der_t* v = x509 != NULL ? vue_de_x509(x509) : NULL;
    return v != NULL ? v->version : -1;
}

PAL int32_t CryptoNative_GetX509DerSize(X509* x)
{
    if (x == NULL || bssl.i2d_x509 == NULL) return -1;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.i2d_x509(x, NULL);
}

PAL int32_t CryptoNative_EncodeX509(X509* x, uint8_t* buf)
{
    unsigned char* tampon;
    if (x == NULL || buf == NULL || bssl.i2d_x509 == NULL) return -1;
    if (bssl.err_clear) bssl.err_clear();
    tampon = buf;
    return bssl.i2d_x509(x, &tampon);
}

PAL void CryptoNative_X509Destroy(X509* a)
{
    if (a != NULL)
    {
        vue_der_t* v = (vue_der_t*)reg_get_valeur(a);
        if (v != NULL) vue_der_liberer(v);
        reg_del(a);
        /* Session/SSL liés ne référencent pas l'X509 via le registre : rien d'autre. */
        if (bssl.x509_free) bssl.x509_free(a);
    }
}

PAL X509* CryptoNative_X509Duplicate(X509* x509)
{
    if (x509 == NULL || bssl.x509_dup == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.x509_dup(x509);
}

/* Revue : fail-safe — sans up-ref possible, renvoyer NULL évite un double
 * free côté géré (le second détenteur croit posséder sa propre réf). */
PAL X509* CryptoNative_X509UpRef(X509* x509)
{
    if (x509 == NULL) return NULL;
    if (!pret() || bssl.x509_up_ref == NULL) return NULL;
    if (bssl.x509_up_ref(x509) != 1) return NULL;
    return x509;
}

PAL X509* CryptoNative_DecodeX509(const uint8_t* buf, int32_t len)
{
    if (buf == NULL || len == 0 || bssl.d2i_x509 == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    {
        const unsigned char* cif = buf;
        return bssl.d2i_x509(NULL, &cif, (long)len);
    }
}

PAL X509* CryptoNative_PemReadX509FromBio(BIO* bio)
{
    if (bio == NULL || bssl.pem_read_bio_X509 == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.pem_read_bio_X509(bio, NULL, NULL, NULL);
}

PAL X509* CryptoNative_PemReadX509FromBioAux(BIO* bio)
{
    if (bio == NULL || bssl.pem_read_bio_X509_aux == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.pem_read_bio_X509_aux(bio, NULL, NULL, NULL);
}

PAL X509* CryptoNative_ReadX509AsDerFromBio(BIO* bio)
{
    if (bio == NULL || bssl.d2i_x509_bio == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.d2i_x509_bio(bio, NULL);
}

/* Empreinte SHA1 du DER (pal X509_digest). Retour 1 copié, -cBuf insuffisant, 0 échec. */
PAL int32_t CryptoNative_GetX509Thumbprint(X509* x509, uint8_t* pBuf, int32_t cBuf)
{
    unsigned char sha[20];
    if (x509 == NULL) return 0;
    if (cBuf < 20) return -20;
    if (bssl.sha1 == NULL || bssl.x509_digest == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    /* Motif managed : BoringSSL X509_digest écrit 20 octets (SHA1). */
    if (!bssl.x509_digest(x509, bssl.sha1(), sha, NULL)) return 0;
    memcpy(pBuf, sha, sizeof(sha));
    return 1;
}

PAL void* CryptoNative_GetX509NotBefore(X509* x509)
{
    return (x509 != NULL && bssl.x509_get0_notbefore) ? bssl.x509_get0_notbefore(x509) : NULL;
}

PAL void* CryptoNative_GetX509NotAfter(X509* x509)
{
    return (x509 != NULL && bssl.x509_get0_notafter) ? bssl.x509_get0_notafter(x509) : NULL;
}

PAL const void* CryptoNative_GetX509CrlNextUpdate(X509_CRL* crl)
{
    /* X509_CRL_get0_nextUpdate absent du BoringSSL Android (sonde) — hooks via
     * X509_get0_notafter impossible. Échec propre (le géré ignore si NULL). */
    (void)crl;
    return NULL;
}

/* Récupère l' EVP_PKEY du cert (BoringSSL X509_get_pubkey — renvoie un ref fraîche). */
PAL EVP_PKEY* CryptoNative_GetX509EvpPublicKey(X509* x509)
{
    if (x509 == NULL || bssl.x509_get_pubkey == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.x509_get_pubkey(x509);
}

/* AlgorithmIdentifer OID du SPKI en objet ASN1_OBJECT réelle (nid2obj).
 * Revue (M5) : algide = contenu brut de l'OID (parser exact, plus d'hypothèse
 * d'en-tête short-form de 2 octets). */
PAL ASN1_OBJECT* CryptoNative_GetX509PublicKeyAlgorithm(X509* x509)
{
    const vue_der_t* v = x509 != NULL ? vue_de_x509(x509) : NULL;
    int nid;
    if (v == NULL || v->algide == NULL || v->algide_len <= 0) return NULL;
    nid = oid_dotted_nid(v->algide, v->algide_len);
    if (nid == 0 || bssl.obj_nid2obj == NULL) return NULL;
    return bssl.obj_nid2obj(nid);
}

/* AlgorithmIdentifier OID de signature (tbsCert.signature) : non capturé dans
 * la vue (spare) — le géré l'emploie pour l'affichage/comparaison, pas le TLS ;
 * TLS n'exige que l'algo du SPKI. Échec propre (aucun parse superflu). */
PAL ASN1_OBJECT* CryptoNative_GetX509SignatureAlgorithm(X509* x509)
{
    (void)x509;
    return NULL;
}

/* Clé publique brute : coquille ASN1_STRING construite sur le contenu du
 * BIT STRING (sans l'octet d'inutilisé) jeté dans la vue DER en cache —
 * libérée avec la vue par X509Destroy. */
PAL struct asn1_string_st* CryptoNative_GetX509PublicKeyBytes(X509* x509)
{
    vue_der_t* v = x509 != NULL ? (vue_der_t*)vue_de_x509(x509) : NULL;
    if (v == NULL || v->cle == NULL || v->cle_len <= 0) return NULL;
    if (v->coquille_cle == NULL)
    {
        v->coquille_cle = (struct asn1_string_st*)calloc(1, sizeof(struct asn1_string_st));
        if (v->coquille_cle == NULL) return NULL;
        v->coquille_cle->data = (unsigned char*)v->cle;
        v->coquille_cle->length = v->cle_len;
        v->coquille_cle->type = 3; /* V_ASN1_BIT_STRING */
    }
    return v->coquille_cle;
}

PAL int32_t CryptoNative_GetX509PublicKeyParameterBytes(X509* x509, uint8_t* pBuf, int32_t cBuf)
{
    const vue_der_t* v = x509 != NULL ? vue_de_x509(x509) : NULL;
    if (x509 == NULL) return 0;
    if (v == NULL) return 0;
    if (v->parametres == NULL || v->parametres_len <= 0)
    {
        /* pal : 2 = aucune donnée (appanteur NULL attendu du côté géré). */
        return 2;
    }
    if (cBuf < v->parametres_len) return -v->parametres_len;
    memcpy(pBuf, v->parametres, (size_t)v->parametres_len);
    return 1;
}

PAL int32_t CryptoNative_GetX509SubjectPublicKeyInfoDerSize(X509* x509)
{
    const vue_der_t* v = x509 != NULL ? vue_de_x509(x509) : NULL;
    return (v != NULL && v->spki_len > 0) ? v->spki_len : -1;
}

PAL int32_t CryptoNative_EncodeX509SubjectPublicKeyInfo(X509* x509, uint8_t* buf)
{
    const vue_der_t* v = x509 != NULL ? vue_de_x509(x509) : NULL;
    if (v == NULL || buf == NULL || v->spki_len <= 0) return -1;
    memcpy(buf, v->spki, (size_t)v->spki_len);
    return v->spki_len;
}

/* DER brut du nom (i2d_X509_name ; X509_NAME_get0_der absent de la sonde). */
PAL int32_t CryptoNative_GetX509NameRawBytes(X509_NAME* x509Name, uint8_t* pBuf, int32_t cBuf)
{
    int taille;
    unsigned char* tampon;
    if (x509Name == NULL || cBuf < 0 || bssl.i2d_x509_name == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    taille = bssl.i2d_x509_name(x509Name, NULL);
    if (taille < 0) return 0;
    if (pBuf == NULL || cBuf < taille) return -taille;
    tampon = pBuf;
    if (bssl.i2d_x509_name(x509Name, &tampon) != taille) return 0;
    return 1;
}

/* Nom (subject/issuer) intéreior BoringSSL */
PAL ASN1_STRING* CryptoNative_X509GetSerialNumber(X509* x509)
{
    if (x509 == NULL || bssl.x509_get_serial == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.x509_get_serial(x509);
}

PAL X509_NAME* CryptoNative_X509GetIssuerName(X509* x509)
{
    return (x509 != NULL && bssl.x509_get_issuer) ? bssl.x509_get_issuer(x509) : NULL;
}

PAL X509_NAME* CryptoNative_X509GetSubjectName(X509* x509)
{
    return (x509 != NULL && bssl.x509_get_subject) ? bssl.x509_get_subject(x509) : NULL;
}

PAL uint64_t CryptoNative_X509IssuerNameHash(X509* x509)
{
    if (x509 == NULL || bssl.x509_issuer_name_hash == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return (uint64_t)bssl.x509_issuer_name_hash(x509);
}

PAL int32_t CryptoNative_X509CheckPurpose(X509* x, int32_t id, int32_t ca)
{
    if (x == NULL || bssl.x509_check_purpose == NULL) return -1;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.x509_check_purpose(x, id, ca);
}

PAL int32_t CryptoNative_X509GetExtCount(X509* x)
{
    if (x == NULL || bssl.x509_get_ext_count == NULL) return -1;
    return bssl.x509_get_ext_count(x);
}

PAL X509_EXTENSION* CryptoNative_X509GetExt(X509* x, int32_t loc)
{
    if (x == NULL || bssl.x509_get_ext == NULL) return NULL;
    return bssl.x509_get_ext(x, loc);
}

PAL void* CryptoNative_X509ExtensionGetOid(X509_EXTENSION* x)
{
    return (x != NULL && bssl.ext_get_object) ? bssl.ext_get_object(x) : NULL;
}

PAL ASN1_OCTET_STRING* CryptoNative_X509ExtensionGetData(X509_EXTENSION* x)
{
    return (x != NULL && bssl.ext_get_data) ? bssl.ext_get_data(x) : NULL;
}

PAL int32_t CryptoNative_X509ExtensionGetCritical(X509_EXTENSION* x)
{
    if (x == NULL || bssl.ext_get_critical == NULL) return 0;
    return bssl.ext_get_critical(x);
}

/* Miroir pal : X509_get_ext_by_NID puis données d'extension. */
PAL ASN1_OCTET_STRING* CryptoNative_X509FindExtensionData(X509* x, int32_t nid)
{
    int nombre;
    int i;
    if (x == NULL || nid == 0) return NULL;
    nombre = CryptoNative_X509GetExtCount(x);
    if (nombre <= 0 || bssl.obj_obj2nid == NULL) return NULL;
    for (i = 0; i < nombre; i++)
    {
        X509_EXTENSION* ext = CryptoNative_X509GetExt(x, i);
        void* oid;
        int nidext;
        if (ext == NULL) continue;
        oid = CryptoNative_X509ExtensionGetOid(ext);
        if (oid == NULL) continue;
        nidext = bssl.obj_obj2nid((const ASN1_OBJECT*)oid);
        if (nidext == nid) return CryptoNative_X509ExtensionGetData(ext);
    }
    return NULL;
}

/* X509_check_host : NX_PARTIAL_WILDCARDS drogné à 4 (pal ssl). */
PAL int32_t CryptoNative_CheckX509Hostname(X509* x509, const char* hostname, int32_t cchHostname)
{
    if (x509 == NULL) return -3;
    if (cchHostname > 0 && hostname == NULL) return -4;
    if (cchHostname < 0) return -5;
    if (cchHostname > 0 && hostname[0] == '.') return 0;
    if (bssl.x509_check_host == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.x509_check_host(x509, hostname, (size_t)cchHostname, X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS, NULL);
}

/* SAN IP : boring X509_check_ip réelle (pal : 1/0 + codes d'entrée négatifs). */
PAL int32_t CryptoNative_CheckX509IpAddress(X509* x509, const uint8_t* addressBytes, int32_t addressBytesLen, const char* hostname, int32_t cchHostname)
{
    int retour;
    if (x509 == NULL) return -2;
    if (cchHostname > 0 && hostname == NULL) return -3;
    if (cchHostname < 0) return -4;
    if (addressBytesLen < 0) return -5;
    if (addressBytes == NULL) return -6;
    if (bssl.err_clear) bssl.err_clear();
    retour = bssl.x509_check_ip(x509, addressBytes, (size_t)addressBytesLen, 0);
    /* pal : 1 = valide ; check_ip couvre SAN IP + repli CN texte. */
    return retour > 0 ? 1 : 0;
}

/* ---------- X509Chain stores ---------- */
typedef enum {
    DRAPEAU_REVOCATION_FIN = 0,
    DRAPEAU_REVOCATION_CHAINE = 1,
    DRAPEAU_REVOCATION_HORS_RACINE = 2
} drapeau_revoc_t;

PAL void CryptoNative_X509StoreDestroy(X509_STORE* v)
{
    if (v != NULL && bssl.store_free) bssl.store_free(v);   /* revue : null-check symbole */
}

PAL int32_t CryptoNative_X509StoreAddCrl(X509_STORE* ctx, X509_CRL* x)
{
    if (ctx == NULL || x == NULL || bssl.store_add_crl == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.store_add_crl(ctx, x);
}

/* BoringSSL sans CRL : signal OK, pas de contrôle revocation (miroir pal
 * applique des drapeaux, mais leur abs. ne provoque pas d'erreur bloquante). */
PAL int32_t CryptoNative_X509StoreSetRevocationFlag(X509_STORE* ctx, X509RevocationFlag revocationFlag)
{
    unsigned long drapeaux;
    if (ctx == NULL) return 0;
    switch (revocationFlag)
    {
        case EndCertificateOnly: drapeaux = 0; break;          /* à la volée */ 
        case ExcludeRoot: drapeaux = 0; break;
        case EntireChain: drapeaux = 0; break;
        default: return 0;
    }
    /* X509_V_FLAG_CRL_CHECK (0x8) ; ALL 0x10 — absents BoringSSL : sans effet */
    (void)drapeaux;
    return 1;
}

/* Store racines : SSL_CERT_DIR override puis cacerts système Android. */
PAL const char* CryptoNative_GetX509RootStorePath(uint8_t* defaultPath)
{
    const char* dir;
    if (defaultPath == NULL) return NULL;
    dir = getenv("SSL_CERT_DIR");
    *defaultPath = 0;
    if (dir != NULL && dir[0] != '\0') return dir;
    /* /apex/com.android.conscrypt/cacerts (Android 14+) puis cacerts 9-13. */
    if (access("/apex/com.android.conscrypt/cacerts", R_OK) == 0)
        dir = "/apex/com.android.conscrypt/cacerts";
    else
        dir = "/system/etc/security/cacerts";
    *defaultPath = 1;
    return dir;
}

/* Aucun fichier concaténé sur Android : géré saute le RootStoreFile (NULL). */
PAL const char* CryptoNative_GetX509RootStoreFile(uint8_t* defaultPath)
{
    const char* file;
    if (defaultPath == NULL) return NULL;
    file = getenv("SSL_CERT_FILE");
    *defaultPath = 0;
    if (file != NULL && file[0] != '\0') return file;
    return NULL;
}

/* pal : 1 = algorithme connu et estampillé disponible ; BoringSSL couvre les
 * signatures courantes (sha256WithRSA/ECDSA pour nos chemins) → 1 toujours. */
PAL int32_t CryptoNative_IsSignatureAlgorithmAvailable(const char* algorithm)
{
    (void)algorithm;
    return 1;
}

/* ---------- Piles X509 (sk_* génériques : les macros sk_X509_* du BoringSSL) ---------- */

PAL STACK* CryptoNative_NewX509Stack(void)
{
    if (bssl.sk_new_null == NULL) return NULL;
    return bssl.sk_new_null();
}

PAL int32_t CryptoNative_PushX509StackField(STACK* stack, X509* x509)
{
    if (stack == NULL || x509 == NULL || bssl.sk_push == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.sk_push(stack, (void*)x509);
}

PAL X509* CryptoNative_GetX509StackField(STACK* stack, int loc)
{
    if (stack == NULL || bssl.sk_value == NULL) return NULL;
    return (X509*)bssl.sk_value(stack, loc);
}

PAL int32_t CryptoNative_GetX509StackFieldCount(STACK* stack)
{
    if (stack == NULL || bssl.sk_num == NULL) return 0;
    return bssl.sk_num(stack);
}

PAL void CryptoNative_RecursiveFreeX509Stack(STACK* stack)
{
    if (stack != NULL && bssl.sk_pop_free) bssl.sk_pop_free(stack, (void (*)(void*))bssl.x509_free);
}

/* Up-ref + push pour chaque cert source (pal StackAddMultiple). */
PAL int32_t CryptoNative_X509StackAddMultiple(STACK* dest, STACK* src)
{
    int nombre;
    int i;
    int succes = 1;
    if (dest == NULL) return -1;
    if (src == NULL || bssl.sk_num == NULL) return 1;
    nombre = bssl.sk_num(src);
    if (bssl.err_clear) bssl.err_clear();
    for (i = 0; i < nombre; i++)
    {
        X509* cert = (X509*)bssl.sk_value(src, i);
        bssl.x509_up_ref(cert);
        if (!bssl.sk_push(dest, cert)) { succes = 0; bssl.x509_free(cert); break; }
    }
    return succes;
}

/* Adaption Android : le pal lit les fichiers .pfx ; le store système Android est
 * un répertoire de PEM. On pousse tous les certificats PEM (pal devie sur pfx
 * uniquement car le géré lit les répertoires par lui-même) — les PEM déchiffrés
 * par le lecteur géré proviennent déjà (OpenSslCachedSystemStoreProvider). */

/* Chemin « répertoire/fichier » joint dans chemin ; 0 si tronqué/hors norme. */
static int chemin_dossier(char* chemin, size_t cap, const char* storePath, const char* nom)
{
    int taille = (int)strlen(storePath);
    int maj = snprintf(chemin, cap, "%s%s%s", storePath,
                       taille > 0 && storePath[taille - 1] == '/' ? "" : "/",
                       nom);
    return maj > 1 && maj < (int)cap ? maj : 0;
}

/* Un fichier PEM peut contenir plusieurs certificats : lecture séquentielle ;
 * chaque cert est poussé (référence possédée par la pile) ou libéré (revue). */
static void stack_dir_pousser_fichier(const char* chemin, STACK* stack)
{
    BIO* fichier = bssl.bio_new_file(chemin, "r");
    X509* cert;
    if (fichier == NULL)
    {
        if (bssl.err_clear) bssl.err_clear();
        return;
    }
    while ((cert = bssl.pem_read_bio_X509(fichier, NULL, NULL, NULL)) != NULL)
    {
        if (!bssl.sk_push(stack, cert)) bssl.x509_free(cert);
    }
    bssl.bio_free(fichier);
    if (bssl.err_clear) bssl.err_clear();
}

PAL int32_t CryptoNative_X509StackAddDirectoryStore(STACK* stack, char* storePath)
{
    DIR* dir;
    struct dirent* entree;
    if (stack == NULL || storePath == NULL) return -1;
    if (bssl.err_clear) bssl.err_clear();
    dir = opendir(storePath);
    if (dir == NULL) return 0;
    while ((entree = readdir(dir)) != NULL)
    {
        char chemin[512];
        if (chemin_dossier(chemin, sizeof(chemin), storePath, entree->d_name) > 0)
            stack_dir_pousser_fichier(chemin, stack);
    }
    closedir(dir);
    return 1;
}

/* Stack de noms (GetX509NameStackField*) */
PAL int32_t CryptoNative_GetX509NameStackFieldCount(STACK* sk)
{
    if (sk == NULL || bssl.sk_num == NULL) return 0;
    return bssl.sk_num(sk);
}

PAL X509_NAME* CryptoNative_GetX509NameStackField(STACK* sk, int32_t loc)
{
    if (sk == NULL || bssl.sk_value == NULL) return NULL;
    return (X509_NAME*)bssl.sk_value(sk, loc);
}

/* ---------- Store + chaîne (pal_x509.c) ---------- */

/* X509ChainNew : store neuf ; remplissage trust séparé des racines/utilisateur.
 * tolerant=0 : échec fatal (systemTrust) ; 1 : doublons datés effacés (pal). */
static int store_chaine_ajouter(X509_STORE* store, STACK* stack, int tolerant)
{
    int i;
    int nombre;
    if (stack == NULL) return 1;
    nombre = bssl.sk_num(stack);
    for (i = 0; i < nombre; i++)
    {
        if (bssl.store_add_cert(store, (X509*)bssl.sk_value(stack, i))) continue;
        if (tolerant) { if (bssl.err_clear) bssl.err_clear(); continue; }
        return 0;
    }
    return 1;
}

/* X509ChainNew : store neuf = systemTrust (fatale) + userTrust (doublons OK). */
PAL X509_STORE* CryptoNative_X509ChainNew(STACK* systemTrust, STACK* userTrust)
{
    X509_STORE* store;
    if (bssl.store_new == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    store = bssl.store_new();
    if (store == NULL) return NULL;
    if (!store_chaine_ajouter(store, systemTrust, 0))
    {
        bssl.store_free(store);
        return NULL;
    }
    store_chaine_ajouter(store, userTrust, 1);
    return store;
}

/* Verrou global : X509_STORE_CTX_init(ctx, store, x, churn) ; capture d'état pour Reset. */
PAL int32_t CryptoNative_X509StoreCtxInit(X509_STORE_CTX* ctx, X509_STORE* store, X509* x509, STACK* extras)
{
    int ret;
    etat_ctx_t* etat;
    if (ctx == NULL || bssl.store_ctx_init == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    ret = bssl.store_ctx_init(ctx, store, x509, extras);
    etat = etat_ctx_associer(ctx);
    if (etat != NULL)
    {
        etat->store = store;
        etat->cert = x509;
        etat->untrusted = extras;
    }
    return ret;
}

PAL X509_STORE_CTX* CryptoNative_X509StoreCtxCreate(void)
{
    if (bssl.store_ctx_new == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.store_ctx_new();
}

/* Libérées : le registre état purge le ctx après la free boring. */
PAL void CryptoNative_X509StoreCtxDestroy(X509_STORE_CTX* v)
{
    if (v != NULL)
    {
        etat_ctx_retirer(v);
        bssl.store_ctx_free(v);
    }
}

PAL int32_t CryptoNative_X509VerifyCert(X509_STORE_CTX* ctx)
{
    if (ctx == NULL || bssl.x509_verify_cert == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.x509_verify_cert(ctx);
}

PAL STACK* CryptoNative_X509StoreCtxGetChain(X509_STORE_CTX* ctx)
{
    if (ctx == NULL || bssl.store_ctx_get1_chain == NULL) return NULL;
    return bssl.store_ctx_get1_chain(ctx);
}

PAL void* CryptoNative_X509StoreCtxGetCurrentCert(X509_STORE_CTX* ctx)
{
    if (ctx == NULL || bssl.store_ctx_get_current_cert == NULL) return NULL;
    return bssl.store_ctx_get_current_cert(ctx);
}

/* Interior pointer vers la collection "untrusted" (pal get0_untrusted). */
PAL STACK* CryptoNative_X509StoreCtxGetSharedUntrusted(X509_STORE_CTX* ctx)
{
    if (ctx == NULL || bssl.store_ctx_get0_untrusted == NULL) return NULL;
    return bssl.store_ctx_get0_untrusted(ctx);
}

PAL int32_t CryptoNative_X509StoreCtxGetError(X509_STORE_CTX* ctx)
{
    if (ctx == NULL || bssl.store_ctx_get_error == NULL) return 0;
    return bssl.store_ctx_get_error(ctx);
}

PAL int32_t CryptoNative_X509StoreCtxGetErrorDepth(X509_STORE_CTX* ctx)
{
    if (ctx == NULL || bssl.store_ctx_get_error_depth == NULL) return 0;
    return bssl.store_ctx_get_error_depth(ctx);
}

/* BoringSSL : pas de set_app_data (sonde) — registre idx. Set callback passe le
 * fnptr géré directement à boring (le géré délègue stockant l'appData via nous). */
PAL int32_t CryptoNative_X509StoreCtxSetVerifyCallback(X509_STORE_CTX* ctx, X509StoreVerifyCallback callback, void* appData)
{
    etat_ctx_t* etat;
    if (ctx == NULL || bssl.store_ctx_set_verify_cb == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    bssl.store_ctx_set_verify_cb(ctx, callback);
    etat = etat_ctx_associer(ctx);
    if (etat == NULL) return 0;
    etat->callback = callback;
    etat->appData = appData;
    return 1;
}

PAL void* CryptoNative_X509StoreCtxGetAppData(X509_STORE_CTX* ctx)
{
    etat_ctx_t* etat = ctx != NULL ? etat_ctx_chercher(ctx) : NULL;
    return etat != NULL ? etat->appData : NULL;
}

/* miroir pal : X509_verify_cert_error_string. */
PAL const char* CryptoNative_X509VerifyCertErrorString(int32_t n)
{
    if (bssl.x509_verify_cert_error_string == NULL) return "unknown";
    return bssl.x509_verify_cert_error_string((long)n);
}

/* Appel pour verifier la chaine du stack ctx (cleanup absent de notre sonde:
 * X509_STORE_CTX_cleanup OK? absent du TO probe -> pas de fns) */
PAL int32_t CryptoNative_X509StoreCtxReset(X509_STORE_CTX* ctx)
{
    X509* leaf;
    STACK* untrusted;
    X509_STORE* store;
    int virt;
    virt = ctx != NULL;
    if (!virt || bssl.store_ctx_get0_cert == NULL) return 0;
    leaf = (X509*)bssl.store_ctx_get0_cert(ctx);
    untrusted = bssl.store_ctx_get0_untrusted(ctx);
    store = (X509_STORE*)reg_get_secondaire(ctx);
    if (store == NULL)
    {
        etat_ctx_t* etat = etat_ctx_chercher(ctx);
        store = etat != NULL ? etat->store : NULL;
    }
    return CryptoNative_X509StoreCtxInit(ctx, store, leaf, untrusted) == 1 ? 1 : 0;
}

PAL int32_t CryptoNative_X509StoreCtxRebuildChain(X509_STORE_CTX* ctx)
{
    int reset;
    if (ctx == NULL) return -1;
    reset = CryptoNative_X509StoreCtxReset(ctx);
    if (reset != 1) return -1;
    return CryptoNative_X509VerifyCert(ctx);
}

/* CommitToChain : les entrées é pingées (miroir pal complet). */
PAL int32_t CryptoNative_X509StoreCtxCommitToChain(X509_STORE_CTX* storeCtx)
{
    STACK* chaine;
    STACK* untrusted;
    void* feuille;
    void* curs;
    if (storeCtx == NULL) return -1;
    if (bssl.err_clear) bssl.err_clear();
    chaine = bssl.store_ctx_get1_chain(storeCtx);
    if (chaine == NULL) return 0;
    untrusted = bssl.store_ctx_get0_untrusted(storeCtx);
    feuille = bssl.store_ctx_get0_cert(storeCtx);
    if (untrusted == NULL) { bssl.sk_free(chaine); return 0; }
    while ((curs = bssl.sk_pop(untrusted)) != NULL) bssl.x509_free(curs);
    while ((curs = bssl.sk_pop(chaine)) != NULL)
    {
        if (curs == feuille) bssl.x509_free(curs);    /* refcount get1_chain */
        else if (!bssl.sk_push(untrusted, curs))
        {
            bssl.x509_free(curs);
            bssl.sk_pop_free(chaine, (void (*)(void*))bssl.x509_free);
            return 0;
        }
    }
    bssl.sk_free(chaine);
    return 1;
}

/* Simplification assumée : pas de reconstruction « alternate trust » — le géré
 * valide les erreurs de signature depuis la chaine existante ; TMDB certificats
 * signés valides ne franchissent pas ce chemin. */
PAL int32_t CryptoNative_X509StoreCtxResetForSignatureError(X509_STORE_CTX* storeCtx, X509_STORE** newStore)
{
    if (storeCtx == NULL || newStore == NULL) return -1;
    *newStore = NULL;
    return 1;
}

/* Revue : conversion UTC explicite (les champs passé sont UTC — ASN.1 UTCTime),
 * pas de DST, pas de fuseau ; le 29 février est couvert par arithmétique
 * bissextile (le tableau statique tronquait février à 28 jours → dérive 24 h
 * une année sur quatre). */
static int annee_est_bissextile(long annee)
{
    return (annee % 4 == 0 && annee % 100 != 0) || (annee % 400 == 0);
}

static long heure_utc_vers_epoch(int32_t annee, int32_t mois, int32_t jour, int32_t heure, int32_t minute, int32_t seconde)
{
    /* jours jusqu'au mois m (hors février bissextile, ajouté ensuite) */
    static const int jours_mois[] = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
    long annee2 = annee;
    long jours;
    int m;
    if (annee < 1970) return -1;
    if (mois < 1 || mois > 12) return -1;
    annee2 -= 1900;
    jours = 365L * (annee2 - 70) + ((annee2 - 69) / 4) - ((annee2 - 69) / 100) + ((annee2 + 299) / 400);
    for (m = 0; m < mois - 1; m++)
        jours += jours_mois[m];
    if (mois > 2 && annee_est_bissextile(annee2 + 1900)) jours++;
    jours += jour - 1;
    return jours * 86400L + heure * 3600L + minute * 60L + seconde;
}

/* Pal : Well par met (param time). */
PAL int32_t CryptoNative_X509StoreSetVerifyTime(X509_STORE* ctx, int32_t year, int32_t month, int32_t day, int32_t hour, int32_t minute, int32_t second, int32_t isDst)
{
    X509_VERIFY_PARAM* parametres;
    long ep;
    (void)isDst;
    if (ctx == NULL) return 0;
    if (bssl.store_get0_param == NULL || bssl.vp_set_time == NULL) return 1;
    ep = heure_utc_vers_epoch(year, month, day, hour, minute, second);
    if (ep < 0) return 0;
    parametres = bssl.store_get0_param(ctx);
    if (parametres == NULL) return 0;
    bssl.vp_set_time(parametres, ep);
    return 1;
}

/* ---------- X509_CRL pal trio (BoringSSL réels) ---------- */

PAL X509_CRL* CryptoNative_DecodeX509Crl(const uint8_t* buf, int32_t len)
{
    if (buf == NULL || len == 0 || bssl.d2i_x509_crl == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    {
        const unsigned char* cif = buf;
        return bssl.d2i_x509_crl(NULL, &cif, (long)len);
    }
}

PAL void CryptoNative_X509CrlDestroy(X509_CRL* a)
{
    if (a != NULL && bssl.x509_crl_free) bssl.x509_crl_free(a);
}

PAL int32_t CryptoNative_PemWriteBioX509Crl(BIO* bio, X509_CRL* crl)
{
    if (bio == NULL || crl == NULL || bssl.pem_write_bio_X509_CRL == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.pem_write_bio_X509_CRL(bio, crl);
}

PAL X509_CRL* CryptoNative_PemReadBioX509Crl(BIO* bio)
{
    if (bio == NULL || bssl.pem_read_bio_X509_CRL == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.pem_read_bio_X509_CRL(bio, NULL, NULL, NULL);
}

/* ---------- Chiffreurs EVP (EVP_CIPHER_* BoringSSL : AES/DES3 uniquement) ---------- */

/* pal EvpCipherCreate2 : ctx neuf, init partielle, longueur clé puis init finale. */
PAL EVP_CIPHER_CTX* CryptoNative_EvpCipherCreate2(const EVP_CIPHER* type, uint8_t* key, int32_t keyLength, unsigned char* iv, int32_t enc)
{
    EVP_CIPHER_CTX* ctx;
    int ok;
    if (type == NULL || bssl.cipher_ctx_new == NULL || bssl.cipher_init_ex == NULL) return NULL;
    if (keyLength < 0 || (keyLength > 0 && key == NULL)) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    ctx = bssl.cipher_ctx_new();
    if (ctx == NULL) return NULL;
    ok = bssl.cipher_init_ex(ctx, type, NULL, NULL, NULL, 0);
    if (ok != 1) { bssl.cipher_ctx_free(ctx); return NULL; }
    ok = bssl.cipher_init_ex(ctx, NULL, NULL, key, iv, enc);
    if (ok != 1) { bssl.cipher_ctx_free(ctx); return NULL; }
    return ctx;
}

/* pal EvpCipherCreatePartial : init partielle seulement (cipher sans clé). */
PAL EVP_CIPHER_CTX* CryptoNative_EvpCipherCreatePartial(const EVP_CIPHER* type)
{
    EVP_CIPHER_CTX* ctx;
    int ok;
    if (type == NULL || bssl.cipher_ctx_new == NULL || bssl.cipher_init_ex == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    ctx = bssl.cipher_ctx_new();
    if (ctx == NULL) return NULL;
    ok = bssl.cipher_init_ex(ctx, type, NULL, NULL, NULL, 0);
    if (ok != 1) { bssl.cipher_ctx_free(ctx); return NULL; }
    return ctx;
}

PAL int32_t CryptoNative_EvpCipherSetKeyAndIV(EVP_CIPHER_CTX* ctx, uint8_t* key, unsigned char* iv, int32_t enc)
{
    if (ctx == NULL || bssl.cipher_init_ex == NULL) return PAL_ECHEC;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.cipher_init_ex(ctx, NULL, NULL, key, iv, enc);
}

PAL int32_t CryptoNative_EvpCipherSetGcmNonceLength(EVP_CIPHER_CTX* ctx, int32_t ivLength)
{
    if (ctx == NULL || ivLength < 0 || bssl.cipher_ctx_ctrl == NULL) return PAL_ECHEC;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.cipher_ctx_ctrl(ctx, CTRL_GCM_SET_IVLEN, ivLength, NULL);
}

PAL int32_t CryptoNative_EvpCipherSetCcmNonceLength(EVP_CIPHER_CTX* ctx, int32_t ivLength)
{
    /* CCM absent de BoringSSL (sonde) : échec propre. */
    (void)ctx; (void)ivLength;
    return PAL_ECHEC;
}

PAL void CryptoNative_EvpCipherDestroy(EVP_CIPHER_CTX* ctx)
{
    if (ctx != NULL && bssl.cipher_ctx_free) bssl.cipher_ctx_free(ctx);
}

/* pal : re-init avec iv (direction conservée) */
PAL int32_t CryptoNative_EvpCipherReset(EVP_CIPHER_CTX* ctx, uint8_t* pIv, int32_t cIv)
{
    (void)cIv;
    if (ctx == NULL || bssl.cipher_init_ex == NULL) return PAL_ECHEC;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.cipher_init_ex(ctx, NULL, NULL, NULL, pIv, -1);
}

PAL int32_t CryptoNative_EvpCipherCtxSetPadding(EVP_CIPHER_CTX* x, int32_t padding)
{
    if (x == NULL || bssl.cipher_ctx_set_padding == NULL) return PAL_ECHEC;
    return bssl.cipher_ctx_set_padding(x, padding);
}

PAL int32_t CryptoNative_EvpCipherUpdate(EVP_CIPHER_CTX* ctx, uint8_t* out, int32_t* outl, unsigned char* in, int32_t inl)
{
    int longueur = 0;
    int ret;
    if (ctx == NULL || out == NULL || outl == NULL || inl < 0 || (inl > 0 && in == NULL) || bssl.cipher_update == NULL)
        return PAL_ECHEC;
    if (bssl.err_clear) bssl.err_clear();
    ret = bssl.cipher_update(ctx, out, &longueur, inl > 0 ? in : (const unsigned char*)"", inl);
    *outl = longueur;
    return ret;
}

PAL int32_t CryptoNative_EvpCipherFinalEx(EVP_CIPHER_CTX* ctx, uint8_t* outm, int32_t* outl)
{
    int longueur = 0;
    int ret;
    if (ctx == NULL || outm == NULL || outl == NULL || bssl.cipher_final_ex == NULL) return PAL_ECHEC;
    if (bssl.err_clear) bssl.err_clear();
    ret = bssl.cipher_final_ex(ctx, outm, &longueur);
    *outl = longueur;
    return ret;
}

/* CTRL_AEAD_GET_TAG = 16, set = 17 (identiques BoringSSL/OpenSSL). */
PAL int32_t CryptoNative_EvpCipherGetGcmTag(EVP_CIPHER_CTX* ctx, uint8_t* tag, int32_t tagLength)
{
    if (ctx == NULL || tag == NULL || tagLength <= 0 || bssl.cipher_ctx_ctrl == NULL) return PAL_ECHEC;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.cipher_ctx_ctrl(ctx, CTRL_GCM_GET_TAG, tagLength, (void*)tag);
}

PAL int32_t CryptoNative_EvpCipherSetGcmTag(EVP_CIPHER_CTX* ctx, uint8_t* tag, int32_t tagLength)
{
    if (ctx == NULL || tag == NULL || tagLength <= 0 || bssl.cipher_ctx_ctrl == NULL) return PAL_ECHEC;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.cipher_ctx_ctrl(ctx, CTRL_GCM_SET_TAG, tagLength, (void*)tag);
}

/* BoringSSL : chacha20-poly1305 EVP absent (sonde) — pas de tag générique AEAD. */
PAL int32_t CryptoNative_EvpCipherGetAeadTag(EVP_CIPHER_CTX* ctx, uint8_t* tag, int32_t tagLength)
{
    (void)ctx; (void)tag; (void)tagLength;
    return PAL_ECHEC;
}

PAL int32_t CryptoNative_EvpCipherSetAeadTag(EVP_CIPHER_CTX* ctx, uint8_t* tag, int32_t tagLength)
{
    (void)ctx; (void)tag; (void)tagLength;
    return PAL_ECHEC;
}

PAL int32_t CryptoNative_EvpCipherGetCcmTag(EVP_CIPHER_CTX* ctx, uint8_t* tag, int32_t tagLength)
{
    (void)ctx; (void)tag; (void)tagLength;
    return PAL_ECHEC;
}

PAL int32_t CryptoNative_EvpCipherSetCcmTag(EVP_CIPHER_CTX* ctx, uint8_t* tag, int32_t tagLength)
{
    (void)ctx; (void)tag; (void)tagLength;
    return PAL_ECHEC;
}

/* Getters de suites : BoringSSL OK (sonde : aes_128/192/256 ecb/cbc/gcm, des, des_ede3, rc2_cbc) ;
 * cfb/ccm/rc2_ecb/chacha absents → NULL (CryptographicException gérée). */
PAL const EVP_CIPHER* CryptoNative_EvpAes128Ecb(void) { return bssl.evp_aes_128_ecb ? bssl.evp_aes_128_ecb() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes128Cbc(void) { return bssl.evp_aes_128_cbc ? bssl.evp_aes_128_cbc() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes128Cfb8(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes128Cfb128(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes128Gcm(void) { return bssl.evp_aes_128_gcm ? bssl.evp_aes_128_gcm() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes128Ccm(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes192Ecb(void) { return bssl.evp_aes_192_ecb ? bssl.evp_aes_192_ecb() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes192Cbc(void) { return bssl.evp_aes_192_cbc ? bssl.evp_aes_192_cbc() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes192Cfb8(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes192Cfb128(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes192Gcm(void) { return bssl.evp_aes_192_gcm ? bssl.evp_aes_192_gcm() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes192Ccm(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes256Ecb(void) { return bssl.evp_aes_256_ecb ? bssl.evp_aes_256_ecb() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes256Cbc(void) { return bssl.evp_aes_256_cbc ? bssl.evp_aes_256_cbc() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes256Cfb8(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes256Cfb128(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes256Gcm(void) { return bssl.evp_aes_256_gcm ? bssl.evp_aes_256_gcm() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpAes256Ccm(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpDesEcb(void) { return bssl.evp_des_ecb ? bssl.evp_des_ecb() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpDesCbc(void) { return bssl.evp_des_cbc ? bssl.evp_des_cbc() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpDesCfb8(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpDes3Ecb(void) { return bssl.evp_des_ede3_ecb ? bssl.evp_des_ede3_ecb() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpDes3Cbc(void) { return bssl.evp_des_ede3_cbc ? bssl.evp_des_ede3_cbc() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpDes3Cfb8(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpDes3Cfb64(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpRC2Ecb(void) { return NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpRC2Cbc(void) { return bssl.evp_rc2_cbc ? bssl.evp_rc2_cbc() : NULL; }
PAL const EVP_CIPHER* CryptoNative_EvpChaCha20Poly1305(void) { return NULL; }

/* suites TLS 1.3 : défauts BoringSSL (set_ciphersuites absent — sonde) ; retour 1. */
/* Miroir pal exact : cipherList/cipherSuites NULL = garder les défauts (retour 1) ;
 * suites TLS 1.3: set_ciphersuites optionnel (absent du BoringSSL Android (sonde)). */
PAL int32_t CryptoNative_SslCtxSetCiphers(SSL_CTX* ctx, const char* cipherList, const char* cipherSuites)
{
    int ret = 1;
    if (ctx == NULL) return 0;
    if (cipherList != NULL)
    {
        if (bssl.ssl_ctx_set_cipher_list == NULL) return 0;
        if (bssl.err_clear) bssl.err_clear();
        if (!bssl.ssl_ctx_set_cipher_list(ctx, cipherList)) return 0;
    }
    if (cipherSuites != NULL && bssl.ssl_ctx_set_ciphersuites != NULL)
    {
        if (bssl.err_clear) bssl.err_clear();
        ret &= bssl.ssl_ctx_set_ciphersuites(ctx, cipherSuites);
    }
    return ret;
}

PAL int32_t CryptoNative_SetCiphers(SSL* ssl, const char* cipherList, const char* cipherSuites)
{
    int ret = 1;
    if (ssl == NULL) return 0;
    if (cipherList != NULL)
    {
        if (bssl.ssl_set_cipher_list == NULL) return 0;
        if (bssl.err_clear) bssl.err_clear();
        if (!bssl.ssl_set_cipher_list(ssl, cipherList)) return 0;
    }
    if (cipherSuites != NULL && bssl.ssl_set_ciphersuites != NULL)
    {
        if (bssl.err_clear) bssl.err_clear();
        ret &= bssl.ssl_set_ciphersuites(ssl, cipherSuites);
    }
    return ret;
}

/* ---------- SSL : cycles client TLS (miroir pal_ssl.c) ---------- */
PAL int32_t CryptoNative_SslGetError(SSL* ssl, int32_t ret);

/* TLS_method si présent (clients ET serveurs pour GetDefaultSignatureAlgorithms),
 * sinon TLS_client_method. */
PAL const SSL_METHOD* CryptoNative_SslV2_3Method(void)
{
    if (bssl.tls_method != NULL) return bssl.tls_method();
    return bssl.tls_client_method();
}

PAL SSL_CTX* CryptoNative_SslCtxCreate(const SSL_METHOD* method)
{
    SSL_CTX* ctx;
    if (pret() == 0 || bssl.ssl_ctx_new == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    ctx = bssl.ssl_ctx_new(method);
    return ctx;
}

PAL void CryptoNative_SslCtxDestroy(SSL_CTX* ctx)
{
    if (ctx != NULL)
    {
        reg_del(ctx);
        bssl.ssl_ctx_free(ctx);
    }
}

/* miroir pal : réguler via SSL_CTX_set_{min,max}_proto_version (BoringSSL réels). */
PAL void CryptoNative_SslCtxSetProtocolOptions(SSL_CTX* ctx, SslProtocols_e protocols)
{
    unsigned int mini = 0;
    unsigned int maxi = 0;
    int premier = 1;
    if (ctx == NULL || bssl.ssl_ctx_set_min_proto_version == NULL) return;
    if (protocols == 0) return;
    #define OU_INCLUSTER(task_bits, ver) \
        if ((protocols & (task_bits)) == (task_bits)) \
        { \
            if (premier) { mini = ver; maxi = ver; premier = 0; } \
            else { if (ver < mini) mini = ver; if (ver > maxi) maxi = ver; } \
        }
    OU_INCLUSTER(PAL_SSL_SSL3, VERSION_SSL3_0)
    OU_INCLUSTER(PAL_SSL_TLS, VERSION_TLS1_0)
    OU_INCLUSTER(PAL_SSL_TLS11, VERSION_TLS1_1)
    OU_INCLUSTER(PAL_SSL_TLS12, VERSION_TLS1_2)
    OU_INCLUSTER(PAL_SSL_TLS13, VERSION_TLS1_3)
    #undef OU_INCLUSTER
    bssl.ssl_ctx_set_min_proto_version(ctx, mini);
    bssl.ssl_ctx_set_max_proto_version(ctx, maxi);
}

/* miroir pal : politique de chiffrement — Require/Allow/NoEncryption acceptent. */
PAL int32_t CryptoNative_SslCtxSetEncryptionPolicy(SSL_CTX* ctx, EncryptionPolicy_e policy)
{
    (void)ctx;
    return (policy == POLICE_REQUIRE || policy == POLICE_ALLOW_NO_ENC || policy == POLICE_NO_ENC) ? 1 : 0;
}

PAL SSL* CryptoNative_SslCreate(SSL_CTX* ctx)
{
    SSL* ssl;
    if (pret() == 0 || bssl.ssl_new == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    ssl = bssl.ssl_new(ctx);
    if (ssl != NULL && bssl.ssl_set_tlsext_status_type != NULL)
    {
        /* opportuniste : demander le stapled OCSP (TLSEXT_STATUSTYPE_ocsp = 1) */
        if (bssl.ssl_set_tlsext_status_type(ssl, 1) != 1)
        {
            if (bssl.err_clear) bssl.err_clear();
        }
    }
    return ssl;
}

PAL void CryptoNative_SslDestroy(SSL* ssl)
{
    if (ssl != NULL)
    {
        reg_del(ssl);
        bssl.ssl_free(ssl);
    }
}

PAL void CryptoNative_SslSetConnectState(SSL* ssl) { if (ssl != NULL) bssl.ssl_set_connect_state(ssl); }
PAL void CryptoNative_SslSetAcceptState(SSL* ssl) { if (ssl != NULL) bssl.ssl_set_accept_state(ssl); }
PAL const char* CryptoNative_SslGetVersion(SSL* ssl) { return (ssl != NULL && bssl.ssl_get_version) ? bssl.ssl_get_version(ssl) : NULL; }

PAL int32_t CryptoNative_SslWrite(SSL* ssl, const void* buf, int32_t num, int32_t* error)
{
    int32_t resultat;
    if (ssl == NULL || buf == NULL || num < 0 || error == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    resultat = num == 0 ? 0 : bssl.ssl_write(ssl, buf, num);
    if (resultat > 0) *error = SSL_ERROR_NONE;
    else *error = CryptoNative_SslGetError(ssl, resultat);
    return resultat;
}

PAL int32_t CryptoNative_SslRead(SSL* ssl, void* buf, int32_t num, int32_t* error)
{
    int32_t resultat;
    if (ssl == NULL || buf == NULL || num < 0 || error == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    resultat = num == 0 ? 0 : bssl.ssl_read(ssl, buf, num);
    if (resultat > 0) *error = SSL_ERROR_NONE;
    else *error = CryptoNative_SslGetError(ssl, resultat);
    return resultat;
}

PAL int32_t CryptoNative_SslGetError(SSL* ssl, int32_t ret)
{
    if (ssl == NULL || bssl.ssl_get_error == NULL) return SSL_ERROR_SSL;
    return bssl.ssl_get_error(ssl, ret);
}

PAL int32_t CryptoNative_SslGetFinished(SSL* ssl, void* buf, int32_t count)
{
    if (ssl == NULL || buf == NULL || count < 0 || bssl.ssl_get_finished == NULL) return 0;
    return (int32_t)bssl.ssl_get_finished(ssl, buf, (size_t)count);
}

PAL int32_t CryptoNative_SslGetPeerFinished(SSL* ssl, void* buf, int32_t count)
{
    if (ssl == NULL || buf == NULL || count < 0 || bssl.ssl_get_peer_finished == NULL) return 0;
    return (int32_t)bssl.ssl_get_peer_finished(ssl, buf, (size_t)count);
}

PAL int32_t CryptoNative_SslDoHandshake(SSL* ssl, int32_t* error)
{
    int32_t ret;
    if (ssl == NULL || error == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    ret = bssl.ssl_do_handshake(ssl);
    if (ret == 1) *error = SSL_ERROR_NONE;
    else *error = CryptoNative_SslGetError(ssl, ret);
    return ret;
}

PAL int32_t CryptoNative_IsSslStateOK(SSL* ssl)
{
    if (ssl == NULL || bssl.ssl_is_init_finished == NULL) return 0;
    return bssl.ssl_is_init_finished(ssl);
}

PAL int32_t CryptoNative_SslShutdown(SSL* ssl)
{
    if (ssl == NULL || bssl.ssl_shutdown == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.ssl_shutdown(ssl);
}

PAL void CryptoNative_SslSetBio(SSL* ssl, BIO* rbio, BIO* wbio)
{
    if (ssl != NULL) bssl.ssl_set_bio(ssl, rbio, wbio);
}

PAL void CryptoNative_SslSetVerifyPeer(SSL* ssl)
{
    if (ssl != NULL) bssl.ssl_set_verify(ssl, 1 /* SSL_VERIFY_PEER */, NULL);
}

PAL int32_t CryptoNative_SslRenegotiate(SSL* ssl, int32_t* error)
{
    int ret;
    if (ssl == NULL || error == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    if (bssl.ssl_renegotiate == NULL) { *error = SSL_ERROR_SSL; return 0; }
    ret = bssl.ssl_renegotiate(ssl);
    *error = CryptoNative_SslGetError(ssl, ret);
    return ret;
}

PAL int32_t CryptoNative_IsSslRenegotiatePending(SSL* ssl)
{
    if (ssl == NULL || bssl.ssl_renegotiate_pending == NULL) return 0;
    if (bssl.ssl_peek && bssl.ssl_peek(ssl, NULL, 0) < 0) { if (bssl.err_clear) bssl.err_clear(); }
    return bssl.ssl_renegotiate_pending(ssl) != 0 ? 1 : 0;
}

PAL X509* CryptoNative_SslGetPeerCertificate(SSL* ssl)
{
    if (ssl == NULL || bssl.ssl_get_peer_certificate == NULL) return NULL;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.ssl_get_peer_certificate(ssl);
}

PAL X509* CryptoNative_SslGetCertificate(SSL* ssl)
{
    if (ssl == NULL || bssl.ssl_get_certificate == NULL) return NULL;
    return bssl.ssl_get_certificate(ssl);
}

PAL STACK* CryptoNative_SslGetPeerCertChain(SSL* ssl)
{
    if (ssl == NULL || bssl.ssl_get_peer_cert_chain == NULL) return NULL;
    return bssl.ssl_get_peer_cert_chain(ssl);
}

PAL int32_t CryptoNative_SslUseCertificate(SSL* ssl, X509* x)
{
    if (ssl == NULL || x == NULL || bssl.ssl_use_certificate == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.ssl_use_certificate(ssl, x);
}

PAL int32_t CryptoNative_SslUsePrivateKey(SSL* ssl, EVP_PKEY* pkey)
{
    if (ssl == NULL || pkey == NULL || bssl.ssl_use_private_key == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.ssl_use_private_key(ssl, pkey);
}

PAL int32_t CryptoNative_SslCtxUseCertificate(SSL_CTX* ctx, X509* x)
{
    if (ctx == NULL || x == NULL || bssl.ssl_ctx_use_certificate == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.ssl_ctx_use_certificate(ctx, x);
}

PAL int32_t CryptoNative_SslCtxUsePrivateKey(SSL_CTX* ctx, EVP_PKEY* pkey)
{
    if (ctx == NULL || pkey == NULL || bssl.ssl_ctx_use_private_key == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.ssl_ctx_use_private_key(ctx, pkey);
}

PAL int32_t CryptoNative_SslCtxCheckPrivateKey(SSL_CTX* ctx)
{
    if (ctx == NULL || bssl.ssl_ctx_check_private_key == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.ssl_ctx_check_private_key(ctx);
}

PAL void CryptoNative_SslCtxSetQuietShutdown(SSL_CTX* ctx)
{
    if (ctx == NULL) return;
    /* pal : la valeur SSL_CTX_set_quiet_shutdown(ctx, 1) — val 1 applquée */
    { void (*fn)(SSL_CTX*, int) = (void (*)(SSL_CTX*, int))dlsym(g_libssl, "SSL_CTX_set_quiet_shutdown");
      if (fn != NULL) fn(ctx, 1); }
}

PAL void CryptoNative_SslSetQuietShutdown(SSL* ssl, int mode)
{
    if (ssl == NULL) return;
    { void (*fn)(SSL*, int) = (void (*)(SSL*, int))dlsym(g_libssl, "SSL_set_quiet_shutdown");
      if (fn != NULL) fn(ssl, mode); }
}

PAL STACK* CryptoNative_SslGetClientCAList(SSL* ssl)
{
    if (ssl == NULL || bssl.ssl_get_client_CA_list == NULL) return NULL;
    return bssl.ssl_get_client_CA_list(ssl);
}

/* ex_data : registre (pal ex 0). */
PAL int32_t CryptoNative_SslSetData(SSL* ssl, void* ptr)
{
    if (ssl == NULL) return 0;
    reg_set(ssl, ptr, NULL);
    return 1;
}

PAL void* CryptoNative_SslGetData(SSL* ssl) { return ssl != NULL ? reg_get_valeur(ssl) : NULL; }

PAL int32_t CryptoNative_SslCtxSetData(SSL_CTX* ctx, void* ptr)
{
    if (ctx == NULL) return 0;
    reg_set(ctx, ptr, NULL);
    return 1;
}

PAL void* CryptoNative_SslCtxGetData(SSL_CTX* ctx) { return ctx != NULL ? reg_get_valeur(ctx) : NULL; }

/* pal : SNI = SSL_get_servername(ssl, TLSEXT_NAMETYPE_host_name = 0). */
PAL const char* CryptoNative_SslGetServerName(SSL* ssl)
{
    if (ssl == NULL || bssl.ssl_get_servername == NULL) return NULL;
    return bssl.ssl_get_servername(ssl, 0);
}

PAL SSL_SESSION* CryptoNative_SslGetSession(SSL* ssl)
{
    return (ssl != NULL && bssl.ssl_get_session) ? bssl.ssl_get_session(ssl) : NULL;
}

PAL int32_t CryptoNative_SslSetSession(SSL* ssl, SSL_SESSION* session)
{
    if (ssl == NULL || bssl.ssl_set_session == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.ssl_set_session(ssl, session);
}

PAL void CryptoNative_SslSessionFree(SSL_SESSION* session)
{
    if (session != NULL)
    {
        etat_sess_retirer(session);
        bssl.ssl_session_free(session);
    }
}

/* BoringSSL Android : SSL_SESSION_get0_hostname absent (sonde) → état local. */
PAL const char* CryptoNative_SslSessionGetHostname(SSL_SESSION* session)
{
    etat_sess_t* e = session != NULL ? etat_sess_chercher(session) : NULL;
    return (e != NULL) ? e->hostname_alloc : NULL;
}

/* Contrat pal : retourne 0 toujours (pal_ssl.c release/10.0). */
PAL int32_t CryptoNative_SslSessionSetHostname(SSL_SESSION* session, const char* hostname)
{
    etat_sess_t* e;
    char* copie;
    if (session == NULL || hostname == NULL) return 0;
    copie = strdup(hostname);
    if (copie == NULL) return 0;
    e = etat_sess_associer(session);
    if (e != NULL)
    {
        free(e->hostname_alloc);
        e->hostname_alloc = copie;
        return 0;   /* miroir pal */
    }
    free(copie);
    return 0;
}

/* C1-revue : appData sur son propre champ — coexiste avec le hostname. */
PAL void CryptoNative_SslSessionSetData(SSL_SESSION* session, void* val)
{
    etat_sess_t* e;
    if (session == NULL) return;
    e = etat_sess_associer(session);
    if (e != NULL) e->app_data = val;
}

/* Requis par le géré en resumption TLS 1.3 (héritage du cert via session). */
PAL void* CryptoNative_SslSessionGetData(SSL_SESSION* session)
{
    etat_sess_t* e = session != NULL ? etat_sess_chercher(session) : NULL;
    return (e != NULL) ? e->app_data : NULL;
}

PAL int32_t CryptoNative_SslSessionReused(SSL* ssl)
{
    if (ssl == NULL || bssl.ssl_session_reused == NULL) return 0;
    return bssl.ssl_session_reused(ssl);
}

PAL int32_t CryptoNative_SslCtxRemoveSession(SSL_CTX* ctx, SSL_SESSION* session)
{
    if (ctx == NULL || session == NULL || bssl.ssl_ctx_remove_session == NULL) return 0;
    bssl.ssl_ctx_remove_session(ctx, session);
    return 0;
}

/* caching pal (M1-revue, miroir pal_ssl.c release/10.0 vérifié en ligne) :
 *   - mode != 0 → SSL_SESS_CACHE_BOTH (0x03 = CLIENT|SERVER), 0 → OFF ;
 *   - cacheSize >= 0 → SSL_CTX_sess_set_cache_size ;
 *   - session_id_context si fourni (plafond 32), indépendamment du mode ;
 *   - callbacks INSTALLÉS via les symboles dlopen'és quand fournis — le géré
 *     (Interop.OpenSsl.cs 10.0) passe NewSessionCallback/RemoveSessionCallback
 *     réels : sans installation, le cache SslSessionsCache géré resterait vide
 *     et la resumption serait inopérante ;
 *   - retour 1 (« willing to restore ») ; 0 si un callback demandé n'a pas pu
 *     être installé (symbole nul) — jamais de faux succès.
 * Divergence assumée vs pal : SSL_OP_NO_TICKET en mode OFF non posé (symbole
 * set_options absent de la table ; sans effet attendu pour un client BoringSSL
 * en OFF, tickets jamais émis). */
PAL int32_t CryptoNative_SslCtxSetCaching(SSL_CTX* ctx, int mode, int cacheSize, int contextIdLength, uint8_t* contextId, SslCtxNewSessionCallback cb_new, SslCtxRemoveSessionCallback cb_remove)
{
    if (ctx == NULL || bssl.ssl_ctx_set_session_cache_mode == NULL) return 0;
    bssl.ssl_ctx_set_session_cache_mode(ctx, mode != 0 ? 3 /*SSL_SESS_CACHE_BOTH*/ : 0 /*OFF*/);
    if (mode != 0 && cacheSize >= 0 && bssl.ssl_ctx_sess_set_cache_size != NULL)
        bssl.ssl_ctx_sess_set_cache_size(ctx, cacheSize);
    if (contextIdLength > 0 && contextId != NULL && bssl.ssl_ctx_set_session_id_context)
    {
        int longueur = contextIdLength > 32 ? 32 : contextIdLength;
        bssl.ssl_ctx_set_session_id_context(ctx, contextId, (unsigned int)longueur);
    }
    if (cb_new != NULL)
    {
        if (bssl.ssl_ctx_sess_set_new_cb == NULL) return 0;
        bssl.ssl_ctx_sess_set_new_cb(ctx, cb_new);
    }
    if (cb_remove != NULL)
    {
        if (bssl.ssl_ctx_sess_set_remove_cb == NULL) return 0;
        bssl.ssl_ctx_sess_set_remove_cb(ctx, cb_remove);
    }
    return 1;
}

/* callbacks keylog/ALPN/sess : direct boring (fonctions présentes — sonde). */
PAL void CryptoNative_SslCtxSetKeylogCallback(SSL_CTX* ctx, void* callback)
{
    if (ctx == NULL || callback == NULL) return;
    { int (*fn)(SSL_CTX*, void*) = (int (*)(SSL_CTX*, void*))dlsym(g_libssl, "SSL_CTX_set_keylog_callback");
      if (fn != NULL) fn(ctx, callback); }
}

PAL void CryptoNative_SslCtxSetAlpnSelectCb(SSL_CTX* ctx, void* callback, void* arg)
{
    if (ctx == NULL || callback == NULL) return;
    { int (*fn)(SSL_CTX*, void*, void*) = (int (*)(SSL_CTX*, void*, void*))dlsym(g_libssl, "SSL_CTX_set_alpn_select_cb");
      if (fn != NULL) fn(ctx, callback, arg); }
}

PAL void CryptoNative_SslCtxSetDefaultOcspCallback(SSL_CTX* ctx) { (void)ctx; }

/* pal : SNI = SSL_set_tlsext_host_name ; la chaine DNS e (NFID). */
PAL int32_t CryptoNative_SslSetTlsExtHostName(SSL* ssl, uint8_t* name)
{
    if (ssl == NULL || name == NULL || bssl.ssl_set_tlsext_host_name == NULL) return 0;
    if (bssl.err_clear) bssl.err_clear();
    return bssl.ssl_set_tlsext_host_name(ssl, (const char*)name);
}

PAL int32_t CryptoNative_SslSetAlpnProtos(SSL* ssl, const uint8_t* protos, uint32_t protos_len)
{
    if (ssl == NULL || (protos == NULL && protos_len > 0) || bssl.ssl_set_alpn_protos == NULL) return -1;
    return bssl.ssl_set_alpn_protos(ssl, protos, protos_len);
}

PAL void CryptoNative_SslGet0AlpnSelected(SSL* ssl, const uint8_t** protocol, uint32_t* len)
{
    if (protocol != NULL) *protocol = NULL;
    if (len != NULL) *len = 0;
    if (ssl != NULL && bssl.ssl_get0_alpn_selected) bssl.ssl_get0_alpn_selected(ssl, (const unsigned char**)protocol, len);
}

PAL int32_t CryptoNative_SslAddExtraChainCert(SSL* ssl, X509* x509)
{
    (void)ssl; (void)x509;
    return 0;
}

PAL int32_t CryptoNative_SslCtxAddExtraChainCert(SSL_CTX* ctx, X509* x509)
{
    (void)ctx; (void)x509;
    return 0;
}

PAL int32_t CryptoNative_SslAddClientCAs(SSL* ssl, X509** x509s, uint32_t count)
{
    (void)ssl; (void)x509s; (void)count;
    return 0;
}

PAL void CryptoNative_SslSetClientCertCallback(SSL* ssl, int set) { (void)ssl; (void)set; }
PAL void CryptoNative_SslSetPostHandshakeAuth(SSL* ssl, int32_t val) { (void)ssl; (void)val; }

PAL int32_t CryptoNative_SslSetSigalgs(SSL* ssl, uint8_t* str)
{
    if (ssl == NULL || str == NULL || bssl.ssl_set1_sigalgs_list == NULL) return 0;
    return bssl.ssl_set1_sigalgs_list(ssl, (const char*)str);
}

PAL int32_t CryptoNative_SslSetClientSigalgs(SSL* ssl, uint8_t* str)
{
    return CryptoNative_SslSetSigalgs(ssl, str);
}

/* id IANA-managed 32 bits decrypted (pal : SSL_CIPHER_get_id). */
PAL int32_t CryptoNative_SslGetCurrentCipherId(SSL* ssl, int32_t* cipherId)
{
    const SSL_CIPHER* cipher;
    if (ssl == NULL || cipherId == NULL || bssl.ssl_get_current_cipher == NULL || bssl.cipher_get_id == NULL)
        return 0;
    cipher = bssl.ssl_get_current_cipher(ssl);
    if (cipher == NULL) return 0;
    *cipherId = (int32_t)bssl.cipher_get_id(cipher);
    return 1;
}

/* parcours des suites d'un ssl (SSL_CIPHER_find absent — sonde) : direct, puis
 * comparaison des 16 bits de suite du tableau SSL_get_ciphers. */
static const SSL_CIPHER* trouver_cipher_par_suite(SSL* ssl, int32_t suite)
{
    const STACK* ciphers;
    int i;
    int nombre;
    if (bssl.cipher_find != NULL)
    {
        unsigned char cs[2];
        const SSL_CIPHER* direct;
        cs[0] = (unsigned char)((suite >> 8) & 0xFF);
        cs[1] = (unsigned char)(suite & 0xFF);
        direct = bssl.cipher_find(ssl, cs);
        if (direct != NULL) return direct;
    }
    if (bssl.ssl_get_ciphers == NULL) return NULL;
    ciphers = (const STACK*)bssl.ssl_get_ciphers(ssl);
    if (ciphers == NULL) return NULL;
    nombre = bssl.sk_num(ciphers);
    for (i = 0; i < nombre; i++)
    {
        const SSL_CIPHER* candidat = (const SSL_CIPHER*)bssl.sk_value(ciphers, i);
        if (candidat != NULL && (bssl.cipher_get_id(candidat) & 0xFFFF) == (unsigned long)(suite & 0xFFFF))
            return candidat;
    }
    return NULL;
}

/* 1 si le protocole du cipher est antérieur à TLS 1.3 (pal : préfixe DTLS ignoré). */
static int cipher_version_pre_tls13(const SSL_CIPHER* cipher)
{
    const char* version = bssl.cipher_get_version(cipher);
    if (version == NULL) return 0;
    if (version[0] == 'D') version++;
    return strcmp(version, "TLSv1.3") != 0;
}

PAL const char* CryptoNative_GetOpenSslCipherSuiteName(SSL* ssl, int32_t cipherSuite, int32_t* isTls12OrLower)
{
    const SSL_CIPHER* cipher = (ssl != NULL) ? trouver_cipher_par_suite(ssl, cipherSuite) : NULL;
    if (cipher == NULL)
    {
        if (isTls12OrLower != NULL) *isTls12OrLower = 0;
        return NULL;
    }
    if (isTls12OrLower != NULL) *isTls12OrLower = cipher_version_pre_tls13(cipher);
    return bssl.cipher_get_name(cipher);
}

/* default signature algorithms : SSL_get_sigalgs absent (sonde) → table défaut
 * BoringSSL (TLS 1.2 + 1.3, ordre standard d'offre client). */
static const uint16_t deflate_sigalgs[] = {
    0x0401, 0x0501, 0x0601,                     /* rsa_pkcs sha256/384/512 */
    0x0403, 0x0503, 0x0603,                     /* ecdsa_secp256/384/521 */
    0x0804, 0x0805, 0x0806,                     /* rsa_pss_rsae sha256/384/512 */
    0x0807,                                     /* ed25519 */
};

PAL int32_t CryptoNative_GetDefaultSignatureAlgorithms(uint16_t* buffer, int32_t* count)
{
    int i;
    int nombre = (int)(sizeof(deflate_sigalgs) / sizeof(deflate_sigalgs[0]));
    if (buffer == NULL || count == NULL || *count < nombre) return -1;
    for (i = 0; i < nombre; i++) buffer[i] = deflate_sigalgs[i];
    *count = nombre;
    return 0;
}

/* TLS 1.3 : support natif BoringSSL (même sans set_ciphersuites) → 1. */
PAL int32_t CryptoNative_Tls13Supported(void) { return 1; }

/* pal OpenSslGetProtocolSupport : protocoles offerts (TLS absent sur boring) */
PAL int32_t CryptoNative_OpenSslGetProtocolSupport(SslProtocols_e protocol)
{
    switch (protocol)
    {
        case PAL_SSL_TLS:
        case PAL_SSL_TLS11:
        case PAL_SSL_TLS12:
        case PAL_SSL_TLS13:
            return 1;
        case PAL_SSL_SSL3:
            return 0;
        case 0:
        default:
            return 0;
    }
}

/* pal SslStapleOcsp : void — no-op sans OCSP BoringSSL GSS APIs... */
PAL void CryptoNative_SslStapleOcsp(SSL* ssl, uint8_t* buf, int32_t len) { (void)ssl; (void)buf; (void)len; }
