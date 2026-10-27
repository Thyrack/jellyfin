/* Stub natif crypto Android v2 — remplace libSystem.Security.Cryptography.Native.Android.so
 * pour un process .NET (CoreCLR) libre, SANS JVM attachée (Shield 2015/2017).
 *
 * Constats (smoke T5) :
 *   - le .so officiel exige une JavaVM (AndroidCryptoNative_InitLibraryOnLoad) → SIGSEGV
 *     dès le premier usage crypto ;
 *   - le stub v1 (RNG seul) exportait 0 symbole (-fvisibility=hidden sans annotations) →
 *     EntryPointNotFoundException: CryptoNative_GetMaxMdSize au TypeInit 'Crypto' géré.
 *
 * Conception v2 :
 *   - dlopen paresseux de /system/lib64/libcrypto.so (BoringSSL système, RTLD_NOW|RTLD_LOCAL) ;
 *     en cas d'échec, état mémorisé et échec propre (0/NULL) pour chaque fonction BoringSSL.
 *   - empreintes/HMAC/PBKDF2 réellement calculés via les EVP_* / HMAC_* de BoringSSL :
 *     les tokens EVP_MD* retournés par EvpMd5/Sha* sont des pointeurs BoringSSL réels, seuls
 *     re-passés dans les fonctions de ce même stub (contrat géré : intptr_t opaques).
 *   - RNG : lecture directe /dev/urandom (indépendant de BoringSSL).
 *   - Err* : miroir du pal officiel Android (pas de file d'erreurs → exceptions gérées nues).
 *   - tout autre symbole de l'union (exported-symbols.txt + imports gérés ImplMap, cf.
 *     managed-symbols.txt) : échec propre 0/NULL — jamais de segfault, le géré lève alors des
 *     CryptographicException attrapables (AES/DES/RSA/EC/X509/TLS : hors périmètre v2).
 *   - chaque symbole est déclaré une à une (pas de macro) ; les P/Invoke gérés déclarent des
 *     signatures précises, mais « int32_t sym(void) » appelé avec arguments est ABI-OK aarch64
 *     (X0..X7 morts) — preuve : nm -D du .so == union complète (verrouillé par build-cryptostub.sh).
 *
 * ABI : signatures reprises des pal_*.c officiels d'Android (dotnet/runtime) — md en intptr_t,
 * tailles en int32_t, tailles de sortie en pointeurs int32_t ou uint32_t (BoringSSL écrit des
 * size_t : passage par tampon interne puis copie sur 4 octets).
 * Build : deployment/apk-android/build-cryptostub.sh (NDK clang, Android 26).
 */
#include <dlfcn.h>
#include <fcntl.h>
#include <pthread.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

/* Types BoringSSL opaques (aucun en-tête OpenSSL requis). */
typedef struct evp_md_st EVP_MD;
typedef struct evp_md_ctx_st EVP_MD_CTX;
typedef struct hmac_ctx_st HMAC_CTX;

#define PAL_ECHEC 0
#define PAL_SUCCES 1
#define MD_TAILLE_MAX 64 /* EVP_MAX_MD_SIZE BoringSSL (SHA-512) */

#define BSSL_CHEMIN "/system/lib64/libcrypto.so"

/* ---- Table des symboles BoringSSL résolus une fois pour toutes ---- */
static struct {
    const EVP_MD* (*md5)(void);
    const EVP_MD* (*sha1)(void);
    const EVP_MD* (*sha256)(void);
    const EVP_MD* (*sha384)(void);
    const EVP_MD* (*sha512)(void);
    size_t (*md_taille)(const EVP_MD*);
    EVP_MD_CTX* (*md_ctx_nouveau)(void);
    void (*md_ctx_liberer)(EVP_MD_CTX*);
    int (*digest_init_ex)(EVP_MD_CTX*, const EVP_MD*, void*);
    int (*digest_update)(EVP_MD_CTX*, const void*, size_t);
    int (*digest_final_ex)(EVP_MD_CTX*, uint8_t*, size_t*);
    int (*md_ctx_copie_ex)(EVP_MD_CTX*, const EVP_MD_CTX*);
    int (*pbkdf2)(const char*, size_t, const uint8_t*, size_t, uint32_t, const EVP_MD*, size_t, uint8_t*);
    HMAC_CTX* (*hmac_nouveau)(void);
    void (*hmac_liberer)(HMAC_CTX*);
    int (*hmac_init_ex)(HMAC_CTX*, const void*, size_t, const EVP_MD*, void*);
    int (*hmac_update)(HMAC_CTX*, const uint8_t*, size_t);
    int (*hmac_final)(HMAC_CTX*, uint8_t*, size_t*);
    int (*hmac_copie)(HMAC_CTX*, const HMAC_CTX*); /* optionnel : HmacCurrent dégrade sinon */
} bssl;

