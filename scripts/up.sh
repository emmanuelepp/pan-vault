#!/usr/bin/env bash
# Brings up the whole pan-vault platform on a local minikube cluster.
# Zero configuration: secrets are generated here, everything else comes from git.
#
#   ./scripts/up.sh
#
# Optional environment variables:
#   PROFILE            minikube profile name            (default: pan-vault)
#   MINIKUBE_MEMORY    memory for the cluster            (default: 6g)
#   MINIKUBE_CPUS      CPUs for the cluster              (default: 4)
#   SEALING_KEY_FILE   maintainer only: a Sealed Secrets key backup. When set, the
#                      secrets committed in gitops/secrets/ are used instead of
#                      generating new ones.
set -euo pipefail

PROFILE="${PROFILE:-pan-vault}"
MINIKUBE_MEMORY="${MINIKUBE_MEMORY:-6g}"
MINIKUBE_CPUS="${MINIKUBE_CPUS:-4}"
SEALING_KEY_FILE="${SEALING_KEY_FILE:-}"

SEALED_SECRETS_VERSION="v0.39.1"
ARGOCD_VERSION="v3.5.1"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

say()  { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
ok()   { printf '    \033[32m%s\033[0m\n' "$*"; }
die()  { printf '    \033[31merror: %s\033[0m\n' "$*" >&2; exit 1; }

say "Checking prerequisites"
for tool in docker minikube kubectl openssl curl; do
  command -v "$tool" >/dev/null 2>&1 || die "$tool is not installed"
  ok "$tool"
done
docker info >/dev/null 2>&1 || die "docker is installed but not running"

INOTIFY_HINT='sudo sysctl -w fs.inotify.max_user_instances=512 fs.inotify.max_user_watches=524288'
instances="$(sysctl -n fs.inotify.max_user_instances 2>/dev/null || echo 0)"
if [ "$instances" -lt 512 ]; then
  printf '    \033[33mwarning: fs.inotify.max_user_instances is %s; if kube-proxy or Calico fail to start, run:\n             %s\033[0m\n' "$instances" "$INOTIFY_HINT"
fi

say "Cluster: minikube profile '$PROFILE' with Calico"
if minikube status -p "$PROFILE" >/dev/null 2>&1; then
  ok "already exists, reusing it"
else

  minikube start -p "$PROFILE" --cni=calico --memory="$MINIKUBE_MEMORY" --cpus="$MINIKUBE_CPUS"
fi
kubectl config use-context "$PROFILE" >/dev/null
if ! kubectl -n kube-system rollout status daemonset/calico-node --timeout=300s >/dev/null; then
  die "Calico did not become ready. On Linux this is usually the inotify limit: run '$INOTIFY_HINT' and start again"
fi
ok "Calico ready"

say "Ingress controller"
minikube addons enable ingress -p "$PROFILE" >/dev/null
kubectl -n ingress-nginx rollout status deployment/ingress-nginx-controller --timeout=300s >/dev/null
ok "ingress-nginx ready"

say "Sealed Secrets controller"
if [ -n "$SEALING_KEY_FILE" ]; then
  kubectl apply -f "$SEALING_KEY_FILE" >/dev/null
  ok "maintainer sealing key installed"
fi
kubectl apply -f "https://github.com/bitnami-labs/sealed-secrets/releases/download/${SEALED_SECRETS_VERSION}/controller.yaml" >/dev/null
kubectl -n kube-system rollout status deployment/sealed-secrets-controller --timeout=180s >/dev/null
ok "controller ready"

say "ArgoCD ${ARGOCD_VERSION}"
kubectl create namespace argocd --dry-run=client -o yaml | kubectl apply -f - >/dev/null
kubectl apply -n argocd -f "https://raw.githubusercontent.com/argoproj/argo-cd/${ARGOCD_VERSION}/manifests/install.yaml" >/dev/null
kubectl -n argocd rollout status deployment/argocd-server --timeout=300s >/dev/null
ok "ArgoCD ready"

say "Secrets"
kubectl apply -f k8s/namespace.yaml >/dev/null
kubectl create namespace monitoring --dry-run=client -o yaml | kubectl apply -f - >/dev/null

if [ -n "$SEALING_KEY_FILE" ]; then
  ok "using the sealed secrets committed in gitops/secrets/"
else

  if ! kubectl -n cde get secret pan-vault-dek >/dev/null 2>&1; then
    kubectl -n cde create secret generic pan-vault-dek \
      --from-literal=PanCrypto__Dek="$(openssl rand -base64 32)" >/dev/null
    ok "DEK generated (AES-256, 32 random bytes)"
  fi

  if ! kubectl -n cde get secret pan-vault-tls >/dev/null 2>&1; then
    tmp="$(mktemp -d)"
    openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
      -keyout "$tmp/tls.key" -out "$tmp/tls.crt" \
      -subj "/CN=pan-vault.local/O=pan-vault" \
      -addext "subjectAltName=DNS:pan-vault.local" >/dev/null 2>&1
    kubectl -n cde create secret tls pan-vault-tls \
      --cert="$tmp/tls.crt" --key="$tmp/tls.key" >/dev/null
    rm -rf "$tmp"
    ok "self-signed TLS certificate for pan-vault.local"
  fi

  if ! kubectl -n monitoring get secret grafana-admin >/dev/null 2>&1; then
    kubectl -n monitoring create secret generic grafana-admin \
      --from-literal=admin-user=admin \
      --from-literal=admin-password="$(openssl rand -base64 24)" >/dev/null
    ok "Grafana admin password generated"
  fi
fi

say "ArgoCD Applications (git is the source of truth from here on)"
kubectl apply -f gitops/pan-vault-app.yaml -f gitops/monitoring-app.yaml -f gitops/monitoring-config-app.yaml >/dev/null
if [ -n "$SEALING_KEY_FILE" ]; then
  kubectl apply -f gitops/pan-vault-secrets-app.yaml >/dev/null
fi
ok "applied"

say "Waiting for every Application to be Synced and Healthy (first run pulls images, allow up to 15 minutes)"
deadline=$(( $(date +%s) + 900 ))
while :; do
  status="$(kubectl -n argocd get applications -o jsonpath='{range .items[*]}{.metadata.name}={.status.sync.status}/{.status.health.status} {end}')"
  pending=0
  for item in $status; do
    case "$item" in *=Synced/Healthy) ;; *) pending=1 ;; esac
  done
  if [ "$pending" -eq 0 ] && [ -n "$status" ]; then break; fi
  [ "$(date +%s)" -lt "$deadline" ] || die "timed out, current state: $status"
  printf '    %s\r' "$status"
  sleep 10
done
printf '\n'
for item in $status; do ok "$item"; done

say "Done"
cat <<EOF

    Open the web UIs (ArgoCD, Grafana, Prometheus, Alertmanager):
        ./scripts/open.sh

    Prove the PCI controls against the running cluster:
        ./scripts/smoke.sh

    Tear everything down:
        ./scripts/down.sh

    Optional, to call the API by name instead of IP:
        echo "$(minikube ip -p "$PROFILE") pan-vault.local" | sudo tee -a /etc/hosts
        curl -k https://pan-vault.local/healthz
EOF
