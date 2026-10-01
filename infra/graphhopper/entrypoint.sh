#!/bin/sh
set -eu
cd /opt/graphhopper
osm=${GH_OSM_FILE:?GH_OSM_FILE is required}
network=${GH_MIN_NETWORK_SIZE:-200}
case "$osm" in /data/input/*.osm|/data/input/*.pbf) ;; *) echo 'OSM input must be under /data/input' >&2; exit 2;; esac
case "$network" in 0|200) ;; *) echo 'Unexpected network size override' >&2; exit 2;; esac
test -s "$osm"
mkdir -p /data/graphs /data/elevation/srtm3-kurviger
# Lock the complete data root, including the shared elevation cache, until server exit.
exec 9>/data/engine.lock
flock -n 9 || { echo 'This data directory is already in use' >&2; exit 3; }
manifest=$(mktemp)
trap 'rm -f "$manifest"' EXIT
{
    echo 'graphhopper=11.1'
    echo 'source=ebb578f4e94db72e82f2f8d3bc69417758e16e1c'
    echo "prepare.min_network_size=$network"
    sha256sum graphhopper.jar config.yml road.json
    printf '%s  osm-input\n' "$(sha256sum "$osm" | cut -d ' ' -f 1)"
} > "$manifest"
identity=$(sha256sum "$manifest" | cut -d ' ' -f 1)
graph=/data/graphs/$identity
if test -d "$graph"; then
    if ! test -f "$graph/import-complete.sha256" || ! cmp -s "$manifest" "$graph/identity.txt" || ! test "$(cat "$graph/import-complete.sha256")" = "$identity"; then
        echo "Refusing partial/incompatible graph: $graph. Preserve it and choose a fresh data directory." >&2
        exit 4
    fi
    for file in nodes edges geometry properties location_index; do
        test -s "$graph/$file" || { echo "Incomplete graph: missing $file" >&2; exit 4; }
    done
    echo "GRAPH_REUSE $identity"
    reused=true
else
    mkdir "$graph"
    cp "$manifest" "$graph/identity.txt"
    echo "GRAPH_IMPORT $identity"
    java "-Ddw.graphhopper.datareader.file=$osm" "-Ddw.graphhopper.graph.location=$graph" \
        "-Ddw.graphhopper.prepare.min_network_size=$network" -jar graphhopper.jar import config.yml
    printf '%s\n' "$identity" > "$graph/import-complete.sha256.tmp"
    mv "$graph/import-complete.sha256.tmp" "$graph/import-complete.sha256"
    reused=false
fi
printf '{"graphIdentity":"%s","graphReused":%s}\n' "$identity" "$reused" > /data/startup.json.tmp
mv /data/startup.json.tmp /data/startup.json
exec java "-Ddw.graphhopper.datareader.file=$osm" "-Ddw.graphhopper.graph.location=$graph" \
    "-Ddw.graphhopper.prepare.min_network_size=$network" -jar graphhopper.jar server config.yml