/* Échec permanent (dlopen/dlsym) : toutes les fonctions BoringSSL renvoient alors 0/NULL. */
static void* g_libcrypto = NULL;
static int bssl_indisponible = 0;
static pthread_once_t bssl_une_fois = PTHREAD_ONCE_INIT;

/* Chargement unique et thread-safe de la BoringSSL système. */
static void bssl_charger(void)
{
    static const struct { void** cible; const char* nom; } requis[] = {
        { (void**)&bssl.md5, "EVP_md5" },
        { (void**)&bssl.sha1, "EVP_sha1" },
        { (void**)&bssl.sha256, "EVP_sha256" },
        { (void**)&bssl.sha384, "EVP_sha384" },
        { (void**)&bssl.sha512, "EVP_sha512" },
        { (void**)&bssl.md_taille, "EVP_MD_size" },
        { (void**)&bssl.md_ctx_nouveau, "EVP_MD_CTX_new" },
        { (void**)&bssl.md_ctx_liberer, "EVP_MD_CTX_free" },
        { (void**)&bssl.digest_init_ex, "EVP_DigestInit_ex" },
        { (void**)&bssl.digest_update, "EVP_DigestUpdate" },
        { (void**)&bssl.digest_final_ex, "EVP_DigestFinal_ex" },
        { (void**)&bssl.md_ctx_copie_ex, "EVP_MD_CTX_copy_ex" },
        { (void**)&bssl.pbkdf2, "PKCS5_PBKDF2_HMAC" },
        { (void**)&bssl.hmac_nouveau, "HMAC_CTX_new" },
        { (void**)&bssl.hmac_liberer, "HMAC_CTX_free" },
        { (void**)&bssl.hmac_init_ex, "HMAC_Init_ex" },
        { (void**)&bssl.hmac_update, "HMAC_Update" },
        { (void**)&bssl.hmac_final, "HMAC_Final" },
    };
    size_t i;

    g_libcrypto = dlopen(BSSL_CHEMIN, RTLD_NOW | RTLD_LOCAL);
    if (g_libcrypto == NULL) {
        bssl_indisponible = 1;
        return;
    }
    for (i = 0; i < sizeof(requis) / sizeof(requis[0]); i++) {
        *requis[i].cible = dlsym(g_libcrypto, requis[i].nom);
        if (*requis[i].cible == NULL) {
            bssl_indisponible = 1;
            return;
        }
    }
    /* HMAC_CTX_copy : présent dans BoringSSL ; absent → HmacCurrent échoue proprement. */
    bssl.hmac_copie = (int (*)(HMAC_CTX*, const HMAC_CTX*))dlsym(g_libcrypto, "HMAC_CTX_copy");
}

/* Retourne 1 si BoringSSL est chargé et complet, 0 sinon (échec mémorisé). */
static int bssl_pret(void)
{
    pthread_once(&bssl_une_fois, bssl_charger);
    return !bssl_indisponible;
}

/* ---- RNG : /dev/urandom direct (aucune dépendance BoringSSL) ---- */

/* Remplit buff de len octets ; retourne 1 (le géré marshalle en bool), 0 si échec. */
int32_t CryptoNative_GetRandomBytes(uint8_t* buff, int32_t len)
{
    int fd;
    int total = 0;

    if (buff == NULL || len <= 0) return PAL_ECHEC;
    fd = open("/dev/urandom", O_RDONLY);
    if (fd < 0) return PAL_ECHEC;
    while (total < len) {
        ssize_t lu = read(fd, buff + total, (size_t)(len - total));
        if (lu <= 0) {
            close(fd);
            return PAL_ECHEC; /* lecture partielle = échec (pas de secret tronqué) */
        }
        total += (int)lu;
    }
    close(fd);
    return PAL_SUCCES;
}

/* ---- Empreintes : EVP_MD* BoringSSL réels (tokens opaques pour le géré) ---- */

