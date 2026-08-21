#!/usr/bin/env bash
# Removes everything: port-forwards and the minikube profile.
set -euo pipefail

PROFILE="${PROFILE:-pan-vault}"

"$(dirname "$0")/open.sh" --stop >/dev/null 2>&1 || true
minikube delete -p "$PROFILE"
echo "Removed. The secrets that were generated for this cluster are gone with it."
