/* Provider GSS/NTLM (libSystem.Net.Security.Native) pour l'IL linux System.Net.Security.dll
 * embarqué dans le bundle Android : 21 symboles NetSecurityNative_* (pal_gss.c/pal_negotiate.c).
 *
 * Sur Linux le pal officiel délègue à GSS-API/libkrb5 (libSystem.Net.Security.Native.so). Sur
 * Android aucun GSS n'est fourni et ces chemins servent Negotiate/Ntlm (SPNEGO/proxys) — jamais
 * les clients HTTPS TMDB/OMDb/plugin-repo. Chaque symbole échoue proprement (0/NULL) : le géré
 * retourne « auth non disponible » sans crash (NoCredentialsException attrapable).
 *
 * Build : build-tlsprovider.sh (NDK clang, -fvisibility=hidden). Couverture verrouillée par
 * nm -D vs symbols-netsecurity.txt.
 */
#include <stdint.h>

#define PAL __attribute__((__visibility__("default")))

/* Une fonction par symbole, un seul type de retour int32_t (ABI aarch64 : X0). */
PAL int32_t NetSecurityNative_AcceptSecContext(void) { return 0; }
PAL int32_t NetSecurityNative_AcquireAcceptorCred(void) { return 0; }
PAL int32_t NetSecurityNative_DeleteSecContext(void) { return 0; }
PAL int32_t NetSecurityNative_DisplayMajorStatus(void) { return 0; }
PAL int32_t NetSecurityNative_DisplayMinorStatus(void) { return 0; }
PAL int32_t NetSecurityNative_EnsureGssInitialized(void) { return 0; }   /* 0 = GSS non initialisé → NTLM/SPNEGO désactivés côté géré */
PAL int32_t NetSecurityNative_GetMic(void) { return 0; }
PAL int32_t NetSecurityNative_GetUser(void) { return 0; }
PAL int32_t NetSecurityNative_ImportPrincipalName(void) { return 0; }
PAL int32_t NetSecurityNative_ImportUserName(void) { return 0; }
PAL int32_t NetSecurityNative_InitSecContext(void) { return 0; }
PAL int32_t NetSecurityNative_InitSecContextEx(void) { return 0; }
PAL int32_t NetSecurityNative_InitiateCredSpNego(void) { return 0; }
PAL int32_t NetSecurityNative_InitiateCredWithPassword(void) { return 0; }
PAL int32_t NetSecurityNative_IsNtlmInstalled(void) { return 0; }
PAL int32_t NetSecurityNative_ReleaseCred(void) { return 0; }
PAL int32_t NetSecurityNative_ReleaseGssBuffer(void) { return 0; }
PAL int32_t NetSecurityNative_ReleaseName(void) { return 0; }
PAL int32_t NetSecurityNative_Unwrap(void) { return 0; }
PAL int32_t NetSecurityNative_VerifyMic(void) { return 0; }
PAL int32_t NetSecurityNative_Wrap(void) { return 0; }
