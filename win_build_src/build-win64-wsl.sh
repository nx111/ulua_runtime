#!/bin/bash
# Compatibility entrypoint for older WSL workflows.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$SCRIPT_DIR/build-win64.sh" "$@"
