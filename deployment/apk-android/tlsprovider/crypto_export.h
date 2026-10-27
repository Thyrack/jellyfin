/* Export pal : la compilation -fvisibility=hidden exige une annotation explicite
 * sur chaque symbole exporté. Les TU (provider réel, failover généré, GSS)
 * incluent cet en-tête pour garantir que seuls les membres de l'union sortent du .so. */
#ifndef CRYPTO_EXPORT_H_PLD
#define CRYPTO_EXPORT_H_PLD

#include <stdint.h>

#define PAL __attribute__((__visibility__("default")))

#endif /* CRYPTO_EXPORT_H_PLD */
