#!/bin/sh
# xd-exec-proxy (ADR 0172): writes the tinyproxy config from the read-only allow-list, then runs tinyproxy in the foreground.
# Only CONNECT to an exact listed host:port passes: FilterURLs matches the CONNECT target "host:port" against an anchored pattern, FilterDefaultDeny refuses
# everything else (plain-HTTP forwarding included, whose URL never matches), and ConnectPort refuses any other port. An empty or malformed list stops the start.
set -eu

list=/etc/xd/allowlist.txt
conf=/tmp/tinyproxy.conf
filter=/tmp/tinyproxy.filter
: > "$filter"
ports=""
count=0

while IFS= read -r line || [ -n "$line" ]; do
  case "$line" in '' | '#'*) continue ;; esac
  if ! printf '%s' "$line" | grep -Eq '^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+:[0-9]{1,5}$'; then
    echo "xd-exec-proxy: refusing a malformed allow-list line" >&2
    exit 1
  fi
  host=${line%:*}
  port=${line##*:}
  printf '^%s:%s$\n' "$(printf '%s' "$host" | sed 's/\./\\./g')" "$port" >> "$filter"
  case " $ports " in *" $port "*) ;; *) ports="$ports $port" ;; esac
  count=$((count + 1))
done < "$list"

if [ "$count" -eq 0 ]; then
  echo "xd-exec-proxy: the allow-list is empty" >&2
  exit 1
fi

{
  echo 'Port 8888'
  echo 'Timeout 60'
  echo 'MaxClients 16'
  echo 'LogLevel Notice'
  echo 'DisableViaHeader Yes'
  echo "Filter \"$filter\""
  echo 'FilterType ere'
  echo 'FilterURLs On'
  echo 'FilterCaseSensitive Yes'
  echo 'FilterDefaultDeny Yes'
  for p in $ports; do echo "ConnectPort $p"; done
} > "$conf"

exec tinyproxy -d -c "$conf"
