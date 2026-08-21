#!/usr/bin/env bash
# Port-forwards every web UI of the platform and opens them in the browser.
# Safe to run again at any time. Stop the port-forwards with: ./scripts/open.sh --stop
set -euo pipefail

PROFILE="${PROFILE:-pan-vault}"
PIDFILE="/tmp/pan-vault-port-forwards.pids"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

stop() {
  if [ -f "$PIDFILE" ]; then
    xargs -r kill < "$PIDFILE" 2>/dev/null || true
    rm -f "$PIDFILE"
    echo "    port-forwards stopped"
  fi
}

if [ "${1:-}" = "--stop" ]; then stop; exit 0; fi

kubectl config use-context "$PROFILE" >/dev/null
stop >/dev/null 2>&1 || true

say "Starting port-forwards"
forward() {

  kubectl -n "$1" port-forward "svc/$2" "$3" >/dev/null 2>&1 &
  echo $! >> "$PIDFILE"
}
forward argocd     argocd-server                             8443:443
forward monitoring monitoring-grafana                        3000:80
forward monitoring monitoring-kube-prometheus-prometheus     9090:9090
forward monitoring monitoring-kube-prometheus-alertmanager   9093:9093
sleep 3

ARGOCD_PASSWORD="$(kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath='{.data.password}' | base64 -d)"
GRAFANA_PASSWORD="$(kubectl -n monitoring get secret grafana-admin -o jsonpath='{.data.admin-password}' | base64 -d)"

say "Web UIs"
cat <<EOF

    ArgoCD        https://localhost:8443      user: admin    password: ${ARGOCD_PASSWORD}
    Grafana       http://localhost:3000       user: admin    password: ${GRAFANA_PASSWORD}
                  dashboard "pan-vault" is under Dashboards
    Prometheus    http://localhost:9090       targets: /targets    alerts: /alerts
    Alertmanager  http://localhost:9093

    The API itself has no web UI: Swagger is disabled outside development on
    purpose (PCI DSS Req 2.2.4). Use ./scripts/smoke.sh or curl.
    The ArgoCD and Grafana certificates are self-signed: accept the browser warning.

EOF

opener=""
if command -v xdg-open >/dev/null 2>&1; then opener="xdg-open"; elif command -v open >/dev/null 2>&1; then opener="open"; fi
if [ -n "$opener" ]; then
  for url in https://localhost:8443 http://localhost:3000/dashboards http://localhost:9090/targets http://localhost:9093; do
    "$opener" "$url" >/dev/null 2>&1 || true
    sleep 1
  done
fi

echo "    Port-forwards keep running in the background. Stop them with: ./scripts/open.sh --stop"
