#!/usr/bin/env bash
# Proves the PCI DSS controls against the running local cluster.
# Same checks the CI pipeline runs on every pull request, through the Ingress.
set -uo pipefail

PROFILE="${PROFILE:-pan-vault}"
kubectl config use-context "$PROFILE" >/dev/null

IP="$(minikube ip -p "$PROFILE")"
HOST="pan-vault.local"
CURL="curl -sk --max-time 10 --resolve ${HOST}:443:${IP} --resolve ${HOST}:80:${IP}"
BASE="https://${HOST}"

pass=0; fail=0
check() {

  if [ "$2" = "$3" ]; then
    printf '  \033[32mPASS\033[0m  %-58s %s\n' "$1" "$3"; pass=$((pass + 1))
  else
    printf '  \033[31mFAIL\033[0m  %-58s got %s, expected %s\n' "$1" "$3" "$2"; fail=$((fail + 1))
  fi
}
code() { $CURL -o /dev/null -w '%{http_code}' "$@"; }
post() { code -X POST "$BASE/tokens" -H 'Content-Type: application/json' -d "$1"; }

echo
echo "Transport (Req 4.2.1)"
check "HTTPS /healthz"                              200 "$(code "$BASE/healthz")"
check "HTTP is redirected to HTTPS"                 308 "$(code "http://${HOST}/healthz")"

echo
echo "Sensitive authentication data is rejected (Req 3.3.1)"
check "cvv at the top level"                        400 "$(post '{"pan":"4111111111111111","cvv":"123"}')"
check "cvv nested in an object"                     400 "$(post '{"pan":"4111111111111111","card":{"cvv":"123"}}')"
check "pin inside an array"                         400 "$(post '{"pan":"4111111111111111","items":[{"pin":"1234"}]}')"
check "cvv_2 naming variant"                        400 "$(post '{"pan":"4111111111111111","cvv_2":"123"}')"

echo
echo "Tokenization and masking (Req 3.4.1, 3.5.1)"
check "invalid Luhn is rejected"                    400 "$(post '{"pan":"4111111111111112"}')"
check "valid PAN is tokenized"                      201 "$(post '{"pan":"4111111111111111"}')"
token="$($CURL -X POST "$BASE/tokens" -H 'Content-Type: application/json' -d '{"pan":"378282246310005"}' | grep -o 'tok_[a-f0-9]*')"
body="$($CURL "$BASE/tokens/$token")"
check "GET returns the masked PAN"                  yes "$(echo "$body" | grep -q '"maskedPan":"378282\*\*\*\*\*0005"' && echo yes || echo no)"
check "GET never returns the full PAN"              no  "$(echo "$body" | grep -q '378282246310005' && echo yes || echo no)"
check "GET never returns the ciphertext"            no  "$(echo "$body" | grep -qi 'cipher' && echo yes || echo no)"
check "unknown token is 404"                        404 "$(code "$BASE/tokens/tok_does_not_exist")"

echo
echo "Unnecessary functionality is absent (Req 2.2.4)"
check "Swagger is not served"                       404 "$(code "$BASE/swagger")"
check "metrics are not served through the Ingress"  404 "$(code "$BASE/metrics")"
check "not even with a forged Host header"          404 "$(code -H "Host: ${HOST}:9090" "$BASE/metrics")"

echo
echo "Logs carry no cardholder data (Req 10.2.1)"
logs="$(kubectl -n cde logs deploy/pan-vault --tail=500 2>/dev/null)"
check "no 12 to 19 digit sequence in the pod logs"  no "$(echo "$logs" | sed -E 's/tok_[0-9a-f]{32}//g' | grep -Eq '[0-9]{12,19}' && echo yes || echo no)"

echo
echo "Runtime hardening (Req 2.2.6, 7.2.1)"
pod="$(kubectl -n cde get pod -l app=pan-vault -o jsonpath='{.items[0].metadata.name}')"
check "runs as non-root"                            true  "$(kubectl -n cde get pod "$pod" -o jsonpath='{.spec.securityContext.runAsNonRoot}')"
check "read-only root filesystem"                   true  "$(kubectl -n cde get pod "$pod" -o jsonpath='{.spec.containers[0].securityContext.readOnlyRootFilesystem}')"
check "all capabilities dropped"                    '["ALL"]' "$(kubectl -n cde get pod "$pod" -o jsonpath='{.spec.containers[0].securityContext.capabilities.drop}')"
check "no Kubernetes API token mounted"             no "$(kubectl -n cde get pod "$pod" -o jsonpath='{range .spec.volumes[*]}{.name} {end}' | grep -q kube-api-access && echo yes || echo no)"
check "no shell inside the container"               absent "$(kubectl -n cde exec "$pod" -- /bin/sh -c 'true' >/dev/null 2>&1 && echo present || echo absent)"

echo
echo "Network segmentation (Req 1.2.1, 1.3.1)"
deny="$(kubectl run smoke-deny --rm -i --restart=Never --image=busybox:1.36 -- \
  wget -qO- --timeout=5 "http://pan-vault.cde:8080/healthz" 2>&1 || true)"
check "pod in another namespace is blocked"         yes "$(echo "$deny" | grep -q 'timed out' && echo yes || echo no)"
allow="$(kubectl -n cde run smoke-allow --rm -i --restart=Never --labels=app=gateway --image=busybox:1.36 -- \
  wget -qO- --timeout=5 "http://pan-vault:8080/healthz" 2>&1 || true)"
check "authorized pod in the CDE is allowed"        yes "$(echo "$allow" | grep -q '"status":"ok"' && echo yes || echo no)"

echo
echo "GitOps self-heal (Req 6.5.1, 6.5.2)"
kubectl -n cde delete deployment pan-vault --wait=false >/dev/null 2>&1
healed=no
for _ in $(seq 1 24); do
  if kubectl -n cde get deployment pan-vault >/dev/null 2>&1; then healed=yes; break; fi
  sleep 5
done
check "deleted Deployment is recreated by ArgoCD"   yes "$healed"
kubectl -n cde rollout status deployment/pan-vault --timeout=180s >/dev/null 2>&1

echo
printf '%d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]
