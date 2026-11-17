#!/bin/bash
# Times one Edit Images open's frame work on a baseline commit and on the working tree, with the
# same media and options, and prints both tables. Every argument is passed to the benchmark.
#
#   tools/benchmark/run.sh --media ~/bench-media --choices 6,10,20
#
# BASELINE picks the commit to compare against. Default HEAD, so uncommitted changes are measured
# against the last commit. Set BASELINE=none to measure the working tree alone.

set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(git -C "$here" rev-parse --show-toplevel)"
baseline="${BASELINE:-HEAD}"
# The real path: on macOS the temp folder sits behind the /var symlink, and building through it
# confuses MSBuild's relative paths, so the bundled fonts' licences collide as embedded resources.
work="$(cd "$(mktemp -d)" && pwd -P)"

cleanup() {
    git -C "$repo" worktree remove --force "$work/baseline" >/dev/null 2>&1 || true
    rm -rf "$work"
}
trap cleanup EXIT

build() {
    local plugin="$1" out="$2"
    dotnet build "$here/Benchmark.csproj" -c Release -nologo -v q \
        -p:PluginProject="$plugin" --artifacts-path "$out" >"$out.log" 2>&1 \
        || { cat "$out.log"; exit 1; }
}

if [[ "$baseline" != "none" ]]; then
    git -C "$repo" worktree add --detach "$work/baseline" "$baseline" >/dev/null 2>&1
    build "$work/baseline/Jellyfin.Plugin.ArtworkGenerator/Jellyfin.Plugin.ArtworkGenerator.csproj" "$work/baseline-build"
fi

build "$repo/Jellyfin.Plugin.ArtworkGenerator/Jellyfin.Plugin.ArtworkGenerator.csproj" "$work/current-build"

if [[ "$baseline" != "none" ]]; then
    dotnet "$(find "$work/baseline-build/bin" -name frame-benchmark.dll | head -1)" --label "$(git -C "$repo" rev-parse --short "$baseline")" "$@"
    echo
fi

dotnet "$(find "$work/current-build/bin" -name frame-benchmark.dll | head -1)" --label "working tree" "$@"
