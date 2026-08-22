#!/usr/bin/env bash
set -euo pipefail

configuration="${1:-Staging}"

dotnet clean Icod.LineEditor.sln -c "$configuration"
dotnet restore Icod.LineEditor.sln
dotnet build Icod.LineEditor.sln -c "$configuration" --no-restore
dotnet test Icod.LineEditor.sln -c "$configuration" --no-build