intptr_t CryptoNative_EvpMd5(void) { return bssl_pret() ? (intptr_t)bssl.md5() : 0; }
intptr_t CryptoNative_EvpSha1(void) { return bssl_pret() ? (intptr_t)bssl.sha1() : 0; }
intptr_t CryptoNative_EvpSha256(void) { return bssl_pret() ? (intptr_t)bssl.sha256() : 0; }
intptr_t CryptoNative_EvpSha384(void) { return bssl_pret() ? (intptr_t)bssl.sha384() : 0; }
intptr_t CryptoNative_EvpSha512(void) { return bssl_pret() ? (intptr_t)bssl.sha512() : 0; }

/* Taille en octets de l'empreinte ; 0 si empreinte inconnue. */
int32_t CryptoNative_EvpMdSize(intptr_t md)
{
    if (!bssl_pret() || md == 0) return -1;
    return (int32_t)bssl.md_taille((const EVP_MD*)md);
}

int32_t CryptoNative_GetMaxMdSize(void) { return MD_TAILLE_MAX; }

/* ---- Contextes d'empreinte (create/destroy/copy/reset/update/final/current/oneshot) ---- */

/* Contexte neuf initialisé avec md ; NULL si échec (libère le ctx en cas d'init ratée). */
static EVP_MD_CTX* ctx_neuf_init(const EVP_MD* md)
{
    EVP_MD_CTX* ctx = bssl.md_ctx_nouveau();
    if (ctx == NULL) return NULL;
    if (bssl.digest_init_ex(ctx, md, NULL) != 1) {
        bssl.md_ctx_liberer(ctx);
        return NULL;
    }
    return ctx;
}

void* CryptoNative_EvpMdCtxCreate(intptr_t type)
{
    if (!bssl_pret() || type == 0) return NULL;
    return ctx_neuf_init((const EVP_MD*)type);
}

void CryptoNative_EvpMdCtxDestroy(void* ctx)
{
    if (ctx != NULL && bssl_pret()) bssl.md_ctx_liberer((EVP_MD_CTX*)ctx);
}

/* Clone le contexte : le clone est prêt à poursuivre la même empreinte. */
void* CryptoNative_EvpMdCtxCopyEx(void* ctx)
{
    EVP_MD_CTX* clone;
    if (!bssl_pret() || ctx == NULL) return NULL;
    clone = bssl.md_ctx_nouveau();
    if (clone == NULL) return NULL;
    if (bssl.md_ctx_copie_ex(clone, (const EVP_MD_CTX*)ctx) != 1) {
        bssl.md_ctx_liberer(clone);
        return NULL;
    }
    return clone;
}

/* Réinitialise le contexte pour une nouvelle empreinte de type md ; 1/0. */
int32_t CryptoNative_EvpDigestReset(void* ctx, intptr_t type)
{
    if (!bssl_pret() || ctx == NULL || type == 0) return PAL_ECHEC;
    return bssl.digest_init_ex((EVP_MD_CTX*)ctx, (const EVP_MD*)type, NULL) == 1 ? PAL_SUCCES : PAL_ECHEC;
}

int32_t CryptoNative_EvpDigestUpdate(void* ctx, void* d, int32_t cnt)
{
    if (!bssl_pret() || ctx == NULL || cnt < 0 || (cnt > 0 && d == NULL)) return PAL_ECHEC;
    return bssl.digest_update((EVP_MD_CTX*)ctx, d, (size_t)cnt) == 1 ? PAL_SUCCES : PAL_ECHEC;
}

/* Finalise et écrit la taille sur 4 octets (BoringSSL : size_t → tampon interne). */
static int32_t final_ex_avec_copie_taille(void* ctx, uint8_t* md, uint32_t* s)
{
    size_t taille;
    if (bssl.digest_final_ex((EVP_MD_CTX*)ctx, md, &taille) != 1) return PAL_ECHEC;
    *s = (uint32_t)taille;
    return PAL_SUCCES;
}

int32_t CryptoNative_EvpDigestFinalEx(void* ctx, uint8_t* md, uint32_t* s)
{
    if (!bssl_pret() || ctx == NULL || md == NULL || s == NULL) return PAL_ECHEC;
    return final_ex_avec_copie_taille(ctx, md, s);
}

