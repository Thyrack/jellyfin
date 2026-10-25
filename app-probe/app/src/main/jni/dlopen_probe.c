/* Sonde dlopen exécutée dans le namespace linker applicatif — résultat authentifiant G0. */
#include <dlfcn.h>
#include <jni.h>
#include <string.h>

JNIEXPORT jstring JNICALL
Java_org_jellyfin_shieldprobe_MainActivity_dlopenProbe(JNIEnv *env, jobject thiz, jstring jlib) {
    (void)thiz;
    jboolean isCopy = JNI_FALSE;
    const char *lib = (*env)->GetStringUTFChars(env, jlib, &isCopy);
    void *h = dlopen(lib, RTLD_NOW | RTLD_LOCAL);
    (*env)->ReleaseStringUTFChars(env, jlib, lib);
    if (h) { dlclose(h); return NULL; }
    char *err = dlerror();
    if (!err) err = "raison inconnue";
    return (*env)->NewStringUTF(env, err);
}
