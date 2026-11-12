#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/CursorMeter.App/CursorMeter.App.csproj"
publish="$root/src/CursorMeter.App/bin/Release/net8.0/linux-x64/publish"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n 1)"
if [[ -z "$version" ]]; then
  echo "Version is missing from $project" >&2
  exit 1
fi

dotnet publish "$project" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true

if [[ ! -x "$publish/CursorMeter" ]]; then
  echo "Published executable was not found: $publish/CursorMeter" >&2
  exit 1
fi

name="CursorMeter-$version-linux-x64"
staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
mkdir -p "$staging/$name"
install -m 755 "$publish/CursorMeter" "$staging/$name/CursorMeter"
install -m 755 "$root/scripts/linux/install.sh" "$root/scripts/linux/uninstall.sh" "$staging/$name/"
install -m 644 "$root/scripts/linux/cursormeter.desktop" "$root/scripts/linux/cursormeter.svg" "$staging/$name/"

mkdir -p "$root/dist"
archive="$root/dist/$name.tar.gz"
tar -C "$staging" --owner=0 --group=0 --numeric-owner -czf "$archive" "$name"

echo "Archive: $archive"