/* Empreinte courante sans consommer l'état : clone + final du clone. */
int32_t CryptoNative_EvpDigestCurrent(void* ctx, uint8_t* md, uint32_t* s)
{
    void* clone;
    int32_t ret;
    if (!bssl_pret() || ctx == NULL || md == NULL || s == NULL) return PAL_ECHEC;
    clone = CryptoNative_EvpMdCtxCopyEx(ctx);
    if (clone == NULL) return PAL_ECHEC;
    ret = final_ex_avec_copie_taille(clone, md, s);
    CryptoNative_EvpMdCtxDestroy(clone);
    return ret;
}

int32_t CryptoNative_EvpDigestOneShot(intptr_t type, void* source, int32_t sourceSize, uint8_t* md, uint32_t* mdSize)
{
    void* ctx;
    int32_t ret;
    if (type == 0 || md == NULL || mdSize == NULL || sourceSize < 0 || (sourceSize > 0 && source == NULL))
        return PAL_ECHEC;
    ctx = CryptoNative_EvpMdCtxCreate(type);
    if (ctx == NULL) return PAL_ECHEC;
    ret = CryptoNative_EvpDigestUpdate(ctx, source, sourceSize);
    if (ret == PAL_SUCCES) ret = final_ex_avec_copie_taille(ctx, md, mdSize);
    CryptoNative_EvpMdCtxDestroy(ctx);
    return ret;
}

/* ---- HMAC : contexte interne {HMAC_CTX*, md, copie de clé} (contrat Reset sans clé) ---- */

static const uint8_t g_octet_nul = 0;

typedef struct {
    HMAC_CTX* ctx;     /* contexte BoringSSL */
    const EVP_MD* md;  /* empreinte du HMAC, conservée pour Reset */
    uint8_t* cle;      /* copie de la clé (NULL si clé vide) */
    int32_t cle_long;  /* longueur de la copie */
} mac_ctx_t;

/* Copie la clé ; clé vide → NULL/0 (la clé effective devient l'octet nul). */
static int mac_cle_copier(mac_ctx_t* mac, const uint8_t* cle, int32_t cle_long)
{
    if (cle != NULL && cle_long > 0) {
        mac->cle = (uint8_t*)malloc((size_t)cle_long);
        if (mac->cle == NULL) return 0;
        memcpy(mac->cle, cle, (size_t)cle_long);
        mac->cle_long = cle_long;
        return 1;
    }
    mac->cle = NULL;
    mac->cle_long = 0;
    return 1;
}

/* Sélectionne la clé effective pour HMAC_Init_ex ; clé vide → 1 octet nul (miroir du
 * pal officiel : padding zéro ⇒ même empreinte qu'une clé de longueur nulle). */
static void mac_cle_eff(const mac_ctx_t* mac, const uint8_t** cle, size_t* longueur)
{
    if (mac->cle != NULL && mac->cle_long > 0) {
        *cle = mac->cle;
        *longueur = (size_t)mac->cle_long;
    } else {
        *cle = &g_octet_nul;
        *longueur = 1;
    }
}

void* CryptoNative_HmacCreate(uint8_t* cle, int32_t cle_long, intptr_t type)
{
    mac_ctx_t* mac;
    const uint8_t* cle_eff;
    size_t long_eff;

    if (!bssl_pret() || type == 0 || (cle != NULL && cle_long < 0)) return NULL;
    mac = (mac_ctx_t*)calloc(1, sizeof(mac_ctx_t));
    if (mac == NULL) return NULL;
    if (!mac_cle_copier(mac, cle, cle_long)) {
        free(mac);
        return NULL;
    }
    mac->md = (const EVP_MD*)type;
    mac->ctx = bssl.hmac_nouveau();
    if (mac->ctx == NULL) {
        free(mac->cle);
        free(mac);
        return NULL;
    }
    mac_cle_eff(mac, &cle_eff, &long_eff);
    if (bssl.hmac_init_ex(mac->ctx, cle_eff, long_eff, mac->md, NULL) != 1) {
        bssl.hmac_liberer(mac->ctx);
        free(mac->cle);
        free(mac);
        return NULL;
    }
    return (void*)mac;
}

/* Ré-initialise avec la clé conservée (miroir du Reset Java implicite). */
int32_t CryptoNative_HmacReset(void* contexte)
{
    mac_ctx_t* mac = (mac_ctx_t*)contexte;
    const uint8_t* cle_eff;
    size_t long_eff;

    if (!bssl_pret() || mac == NULL || mac->ctx == NULL) return PAL_ECHEC;
    mac_cle_eff(mac, &cle_eff, &long_eff);
    return bssl.hmac_init_ex(mac->ctx, cle_eff, long_eff, mac->md, NULL) == 1 ? PAL_SUCCES : PAL_ECHEC;
}

