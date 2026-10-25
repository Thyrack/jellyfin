#!/usr/bin/env bash
# G2 — benchmarks encode/décode par marche : fps mesurés sur les binaires de la matrice T8.
set -euo pipefail
# shellcheck disable=SC1090,SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

generate_inputs() { # médias de benchmark générés sur l'hôte
  local gen="$1"
  mkdir -p "$gen"
  [ -f "$gen/in_4k_h264.mp4" ] || ffmpeg -hide_banner -loglevel error -y \
    -f lavfi -i testsrc2=size=3840x2160:rate=30 -t 6 \
    -c:v libx264 -preset veryfast -pix_fmt yuv420p "$gen/in_4k_h264.mp4"
  [ -f "$gen/in_1080_h264.mp4" ] || ffmpeg -hide_banner -loglevel error -y \
    -f lavfi -i testsrc2=size=1920x1080:rate=30 -t 6 \
    -c:v libx264 -preset veryfast -pix_fmt yuv420p "$gen/in_1080_h264.mp4"
  if ffmpeg -hide_banner -encoders 2>/dev/null | grep -q libx265; then
    [ -f "$gen/in_1080_hevc.mp4" ] || ffmpeg -hide_banner -loglevel error -y \
      -f lavfi -i testsrc2=size=1920x1080:rate=30 -t 6 \
      -c:v libx265 -preset ultrafast -pix_fmt yuv420p "$gen/in_1080_hevc.mp4"
  fi
}

run_case() { # run_case <variant> <route> <decoder-args> <input> <encoder-args>
  local variant="$1" route="$2" dec="$3" input="$4" enc="$5"
  local pr="$PROBE_DIR/out/bench/progress-$variant-$route.txt"
  shell_cmd "/data/local/tmp/jf-ffmpeg-$variant $dec -i /data/local/tmp/$input -t 4 $enc -f null /dev/null" > "$pr" 2>&1 || true
  printf '%s\t%s\t%s\t%s\t%s\n' "$variant" "$input" "$route" "$(parse_progress_fps "$pr")" "$(parse_progress_speed "$pr")" >> "$DATA_DIR/g2-bench.tsv"
}

push_variant() { # push <variant> : binaire sur l'appareil puis listage des encodeurs HW
  local variant="$1"
  local bin="$PROBE_DIR/out/ffmpeg-$variant/ffmpeg"
  if [ ! -x "$bin" ]; then
    warn "variant $variant absent : exécuter tools/probe/build-ffmpeg.sh $variant (T8)."
    return 1
  fi
  run_adb push "$bin" "/data/local/tmp/jf-ffmpeg-$variant"
  shell_cmd "chmod 744 /data/local/tmp/jf-ffmpeg-$variant"
  if ! shell_cmd "test -x /data/local/tmp/jf-ffmpeg-$variant"; then
    warn "push du binaire $variant non vérifié sur l'appareil — skipping les mesures."
    return 1
  fi
  if ! shell_cmd "/data/local/tmp/jf-ffmpeg-$variant -hide_banner -encoders 2>/dev/null | grep -E 'v4l2m2m|mediacodec|nvdec'" \
    > "$DATA_DIR/g2-encoders-$variant.txt" 2>&1; then
    printf 'listage encodeurs échoué (binaire sans backend hw ou fichier vide)\n' > "$DATA_DIR/g2-encoders-$variant.txt"
  fi
  return 0
}

