/* Micro-sonde dlopen de la pile NVMedia du Tegra (Shield 2015/2017).
   Contexte shell ADB : informatif. Résultat authentifiant : mini-sonde APK (Task 5). */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <stdio.h>
#include <string.h>

static const char *LIBS[] = { "libnvmedia.so", "libnvmpeg11.so", "libnvhost.so" };

int main(void) {
  for (size_t i = 0; i < sizeof(LIBS) / sizeof(LIBS[0]); i++) {
    void *h = dlopen(LIBS[i], RTLD_NOW | RTLD_LOCAL);
    if (h) {
      printf("%-18s OK\n", LIBS[i]);
      dlclose(h);
    } else {
      char *err = dlerror(); /* dlerror() réinitialise le contexte : lecture unique */
      printf("%-18s ÉCHEC (%s)\n", LIBS[i], err ? err : "raison inconnue");
    }
  }
  return 0; /* les verdicts sont lus sur stdout, pas via l'exit-code */
}