int32_t CryptoNative_HmacUpdate(void* contexte, uint8_t* data, int32_t len)
{
    mac_ctx_t* mac = (mac_ctx_t*)contexte;
    if (!bssl_pret() || mac == NULL || mac->ctx == NULL || data == NULL || len <= 0) return PAL_ECHEC;
    return bssl.hmac_update(mac->ctx, data, (size_t)len) == 1 ? PAL_SUCCES : PAL_ECHEC;
}

/* Finalise ; écrit la longueur sur 4 octets (BoringSSL : size_t → tampon interne). */
int32_t CryptoNative_HmacFinal(void* contexte, uint8_t* data, int32_t* len)
{
    mac_ctx_t* mac = (mac_ctx_t*)contexte;
    size_t taille;

    if (!bssl_pret() || mac == NULL || mac->ctx == NULL || data == NULL || len == NULL) return PAL_ECHEC;
    if (bssl.hmac_final(mac->ctx, data, &taille) != 1) return PAL_ECHEC;
    *len = (int32_t)taille;
    return PAL_SUCCES;
}

/* Empreinte courante sans consommer l'état : HMAC_CTX_copy + final de la copie. */
int32_t CryptoNative_HmacCurrent(void* contexte, uint8_t* data, int32_t* len)
{
    mac_ctx_t* mac = (mac_ctx_t*)contexte;
    HMAC_CTX* copie;
    size_t taille;

    if (!bssl_pret() || mac == NULL || mac->ctx == NULL || data == NULL || len == NULL) return PAL_ECHEC;
    if (bssl.hmac_copie == NULL) return PAL_ECHEC;
    copie = bssl.hmac_nouveau();
    if (copie == NULL) return PAL_ECHEC;
    if (bssl.hmac_copie(copie, mac->ctx) != 1) {
        bssl.hmac_liberer(copie);
        return PAL_ECHEC;
    }
    if (bssl.hmac_final(copie, data, &taille) != 1) {
        bssl.hmac_liberer(copie);
        return PAL_ECHEC;
    }
    bssl.hmac_liberer(copie);
    *len = (int32_t)taille;
    return PAL_SUCCES;
}

void CryptoNative_HmacDestroy(void* contexte)
{
    mac_ctx_t* mac = (mac_ctx_t*)contexte;

    if (mac == NULL) return;
    if (mac->ctx != NULL) bssl.hmac_liberer(mac->ctx);
    free(mac->cle);
    free(mac);
}

int32_t CryptoNative_HmacOneShot(intptr_t type, uint8_t* cle, int32_t cle_long, uint8_t* source, int32_t source_long, uint8_t* md, int32_t* md_long)
{
    void* ctx;
    int32_t ret;

    ctx = CryptoNative_HmacCreate(cle, cle_long, type);
    if (ctx == NULL) return PAL_ECHEC;
    ret = source_long == 0 ? PAL_SUCCES : CryptoNative_HmacUpdate(ctx, source, source_long);
    if (ret == PAL_SUCCES) ret = CryptoNative_HmacFinal(ctx, md, md_long);
    CryptoNative_HmacDestroy(ctx);
    return ret;
}

/* ---- PBKDF2 ---- */

/* Sémantique pal_evp.h : -1 si entrée invalide, sinon le retour de PKCS5_PBKDF2_HMAC (1/0). */
int32_t CryptoNative_Pbkdf2(const char* password, int32_t passwordLength, const uint8_t* salt, int32_t saltLength, int32_t iterations, const EVP_MD* digest, uint8_t* destination, int32_t destinationLength)
{
    if (passwordLength < 0 || saltLength < 0 || iterations <= 0 || digest == NULL ||
        destination == NULL || destinationLength < 0) {
        return -1;
    }
    if (password == NULL && passwordLength != 0) return -1;
    if (salt == NULL && saltLength != 0) return -1;
    if (!bssl_pret()) return PAL_ECHEC;
    return (int32_t)bssl.pbkdf2(password != NULL ? password : "",
                                (size_t)passwordLength,
                                salt != NULL ? salt : (const uint8_t*)"",
                                (size_t)saltLength,
                                (uint32_t)iterations,
                                digest,
                                (size_t)destinationLength,
                                destination);
}

