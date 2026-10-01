# Local GraphHopper

The engine and explicit application provider switch are ready for local manual
testing. They create no cloud resources and require no ORS or Gemini requests. Requirements:
Docker with Linux containers, Docker Compose v2+, PowerShell 7.4+, and native
`docker`/`curl` on PATH. The launcher works with Windows or Linux executable names.

## Commands

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/start-graphhopper.ps1
pwsh -NoProfile -File tools/start-graphhopper.ps1 -SkipBuild -Restart -WaitSeconds 120
pwsh -NoProfile -File tools/start-graphhopper.ps1 -Stop
```

The service remains running after the launcher exits. `-Stop` stops only the
owned service in Compose project `ai-bike-gh-local`, after checking its data
mount, ownership manifest and engine revision. It does not remove data, stop
the application, prune Docker, or touch other containers. There is no automatic
restart policy: an import failure stays visible. A readiness timeout leaves the
service/data in place for inspection.

Engine: <http://127.0.0.1:8989>. Readiness: <http://127.0.0.1:8989/info>.
Only the application port is published, on IPv4 host loopback. The admin
connector stays inside the container on its loopback. No accounts or tokens
are required. An explicit empty Compose env file avoids loading application
credentials from `.env`.

`-DataDirectory`, `-ProjectName` and `-Port` support isolated instances; repeat
the same arguments when stopping/restarting. The default ignored location is
`infra/graphhopper/artifacts/regional`. Use `-SkipBuild` only with an image already
built from the current engine files; normal launch rebuilds the small config layer.

## Engine And Data Identity

The Dockerfile uses pinned Maven/Temurin image digests and Maven Dependency Plugin
3.8.1 to retrieve the **released** `com.graphhopper:graphhopper-web:11.1` JAR from
Maven Central. It verifies its SHA-256 before copying it into the Java 25 JRE
image. This is an image build using the released JAR, not a source compilation.
The source reference is upstream tag `11.1` at commit
`ebb578f4e94db72e82f2f8d3bc69417758e16e1c`. The source POM's snapshot label is
not used as artifact identity. Runtime Java is Temurin `25.0.4.1+1`.

Pinned identities:

| Input | SHA-256 |
| --- | --- |
| GraphHopper 11.1 JAR | `8462f758d9ea49edaded557cec5c687a0a24f004ec371cd4daaeb0534824ea33` |
| Geofabrik Israel-and-Palestine `260929` PBF | `f98a8b9ad7fb3df19c846d3a562d9394abc5657173faeddcb033a53f4aa701f3` |
| Current `config.yml` | `96efc56f225af83a7ddd990baa238afd3caec0ba0c68c08ea6f4f3691a10cce8` |
| Current `road.json` | `94989d7dfa4196dcf68936e0edb08da579445925734ed9612b26f32894edebb1` |

The public PBF is 119,964,524 bytes, with upstream MD5
`d5325e11f553ae0580d01d79c9687e8b`; downloads must match both pinned checksums.
The `260930` sidecar returned HTTP 503/timeouts during setup, so the fully
verifiable preceding snapshot was selected explicitly. Geofabrik rotates daily
archives: if this URL disappears, preserve the verified local PBF; selecting
a new snapshot requires an intentional URL/hash update, not a latest-file fallback.

Sources verified on 2026-10-01:

- [Official release](https://github.com/graphhopper/graphhopper/releases/tag/11.1)
- [Maven artifact checksum](https://repo.maven.apache.org/maven2/com/graphhopper/graphhopper-web/11.1/graphhopper-web-11.1.jar.sha256)
- [Pinned source config](https://github.com/graphhopper/graphhopper/blob/ebb578f4e94db72e82f2f8d3bc69417758e16e1c/config-example.yml)
- [Geofabrik region](https://download.geofabrik.de/asia/israel-and-palestine.html) and [snapshot checksum](https://download.geofabrik.de/asia/israel-and-palestine-260929.osm.pbf.md5)
- [11.1 SRTM provider](https://github.com/graphhopper/graphhopper/blob/11.1/core/src/main/java/com/graphhopper/reader/dem/SRTMProvider.java)

The data root contains `input/`, `graphs/<identity>/`, and
`elevation/srtm3-kurviger/`. The graph identity hashes the JAR, PBF/XML,
configuration, road model and network-size override. Changed inputs select a
new graph directory and log `GRAPH_IMPORT`; existing compatible inputs log
`GRAPH_REUSE`. Old variants are preserved. An atomic completion marker is
written only after explicit import succeeds. Missing markers, mismatching
identity manifests, or missing essential graph files fail closed. There is
no silent cleanup/reimport of an interrupted graph. Preserve that directory
for diagnosis and choose a fresh data directory with a distinct project name.
The data-root lock covers import, graph serving and shared elevation writes.

Evidence is ignored alongside data: `readiness.json`, `readiness-history.jsonl`
(subsequent launches), `resource-samples.json`, `route-verification.json`, input
download metadata, and each graph's `identity.txt`. The JSON records resolved
image identity and actual graph reuse. Exact config/model hashes remain in
`identity.txt`; LF Git attributes keep their bytes stable across platforms.
The released JAR and base images are pinned; Ubuntu curl is version-pinned,
but its transitive OS package dependencies are not a hermetic build lock.

## Routing Contract

Profile `road` composes upstream `racingbike.json`, `bike_elevation.json` and
the local conservative model. Both racing-bike and ordinary `bike_access`
restrictions remain active. Known unpaved surfaces, steps, ferries, fords and
construction are excluded. Cycleways/paths/footways are not removed by the
automotive example's import exclusion list. The stale `sac_scale` hint in
the upstream model comment is not an encoded value in 11.1; `hike_rating`
is retained instead.

CH and LM are disabled. GraphHopper 11.1 rejects native `round_trip` with a CH
solver; flexible routing makes the adapter's requests work without
`ch.disable`/`lm.disable` properties. Engine bounds: 15-second routing timeout,
1,000,000 visited nodes. Requests may use `timeout_ms: 14000`. POST points
use `[longitude, latitude]`, and unencoded responses contain a 3D LineString
when `elevation: true`. `surface`, `road_class`, `road_environment` are available
as path details. SRTM uses the public Kurviger SRTM3 mirror, with bilinear
interpolation and a persistent elevation cache, without credentials.

Missing/unknown surfaces are not asphalt evidence. Road classes do not prove
cycling legality or safety. SRTM has finite resolution/voids and cannot prove
complete elevation coverage; the upstream provider can represent missing
tiles as sea level. Unknown roads, traffic, border/access restrictions and
stale/incomplete OSM tags still require independent judgment. Conservative
exclusions can disconnect routes. Native loop distance is heuristic and some
seeds return zero-distance paths (also observed on the tiny synthetic fixture).
Changing engines is not route-quality or safety proof.

## Verification

```powershell
# CI/Ubuntu and Windows: builds image, no regional download.
pwsh -NoProfile -File infra/graphhopper/tests/smoke.ps1
# Reuse a current image:
pwsh -NoProfile -File infra/graphhopper/tests/smoke.ps1 -SkipBuild
# Already-running regional engine, public example locations only:
pwsh -NoProfile -File infra/graphhopper/tests/regional.ps1
```

Synthetic tests use a fresh unique project/data root on loopback port 18989.
They generate a deterministic, flat 100m SRTM3 cache tile before import; no
elevation-network request is needed. They check 100m 3D elevation, profile and
details, paved detours around shortcuts, bicycle prohibition, cycleway retention,
native loops without CH overrides, graph reuse and partial-cache refusal with
network disabled. They stop their owned service in `finally` and preserve
evidence, including failed imports. First-run image dependencies are public
downloads; subsequent routing is local. Regional tests output only numeric
summaries, never response bodies or user GPS.

Observed on 2026-10-01, not a benchmark or quality qualification:

- Docker reported 4 CPUs and 4,106,862,592 bytes (3.82 GiB) available memory.
- Engine limit: 3 GiB, no swap allowance beyond that, 2 CPUs; JVM heap maximum
  2304 MiB, initial 256 MiB, 2 active processors; memory-mapped graph/elevation.
- Regional import to readiness: 38.3 seconds; cgroup peak approximately
  1335 MiB. One mid-import sample was 1.16 GiB and 108% CPU.
- Restart: same graph identity, no reimport, readiness in 7.4 seconds;
  cgroup peak 211,591,168 bytes (about 202 MiB) at readiness.
- Regional public A-B sample: 2463m, 30 geometry points, 142ms HTTP time.
  Native 10km loop request: 9794m, 161 points, 60ms. Both had nonempty details
  and 3D elevation. Timings are single samples, not performance guarantees.
- Persisted regional graph: 105,956,239 bytes; elevation cache: 40,289,869 bytes;
  PBF: 119,964,524 bytes. Runtime memory includes file mappings/page cache;
  Docker stats and cgroup memory peaks measure different accounting views.

## Application Manual Testing

After engine readiness, start the app explicitly:

```powershell
pwsh -NoProfile -File tools/start-local.ps1 -RoutingProvider GraphHopper
# App stop (does not stop GraphHopper):
pwsh -NoProfile -File tools/start-local.ps1 -Stop
# Offline launcher regression checks:
pwsh -NoProfile -File tools/test-local-routing.ps1
```

Use the printed frontend URL; occupied 5173/5080 ports cause new ports to be
selected without stopping unrelated processes. Choose Manual and keep AI
refinement off. This session explicitly empties ORS/Gemini credentials, including
inherited environment values, overriding Development User Secrets. Prompt mode
is therefore intentionally unavailable. The default launcher remains ORS for
backward compatibility. A running session with another provider cannot be
silently reused. `/health` tests API health, not ongoing engine readiness.

The API selects `Routing:Provider=GraphHopper`, with a root HTTP(S)
`Routing:GraphHopper:BaseUrl` and `Routing:GraphHopper:Profile=road`. It shares the
existing `IRoutingProvider`, contracts, quality filters and GPX/naming workflow.
Client requests are bounded to 15 seconds and 8 MiB, redirects disabled, no
retries or ORS fallback. The native loop's ordinary unsnappable generated-point
error is recognized only in a round-trip request; configuration errors remain
distinct and raw upstream locations are never returned in public errors.

App checks on 2026-10-01 used public control points, three routing attempts per
loop, and **zero ORS/Gemini calls**:

| Scenario | Observed result |
| --- | --- |
| Tel Aviv area A-B | 12.06 km, 37.9 min; 190 geometry/elevation points, 27 segment runs |
| Tel Aviv loop, 35-45 km | Two closed accepted candidates: 42.89 and 41.63 km |
| Haifa loop, 35-45 km | Two closed accepted candidates: 39.73 and 39.52 km; remaining seed found no route |
| Tel Aviv loop, 20 km target | No accepted candidate; 17.39, 24.94 and 14.05 km excluded by unchanged tolerance |

Desktop 1440x1000 and mobile 390x844 live-browser checks verified a nonblank map
with the track visible, elevation profile, segment metadata, no page overflow or
page errors, and matching named GPX download. Screenshots/raw outputs remain in
ignored local artifacts. These samples are functional checks, **not evidence of
better overall route quality than ORS**. Loop warnings still report unknown
surfaces and paths/tracks/footways; inspect these before riding. A-B time is a
provider estimate, not a personal speed prediction. Keep distance/time constraints
unchanged when comparing providers, and record excluded attempts as well as
successful ones. Cloud deployment remains a separate joint task.
