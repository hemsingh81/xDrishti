#!/usr/bin/env bash
# Files one exchange filing the owner has already saved into the inbox of the filings source (P15-01, ADR 0179): copies it to
# <SYMBOL>_<kind>_<period_end>_<sha8>.<ext> (0600) and writes its sidecar <name>.meta.json (0600) beside it. It only files local
# files; it downloads nothing and reaches no network.
#   scripts/filings-drop.sh --symbol RELIANCE --kind results --period-end 2026-06-30 --broadcast "2026-07-24T16:12:00+05:30" results.xml
# --broadcast: copy the time the exchange shows on the filing, WITH the offset (+05:30). Without it the sidecar says "broadcast_ts": null
# and the filing counts as unavailable for backtests (it is never guessed from a file date). --source-ref is an optional human note.
# Inbox: $FILINGS_INBOX, else data/filings/inbox in the repository (created 0700). Exit codes: 0 filed (or already filed), 1 a different
# file is already there, 2 a refused argument.
set -euo pipefail

die() { echo "filings-drop: $*" >&2; exit 2; }

usage() {
  cat <<'USAGE'
usage: filings-drop.sh --symbol SYMBOL --kind results|shareholding|board_meeting|announcement [--period-end YYYY-MM-DD]
                       [--broadcast "YYYY-MM-DDThh:mm:ss+05:30"] [--source-ref TEXT] [--] FILE
Copy the time the exchange shows on the filing, with its offset. Without --broadcast the filing is unavailable for backtests.
Inbox: $FILINGS_INBOX, else data/filings/inbox in the repository (created 0700).
USAGE
}

symbol="" kind="" period="" broadcast="" source_ref="" file="" have_broadcast=0
while [ $# -gt 0 ]; do
  case "$1" in
    --symbol) [ $# -ge 2 ] || die "--symbol needs a value"; symbol="$2"; shift 2 ;;
    --kind) [ $# -ge 2 ] || die "--kind needs a value"; kind="$2"; shift 2 ;;
    --period-end) [ $# -ge 2 ] || die "--period-end needs a value"; period="$2"; shift 2 ;;
    --broadcast) [ $# -ge 2 ] || die "--broadcast needs a value"; broadcast="$2"; have_broadcast=1; shift 2 ;;
    --source-ref) [ $# -ge 2 ] || die "--source-ref needs a value"; source_ref="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    --) shift; [ $# -le 1 ] || die "one file at a time"; [ $# -eq 0 ] || file="$1"; break ;;
    -*) die "unknown option $1" ;;
    *) [ -z "$file" ] || die "one file at a time"; file="$1"; shift ;;
  esac
done

[ -n "$symbol" ] || die "missing --symbol"
[ -n "$kind" ] || die "missing --kind"
[ -n "$file" ] || die "missing the file to file"
case "$kind" in results|shareholding|board_meeting|announcement) ;; *) die "unknown kind '$kind' (results, shareholding, board_meeting, announcement)" ;; esac
case "$file" in -*) file="./$file" ;; esac
[ -f "$file" ] || die "not a regular file: $file"

base="$(basename -- "$file")"
[[ "$base" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,119}$ ]] || die "the file name '$base' is refused (letters, digits, . _ - ; at most 120 characters; start with a letter or digit)"
ext="$(printf '%s' "${base##*.}" | tr '[:upper:]' '[:lower:]')"
[ "$base" != "$ext" ] || die "the file has no extension"
case "$ext" in xml|xbrl|csv|pdf|txt) ;; *) die "the extension .$ext is refused (xml, xbrl, csv, pdf, txt)" ;; esac
case "$base" in *.meta.json) die "a sidecar name cannot be filed" ;; esac

row_level=0
if [ "$ext" = "csv" ] && { [ "$kind" = "board_meeting" ] || [ "$kind" = "shareholding" ] || [ "$kind" = "announcement" ]; }; then row_level=1; fi
if [ "$symbol" = "*" ]; then
  [ "$row_level" = 1 ] || die "the symbol * is only for a csv of board meetings, shareholding or announcements"
  name_symbol="ALL"
else
  [[ "$symbol" =~ ^[A-Z0-9\&-]{1,20}$ ]] || die "the symbol '$symbol' is refused (capital letters, digits, & and -; at most 20)"
  name_symbol="${symbol//&/_}"
fi

valid_date() { # YYYY-MM-DD, a real calendar day from the year 2000
  local y=$((10#${1:0:4})) m=$((10#${1:5:2})) d=$((10#${1:8:2})) max
  [ "$y" -ge 2000 ] && [ "$m" -ge 1 ] && [ "$m" -le 12 ] || return 1
  case "$m" in 1|3|5|7|8|10|12) max=31 ;; 4|6|9|11) max=30 ;; *) if [ $((y % 4)) -eq 0 ] && { [ $((y % 100)) -ne 0 ] || [ $((y % 400)) -eq 0 ]; }; then max=29; else max=28; fi ;; esac
  [ "$d" -ge 1 ] && [ "$d" -le "$max" ]
}