/* Nom d'algorithme géré (HashAlgorithmName.Name) → EVP_MD BoringSSL. */
static const EVP_MD* pbkdf2_md_depuis_nom(const char* nom)
{
    if (!bssl_pret() || nom == NULL) return NULL;
    if (strcmp(nom, "MD5") == 0) return bssl.md5();
    if (strcmp(nom, "SHA1") == 0) return bssl.sha1();
    if (strcmp(nom, "SHA256") == 0) return bssl.sha256();
    if (strcmp(nom, "SHA384") == 0) return bssl.sha384();
    if (strcmp(nom, "SHA512") == 0) return bssl.sha512();
    return NULL; /* géré : CryptographicException(UnknownHashAlgorithm) */
}

int32_t AndroidCryptoNative_Pbkdf2(const char* algorithmName, const uint8_t* password, int32_t passwordLength, uint8_t* salt, int32_t saltLength, int32_t iterations, uint8_t* destination, int32_t destinationLength)
{
    const EVP_MD* md = pbkdf2_md_depuis_nom(algorithmName);
    if (md == NULL) return -1;
    return CryptoNative_Pbkdf2((const char*)password, passwordLength, salt, saltLength, iterations, md, destination, destinationLength);
}

/* ---- Initialisation ---- */

/* Interop géré Android : != 0 → InvalidOperationException au cctor CryptoInitializer.
 * Le pal officiel Android retourne 0 (succès, rien à initialiser). */
int32_t CryptoNative_EnsureOpenSslInitialized(void) { return 0; }

/* TLS Java hors périmètre v2 ; l'appel échoue plus loin (SSLStreamCreate → 0). */
int32_t AndroidCryptoNative_SSLStreamInitialize(void) { return PAL_SUCCES; }

/* ---- Err : miroir exact du pal officiel Android (pas de file d'erreurs) ---- */

void CryptoNative_ErrClearError(void) {}

uint64_t CryptoNative_ErrGetErrorAlloc(int32_t* isAllocFailure)
{
    (void)isAllocFailure;
    return 0; /* 0 = pas d'erreur → CryptographicException nue côté géré */
}

uint64_t CryptoNative_ErrPeekError(void) { return 0; }

uint64_t CryptoNative_ErrPeekLastError(void) { return 0; }

const char* CryptoNative_ErrReasonErrorString(uint64_t error)
{
    (void)error;
    return "See logcat for more details.";
}

void CryptoNative_ErrErrorStringN(uint64_t e, char* buf, int32_t len)
{
    static const char message[] = "See logcat for more details.";
    (void)e;
    if (buf == NULL || len <= 0) return;
    memcpy(buf, message, len < (int32_t)sizeof(message) ? (size_t)len : sizeof(message));
}

/* ---- JNI : sans JVM attachée, jamais invoqués ; échec propre ---- */

int32_t JNI_OnLoad(void) { return 0; }

int32_t Java_net_dot_android_crypto_DotnetProxyTrustManager_verifyRemoteCertificate(void) { return 0; }

