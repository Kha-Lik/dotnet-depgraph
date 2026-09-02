#!/usr/bin/env bash
set -euo pipefail
fixture_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
feed="$fixture_root/local-feed"
fixture_packages="$fixture_root/.packages"
mkdir -p "$feed" "$fixture_packages"
NUGET_PACKAGES="$fixture_packages" dotnet pack "$fixture_root/PackageSources/Serialization/Serialization.csproj" -o "$feed" --nologo
NUGET_PACKAGES="$fixture_packages" dotnet restore "$fixture_root/PackageSources/Storage/Storage.csproj" --configfile "$fixture_root/NuGet.config" --nologo
NUGET_PACKAGES="$fixture_packages" dotnet pack "$fixture_root/PackageSources/Storage/Storage.csproj" -o "$feed" --no-restore --nologo
NUGET_PACKAGES="$fixture_packages" dotnet restore "$fixture_root/PackageSources/Feature/Feature.csproj" --configfile "$fixture_root/NuGet.config" --nologo
NUGET_PACKAGES="$fixture_packages" dotnet pack "$fixture_root/PackageSources/Feature/Feature.csproj" -o "$feed" --no-restore --nologo
NUGET_PACKAGES="$fixture_packages" dotnet pack "$fixture_root/Representative/Shared/Shared.csproj" -o "$feed" --nologo
for project in "$fixture_root"/Representative/Main/App.csproj "$fixture_root"/Representative/ToolA/ToolA.csproj "$fixture_root"/Representative/ToolB/ToolB.csproj "$fixture_root"/Representative/Isolated/Isolated.csproj "$fixture_root"/Representative/DuplicateA/Foo.csproj "$fixture_root"/Representative/DuplicateB/Foo.csproj; do
  NUGET_PACKAGES="$fixture_packages" dotnet restore "$project" --configfile "$fixture_root/NuGet.config" --nologo
done