if [ -n "$period" ]; then
  [[ "$period" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] && valid_date "$period" || die "--period-end must be a real date, YYYY-MM-DD, from 2000"
fi
if [ -z "$period" ] && { [ "$kind" = "results" ] || { [ "$kind" = "shareholding" ] && [ "$row_level" = 0 ]; }; }; then
  die "--period-end is required for $kind"
fi

if [ "$have_broadcast" = 1 ] && [ -n "$broadcast" ]; then
  re='^([0-9]{4}-[0-9]{2}-[0-9]{2})T([0-9]{2}):([0-9]{2})(:([0-9]{2})(\.[0-9]{1,7})?)?(Z|[+-]([0-9]{2}):([0-9]{2}))$'
  [[ "$broadcast" =~ $re ]] || die "give the time as shown by the exchange with +05:30, for example 2026-07-24T16:12:00+05:30"
  valid_date "${BASH_REMATCH[1]}" && [ "$((10#${BASH_REMATCH[2]}))" -le 23 ] && [ "$((10#${BASH_REMATCH[3]}))" -le 59 ] && [ "$((10#${BASH_REMATCH[5]:-0}))" -le 59 ] \
    || die "--broadcast is not a real date and time from 2000 (give the time as shown by the exchange with +05:30)"
  [ "$((10#${BASH_REMATCH[8]:-0}))" -le 14 ] && [ "$((10#${BASH_REMATCH[9]:-0}))" -le 59 ] || die "--broadcast has an offset beyond +-14:59"
fi
if [ ${#source_ref} -gt 200 ]; then die "--source-ref is longer than 200 characters"; fi
if [[ "$source_ref" == *[\"\\]* || "$source_ref" =~ [[:cntrl:]] ]]; then die "--source-ref may not hold quotes, backslashes or control characters"; fi

max_mb="${FILINGS_MAX_FILE_MB:-20}"
[[ "$max_mb" =~ ^[0-9]{1,3}$ ]] || die "FILINGS_MAX_FILE_MB must be a whole number of 1 to 200"
[ "$((10#$max_mb))" -ge 1 ] && [ "$((10#$max_mb))" -le 200 ] || die "FILINGS_MAX_FILE_MB must be a whole number of 1 to 200"
max_mb=$((10#$max_mb))
size="$(wc -c < "$file" | tr -d ' ')"
[ "$size" -gt 0 ] || die "the file is empty"
[ "$size" -le $((max_mb * 1024 * 1024)) ] || die "the file is larger than ${max_mb} MB"

if command -v sha256sum >/dev/null 2>&1; then sha="$(sha256sum -- "$file" | cut -d' ' -f1)"; else sha="$(shasum -a 256 "$file" | cut -d' ' -f1)"; fi
sha8="${sha:0:8}"

root="$(cd "$(dirname "$0")/.." && pwd)"
inbox="${FILINGS_INBOX:-$root/data/filings/inbox}"
if [ ! -d "$inbox" ]; then
  (umask 077; mkdir -p "$inbox")
  chmod 700 "$inbox"
fi
if [ "$(ls -ld "$inbox" | cut -c5-10)" != "------" ]; then
  echo "filings-drop: warning: $inbox is open to other users; run chmod 700 on it or the filings source will refuse to read it" >&2
fi

target_name="${name_symbol}_${kind}_${period:-none}_${sha8}.${ext}"
target="$inbox/$target_name"
sidecar="$target.meta.json"

if [ "$have_broadcast" = 1 ] && [ -n "$broadcast" ]; then
  broadcast_json="\"$broadcast\""
else
  broadcast_json="null"
fi
period_json="null"; [ -z "$period" ] || period_json="\"$period\""
ref_json="null"; [ -z "$source_ref" ] || ref_json="\"$source_ref\""
meta="$(printf '{\n  "schema": 1,\n  "symbol": "%s",\n  "kind": "%s",\n  "period_end": %s,\n  "broadcast_ts": %s,\n  "source_ref": %s\n}\n' "$symbol" "$kind" "$period_json" "$broadcast_json" "$ref_json")"

if [ -L "$target" ] || [ -L "$sidecar" ]; then
  echo "filings-drop: $target_name or its sidecar is a symbolic link in the inbox; nothing was changed" >&2
  exit 1
fi
if [ -e "$target" ] || [ -e "$sidecar" ]; then
  if [ -f "$target" ] && cmp -s -- "$file" "$target" && [ -f "$sidecar" ] && [ "$(cat "$sidecar")" = "$meta" ]; then
    echo "filings-drop: already filed, nothing to do" >&2
    echo "$target"; echo "$sidecar"
    exit 0
  fi
  echo "filings-drop: $target_name is already in the inbox with different content or a different sidecar; nothing was changed" >&2
  exit 1
fi

# Both files are written under temporary names in the inbox (0600) and moved into place without overwriting; any failure removes what was made.
tmp_file="$(mktemp "$inbox/.filing-copy.XXXXXX")"
tmp_meta="$(mktemp "$inbox/.filing-meta.XXXXXX")"
cleanup() { rm -f -- "$tmp_file" "$tmp_meta"; }
trap cleanup EXIT
cp -- "$file" "$tmp_file"
printf '%s\n' "$meta" > "$tmp_meta"
chmod 600 "$tmp_file" "$tmp_meta"
mv -n -- "$tmp_file" "$target"
if [ -e "$tmp_file" ]; then echo "filings-drop: $target_name appeared while filing; nothing was changed" >&2; exit 1; fi
if ! mv -n -- "$tmp_meta" "$sidecar" || [ -e "$tmp_meta" ]; then
  rm -f -- "$target"
  echo "filings-drop: could not write the sidecar; nothing was filed" >&2
  exit 1
fi
trap - EXIT
if [ "$have_broadcast" = 0 ] || [ -z "$broadcast" ]; then
  echo "filings-drop: no --broadcast given: this filing will be unavailable for backtests until a sidecar carries the exchange's time" >&2
fi
echo "$target"
echo "$sidecar"