/* ---- Généré : échec propre pour les symboles restants de l'union (hors périmètre v2) ---- */
int32_t AndroidCryptoNative_AeadCipherFinalEx(void) { return 0; }
int32_t AndroidCryptoNative_Aes128Cbc(void) { return 0; }
int32_t AndroidCryptoNative_Aes128Ccm(void) { return 0; }
int32_t AndroidCryptoNative_Aes128Cfb128(void) { return 0; }
int32_t AndroidCryptoNative_Aes128Cfb8(void) { return 0; }
int32_t AndroidCryptoNative_Aes128Ecb(void) { return 0; }
int32_t AndroidCryptoNative_Aes128Gcm(void) { return 0; }
int32_t AndroidCryptoNative_Aes192Cbc(void) { return 0; }
int32_t AndroidCryptoNative_Aes192Ccm(void) { return 0; }
int32_t AndroidCryptoNative_Aes192Cfb128(void) { return 0; }
int32_t AndroidCryptoNative_Aes192Cfb8(void) { return 0; }
int32_t AndroidCryptoNative_Aes192Ecb(void) { return 0; }
int32_t AndroidCryptoNative_Aes192Gcm(void) { return 0; }
int32_t AndroidCryptoNative_Aes256Cbc(void) { return 0; }
int32_t AndroidCryptoNative_Aes256Ccm(void) { return 0; }
int32_t AndroidCryptoNative_Aes256Cfb128(void) { return 0; }
int32_t AndroidCryptoNative_Aes256Cfb8(void) { return 0; }
int32_t AndroidCryptoNative_Aes256Ecb(void) { return 0; }
int32_t AndroidCryptoNative_Aes256Gcm(void) { return 0; }
int32_t AndroidCryptoNative_BigNumToBinary(void) { return 0; }
int32_t AndroidCryptoNative_ChaCha20Poly1305(void) { return 0; }
int32_t AndroidCryptoNative_CipherCreate(void) { return 0; }
int32_t AndroidCryptoNative_CipherCreatePartial(void) { return 0; }
int32_t AndroidCryptoNative_CipherCtxSetPadding(void) { return 0; }
int32_t AndroidCryptoNative_CipherDestroy(void) { return 0; }
int32_t AndroidCryptoNative_CipherFinalEx(void) { return 0; }
int32_t AndroidCryptoNative_CipherIsSupported(void) { return 0; }
int32_t AndroidCryptoNative_CipherReset(void) { return 0; }
int32_t AndroidCryptoNative_CipherSetKeyAndIV(void) { return 0; }
int32_t AndroidCryptoNative_CipherSetNonceLength(void) { return 0; }
int32_t AndroidCryptoNative_CipherSetTagLength(void) { return 0; }
int32_t AndroidCryptoNative_CipherUpdate(void) { return 0; }
int32_t AndroidCryptoNative_CipherUpdateAAD(void) { return 0; }
int32_t AndroidCryptoNative_DecodeRsaSubjectPublicKeyInfo(void) { return 0; }
int32_t AndroidCryptoNative_DeleteGlobalReference(void) { return 0; }
int32_t AndroidCryptoNative_Des3Cbc(void) { return 0; }
int32_t AndroidCryptoNative_Des3Cfb64(void) { return 0; }
int32_t AndroidCryptoNative_Des3Cfb8(void) { return 0; }
int32_t AndroidCryptoNative_Des3Ecb(void) { return 0; }
int32_t AndroidCryptoNative_DesCbc(void) { return 0; }
int32_t AndroidCryptoNative_DesCfb8(void) { return 0; }
int32_t AndroidCryptoNative_DesEcb(void) { return 0; }
int32_t AndroidCryptoNative_DsaGenerateKey(void) { return 0; }
int32_t AndroidCryptoNative_DsaKeyCreateByExplicitParameters(void) { return 0; }
int32_t AndroidCryptoNative_DsaSign(void) { return 0; }
int32_t AndroidCryptoNative_DsaSignatureFieldSize(void) { return 0; }
int32_t AndroidCryptoNative_DsaSizeP(void) { return 0; }
int32_t AndroidCryptoNative_DsaSizeSignature(void) { return 0; }
int32_t AndroidCryptoNative_DsaVerify(void) { return 0; }
int32_t AndroidCryptoNative_EcDsaSign(void) { return 0; }
int32_t AndroidCryptoNative_EcDsaSize(void) { return 0; }
int32_t AndroidCryptoNative_EcDsaVerify(void) { return 0; }
int32_t AndroidCryptoNative_EcKeyCreateByExplicitParameters(void) { return 0; }
int32_t AndroidCryptoNative_EcKeyCreateByKeyParameters(void) { return 0; }
int32_t AndroidCryptoNative_EcKeyCreateByOid(void) { return 0; }
int32_t AndroidCryptoNative_EcKeyDestroy(void) { return 0; }
int32_t AndroidCryptoNative_EcKeyGetCurveName(void) { return 0; }
int32_t AndroidCryptoNative_EcKeyGetSize(void) { return 0; }
int32_t AndroidCryptoNative_EcKeyUpRef(void) { return 0; }
int32_t AndroidCryptoNative_EcdhDeriveKey(void) { return 0; }
int32_t AndroidCryptoNative_GetBigNumBytes(void) { return 0; }
int32_t AndroidCryptoNative_GetDsaParameters(void) { return 0; }
int32_t AndroidCryptoNative_GetECCurveParameters(void) { return 0; }
int32_t AndroidCryptoNative_GetECKeyParameters(void) { return 0; }
int32_t AndroidCryptoNative_GetRsaParameters(void) { return 0; }
int32_t AndroidCryptoNative_NewGlobalReference(void) { return 0; }
int32_t AndroidCryptoNative_RegisterRemoteCertificateValidationCallback(void) { return 0; }
int32_t AndroidCryptoNative_RsaCreate(void) { return 0; }
int32_t AndroidCryptoNative_RsaDestroy(void) { return 0; }
int32_t AndroidCryptoNative_RsaGenerateKeyEx(void) { return 0; }
int32_t AndroidCryptoNative_RsaPrivateDecrypt(void) { return 0; }
int32_t AndroidCryptoNative_RsaPublicEncrypt(void) { return 0; }
int32_t AndroidCryptoNative_RsaSignPrimitive(void) { return 0; }
int32_t AndroidCryptoNative_RsaSize(void) { return 0; }
int32_t AndroidCryptoNative_RsaUpRef(void) { return 0; }
int32_t AndroidCryptoNative_RsaVerificationPrimitive(void) { return 0; }
int32_t AndroidCryptoNative_SSLGetSupportedProtocols(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamCreate(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamCreateWithCertificates(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamCreateWithKeyStorePrivateKeyEntry(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamGetApplicationProtocol(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamGetCipherSuite(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamGetPeerCertificate(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamGetPeerCertificates(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamGetProtocol(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamHandshake(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamIsLocalCertificateUsed(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamRead(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamRelease(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamRequestClientAuthentication(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamSetApplicationProtocols(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamSetEnabledProtocols(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamSetTargetHost(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamShutdown(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamVerifyHostname(void) { return 0; }
int32_t AndroidCryptoNative_SSLStreamWrite(void) { return 0; }
int32_t AndroidCryptoNative_SSLSupportsApplicationProtocolsConfiguration(void) { return 0; }
int32_t AndroidCryptoNative_SetRsaParameters(void) { return 0; }
int32_t AndroidCryptoNative_X509ChainBuild(void) { return 0; }
int32_t AndroidCryptoNative_X509ChainCreateContext(void) { return 0; }
int32_t AndroidCryptoNative_X509ChainDestroyContext(void) { return 0; }
int32_t AndroidCryptoNative_X509ChainGetCertificateCount(void) { return 0; }
int32_t AndroidCryptoNative_X509ChainGetCertificates(void) { return 0; }
int32_t AndroidCryptoNative_X509ChainGetErrorCount(void) { return 0; }
int32_t AndroidCryptoNative_X509ChainGetErrors(void) { return 0; }
int32_t AndroidCryptoNative_X509ChainSetCustomTrustStore(void) { return 0; }
int32_t AndroidCryptoNative_X509ChainValidate(void) { return 0; }
int32_t AndroidCryptoNative_X509Decode(void) { return 0; }
int32_t AndroidCryptoNative_X509DecodeCollection(void) { return 0; }
int32_t AndroidCryptoNative_X509Encode(void) { return 0; }
int32_t AndroidCryptoNative_X509ExportPkcs7(void) { return 0; }
int32_t AndroidCryptoNative_X509GetCertificateForPrivateKeyEntry(void) { return 0; }
int32_t AndroidCryptoNative_X509GetContentType(void) { return 0; }
int32_t AndroidCryptoNative_X509IsKeyStorePrivateKeyEntry(void) { return 0; }
int32_t AndroidCryptoNative_X509PublicKey(void) { return 0; }
int32_t AndroidCryptoNative_X509StoreAddCertificate(void) { return 0; }
int32_t AndroidCryptoNative_X509StoreAddCertificateWithPrivateKey(void) { return 0; }
int32_t AndroidCryptoNative_X509StoreContainsCertificate(void) { return 0; }
int32_t AndroidCryptoNative_X509StoreDeleteEntry(void) { return 0; }
int32_t AndroidCryptoNative_X509StoreEnumerateCertificates(void) { return 0; }
int32_t AndroidCryptoNative_X509StoreEnumerateTrustedCertificates(void) { return 0; }
int32_t AndroidCryptoNative_X509StoreGetPrivateKeyEntry(void) { return 0; }
int32_t AndroidCryptoNative_X509StoreOpenDefault(void) { return 0; }
int32_t AndroidCryptoNative_X509StoreRemoveCertificate(void) { return 0; }
int32_t CryptoNative_CheckX509Hostname(void) { return 0; }
int32_t CryptoNative_CheckX509IpAddress(void) { return 0; }
int32_t CryptoNative_ErrGetExceptionError(void) { return 0; }
int32_t CryptoNative_X509Destroy(void) { return 0; }
int32_t CryptoNative_X509UpRef(void) { return 0; }