main() {
  if [ "$DRY_RUN" -eq 1 ]; then
    log 'dry-run : 3 inputs générés, 2 variants poussés, 2-3 routes mesurées par marche, TSV + listes encoders.'
    exit 0
  fi
  ensure_data_dir
  require_cmd ffmpeg
  local tsv="$DATA_DIR/g2-bench.tsv" gen="$PROBE_DIR/out/bench"
  adb_target
  generate_inputs "$gen"
  # push des inputs de benchmark sur l'appareil (run_case les consomme via /data/local/tmp)
  local input_file
  for input_file in in_4k_h264.mp4 in_1080_h264.mp4 in_1080_hevc.mp4; do
    if [ -f "$gen/$input_file" ]; then
      run_adb push "$gen/$input_file" "/data/local/tmp/$input_file"
    fi
  done
  printf 'variant\tinput\troute\tfps\tspeed\n' > "$tsv"
  if push_variant sw; then
    run_case sw 'dcpu-enc-cpu' '' 'in_1080_h264.mp4' '-c:v libx264 -preset veryfast'
    run_case sw 'dcpu-4k-scale' '' 'in_4k_h264.mp4' '-vf scale=1920:1080 -c:v libx264 -preset veryfast'
  fi
  if push_variant v4l2m2m; then
    run_case v4l2m2m 'dcpu-enc-m2m' '' 'in_1080_h264.mp4' '-c:v h264_v4l2m2m'
    run_case v4l2m2m 'dcpu-4k-scale' '' 'in_4k_h264.mp4' '-vf scale=1920:1080 -c:v h264_v4l2m2m'
    if [ -f "$gen/in_1080_hevc.mp4" ]; then
      run_case v4l2m2m 'hwdec-enc' '-hwaccel v4l2m2m -c:v hevc_v4l2m2m' 'in_1080_hevc.mp4' '-c:v h264_v4l2m2m'
    fi
  fi
  mediacodec_bench "$tsv"
  [ -f "$gen/in_1080_hevc.mp4" ] || warn 'input HEVC absent (libx265 introuvable sur hôte) : déposer un HEVC test dans out/bench/in_1080_hevc.mp4 avant de relancer.'
  log "benchmarks : $tsv"
}

mediacodec_bench() { # marche MediaCodec : exécutée seulement si l'encodeur existe (constat T8 via config.h)
  local tsv="$1"
  local bin="$PROBE_DIR/out/ffmpeg-mediacodec/ffmpeg"
  if [ ! -x "$bin" ]; then
    warn "mediacodec : binaire absent (build échoué au configure) — constat no-go consigné."
    printf 'mediacodec\tconstat\tno-go : binaire absent (configure KO, cf. out/ffmpeg-mediacodec/configure.log)\t0\t\n' >> "$tsv"
    return 0
  fi
  if grep -q "ENCODEUR PRÉSENT" "$PROBE_DIR/out/ffmpeg-mediacodec/encoders-presence.txt" 2>/dev/null; then
    run_adb push "$bin" /data/local/tmp/jf-ffmpeg-mediacodec
    shell_cmd 'chmod 744 /data/local/tmp/jf-ffmpeg-mediacodec'
    # listage des encodeurs MediaCodec exposés par le binaire (écrit dans g2-encoders-mediacodec.txt)
    shell_cmd "/data/local/tmp/jf-ffmpeg-mediacodec -hide_banner -encoders 2>/dev/null | grep -E 'mediacodec'" \
      > "$DATA_DIR/g2-encoders-mediacodec.txt" 2>&1 || true
    # vérification d'usage sur l'appareil : l'encodeur doit être listé
    if shell_cmd "/data/local/tmp/jf-ffmpeg-mediacodec -hide_banner -encoders 2>/dev/null" 2>&1 | grep -q h264_mediacodec; then
      run_case mediacodec 'dcpu-enc-mc' '' 'in_1080_h264.mp4' '-c:v h264_mediacodec'
      run_case mediacodec 'dcpu-4k-scale' '' 'in_4k_h264.mp4' '-vf scale=1920:1080 -c:v h264_mediacodec'
    else
      printf 'mediacodec\tconstat\tno-go : h264_mediacodec absent des encodeurs listés par le binaire\t0\t\n' >> "$tsv"
    fi
  else
    printf 'mediacodec\tconstat\tno-go : CONFIG_H264_MEDIACODEC_ENCODER absent de la base ffmpeg v7.1.3-6\t0\t\n' >> "$tsv"
  fi
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then main; fi
