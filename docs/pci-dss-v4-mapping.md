# PCI DSS v4.0.1 control mapping

This document maps every technical control implemented in `pan-vault` to the
PCI DSS v4.0.1 requirement it aligns with, to the repository artifact that
contains it, and to the command that verifies it.

> **This project is not PCI compliant and does not claim to be.** It is a
> reference implementation of technical controls *aligned to* the standard's
> requirements. Real compliance involves organizational scope, sustained
> operational evidence over time, and validation by a QSA or through an SAQ, none
> of which applies here. The [Out of scope](#out-of-scope) section lists what is
> deliberately not covered.

## Summary

| # | Control | Requirement | Artifact |
|---|---------|-------------|----------|
| 1 | PAN encrypted with AES-256-GCM | 3.5.1 | `src/PanVault.Api/Crypto/PanCrypto.cs` |
| 2 | SAD rejected, never stored | 3.3.1, 3.3.1.1, 3.3.1.2, 3.3.1.3 | `src/PanVault.Api/Validation/SadGuard.cs` |
| 3 | PAN masked when displayed | 3.4.1 | `src/PanVault.Api/Validation/PanMasker.cs` |
| 4 | Token has no mathematical relationship to the PAN | 3.5.1 | `src/PanVault.Api/Tokens/TokenStore.cs` |
| 5 | DEK kept out of code and image | 3.6.1 | `k8s/deployment.yaml`, `docs/secret.example.yaml` |
| 6 | TLS enforced in transit | 4.2.1 | `k8s/ingress.yaml` |
| 7 | Unnecessary functionality not enabled | 2.2.4 | `src/PanVault.Api/Program.cs`, `Dockerfile` |
| 8 | Minimal image attack surface | 2.2.4, 2.2.6 | `Dockerfile` |
| 9 | Unprivileged execution | 2.2.6, 7.2.1 | `Dockerfile`, `k8s/deployment.yaml` |
| 10 | No Kubernetes API credentials in the pod | 7.2.1, 7.2.2 | `k8s/serviceaccount.yaml`, `k8s/deployment.yaml` |
| 11 | Default-deny network segmentation | 1.2.1, 1.3.1, 1.4.1 | `k8s/networkpolicy.yaml` |
| 12 | No cardholder data in logs | 3.3.1, 10.2.1 | `src/PanVault.Api/Program.cs` |
| 13 | Components free of known vulnerabilities | 6.3.3 | `docs/trivy-report.txt`, `.github/workflows/ci.yaml` |
| 14 | Targeted risk analysis | 12.3.1 | `docs/targeted-risk-analysis.md` |
| 15 | Desired state enforced from git, drift reverted | 6.5.1, 6.5.2, 1.2.2 | `gitops/` |
| 16 | Secrets versioned only as ciphertext | 3.6.1, 8.6.2 | `gitops/secrets/`, `observability/grafana-admin-sealed.yaml` |
| 17 | Vendor default credentials replaced | 2.2.2 | `observability/grafana-admin-sealed.yaml`, `gitops/monitoring-app.yaml` |
| 18 | Monitoring and alerting of the CDE component | 10.7.2, 10.7.3 | `observability/`, `k8s/networkpolicy.yaml` |

## Verification

Every control can be checked with a command. The ones that talk to the cluster
assume you have already run `kubectl apply -f k8s/` and that the Ingress resolves
at `pan-vault.local` (see the [README](../README.md)).

### 1. PAN encrypted with AES-256-GCM (Req 3.5.1)

The PAN is encrypted with AES-256 in GCM mode, using a random 96 bit nonce per
operation and a 128 bit authentication tag. The resulting blob carries a version
byte at the front, authenticated as associated data, so tampering with it
invalidates the tag.

Format: `[version:1][nonce:12][tag:16][ciphertext:N]`, base64 encoded.

The same PAN produces a different ciphertext on every call, because the nonce is
random:

```bash
for i in 1 2; do
  curl -sk -X POST https://pan-vault.local/tokens \
    -H 'Content-Type: application/json' \
    -d '{"pan":"4111111111111111"}'
  echo
done
```

Expected result: two different tokens for the same PAN.

### 2. SAD rejected (Req 3.3.1, 3.3.1.1, 3.3.1.2, 3.3.1.3)

Sensitive authentication data (verification code, PIN, PIN block and magnetic
stripe data) must not be stored after authorization. The control here is
stricter: it is not even accepted in the request.

`SadGuard` runs as global middleware, so it applies to any endpoint, whenever it
gets added. It walks the entire JSON tree, not just the top level, and normalizes
field names so that `cvv_2`, `cvv-2` and `CVV 2` are all treated as `cvv2`.

```bash
# Top level field
curl -sk -X POST https://pan-vault.local/tokens \
  -H 'Content-Type: application/json' \
  -d '{"pan":"4111111111111111","cvv":"123"}'

# Nested field
curl -sk -X POST https://pan-vault.local/tokens \
  -H 'Content-Type: application/json' \
  -d '{"pan":"4111111111111111","card":{"cvv":"123"}}'

# Field inside an array
curl -sk -X POST https://pan-vault.local/tokens \
  -H 'Content-Type: application/json' \
  -d '{"pan":"4111111111111111","items":[{"pin":"1234"}]}'
```

Expected result in all three cases:

```json
{"error":"sensitive_authentication_data","field":"cvv"}
```

with status code `400`. The response names the offending field but never returns
its value.

### 3. PAN masked (Req 3.4.1)

The standard allows displaying at most the first six and the last four digits.
`GET /tokens/{id}` returns the masked PAN and never the PAN in the clear.

```bash
TOKEN=$(curl -sk -X POST https://pan-vault.local/tokens \
  -H 'Content-Type: application/json' \
  -d '{"pan":"378282246310005"}' | grep -o 'tok_[a-f0-9]*')

curl -sk https://pan-vault.local/tokens/$TOKEN
```

Expected result:

```json
{"token":"tok_...","maskedPan":"378282*****0005","brand":"amex","createdAt":"..."}
```

`PanMasker.Mask` fully masks any input shorter than 12 characters rather than
partially revealing it.

### 4. Token unrelated to the PAN (Req 3.5.1)

The token is 128 bits of cryptographic randomness, with no function derived from
the PAN. Without access to the vault there is no way to reverse it, which keeps
systems that only handle tokens out of PCI scope.

```bash
grep -A2 'NewToken' src/PanVault.Api/Tokens/TokenStore.cs
```

### 5. DEK kept out of code and image (Req 3.6.1)

The data encryption key is never in the code, in the image, or in the repository.
In development it comes from user secrets, in Kubernetes from a Secret mounted as
an environment variable. The application refuses to start without it.

```bash
# The image will not start without the DEK
docker run --rm pan-vault:dev
```

Expected result: `OptionsValidationException` and immediate exit. Validation
happens at startup (`ValidateOnStart`), not on the first request, so a
misconfigured deployment fails visibly instead of serving errors.

```bash
# No real value is versioned
git ls-files | xargs grep -l 'PanCrypto__Dek' 2>/dev/null
cat docs/secret.example.yaml
```

Expected result: the only versioned file mentioning the key is the template, with
the value `REPLACE_ME_BASE64_32_BYTES`.

### 6. TLS enforced (Req 4.2.1)

The Ingress terminates TLS and redirects any HTTP request to HTTPS, so cardholder
data never travels in the clear.

```bash
curl -sk -o /dev/null -w 'https -> %{http_code}\n' https://pan-vault.local/healthz
curl -s  -o /dev/null -w 'http  -> %{http_code}\n' http://pan-vault.local/healthz
```

Expected result: `https -> 200` and `http -> 308`.

### 7. Unnecessary functionality not enabled (Req 2.2.4)

Interactive API documentation sits behind the `PanVault:EnableApiDocs` flag, off
by default. When it is off, the OpenAPI services are not even registered in the
dependency injection container: this is not a disabled route, it is absent
functionality.

```bash
curl -sk -o /dev/null -w 'swagger -> %{http_code}\n' https://pan-vault.local/swagger
curl -sk -o /dev/null -w 'healthz -> %{http_code}\n' https://pan-vault.local/healthz
```

Expected result: `swagger -> 404` and `healthz -> 200`. The second acts as a
control: it rules out the 404 being caused by a connectivity problem.

### 8. Minimal attack surface (Req 2.2.4, 2.2.6)

The runtime image is `chiseled`: it contains no shell, no package manager and no
system utilities.

```bash
docker run --rm --entrypoint /bin/sh pan-vault:dev -c "echo hello"
```

Expected result, and the failure **is** the evidence:

```
exec: "/bin/sh": stat /bin/sh: no such file or directory
```

The same from Kubernetes, even with `exec` permissions on the pod:

```bash
kubectl exec -n cde deploy/pan-vault -- ls /
```

Expected result: `exec: "ls": executable file not found in $PATH`.

The scan quantifies the reduction. Trivy reports the operating system package
count on stderr, not in the report file:

```bash
docker run --rm \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v "$HOME/.cache/trivy:/root/.cache" \
  aquasec/trivy:latest image --severity HIGH,CRITICAL pan-vault:dev 2>&1 \
  | grep 'pkg_num'
```

Expected result:

```
INFO  [ubuntu] Detecting vulnerabilities... os_version="24.04" pkg_num=8
```

The image contains 8 operating system packages. A standard Ubuntu base exceeds a
hundred.

### 9. Unprivileged execution (Req 2.2.6, 7.2.1)

The process runs as a non-root user, with no Linux capabilities, no ability to
escalate privileges, and a read-only root filesystem.

```bash
docker inspect pan-vault:dev --format '{{.Config.User}}'

kubectl get pod -n cde -l app=pan-vault -o jsonpath=\
'runAsNonRoot: {.items[0].spec.securityContext.runAsNonRoot}
runAsUser: {.items[0].spec.securityContext.runAsUser}
seccomp: {.items[0].spec.securityContext.seccompProfile.type}
readOnlyRootFilesystem: {.items[0].spec.containers[0].securityContext.readOnlyRootFilesystem}
allowPrivilegeEscalation: {.items[0].spec.containers[0].securityContext.allowPrivilegeEscalation}
capabilities: {.items[0].spec.containers[0].securityContext.capabilities.drop}
'
```

Expected result:

```
1654
runAsNonRoot: true
runAsUser: 1654
seccomp: RuntimeDefault
readOnlyRootFilesystem: true
allowPrivilegeEscalation: false
capabilities: ["ALL"]
```

`runAsNonRoot: true` at the Kubernetes level is redundant with the Dockerfile's
`USER`, and that redundancy is intentional: if someone modifies the Dockerfile,
the cluster still refuses to start the pod.

### 10. No Kubernetes API credentials (Req 7.2.1, 7.2.2)

The application does not talk to the Kubernetes API, so the service account token
is not mounted. An attacker with execution inside the pod finds no credential to
move laterally with.

```bash
kubectl get pod -n cde -l app=pan-vault \
  -o jsonpath='{range .items[0].spec.volumes[*]}{.name}{"\n"}{end}'
```

Expected result: only `tmp`. The absence of a `kube-api-access-*` volume is the
evidence.

### 11. Network segmentation (Req 1.2.1, 1.3.1, 1.4.1)

The `cde` namespace denies all inbound traffic by default. Only two sources are
allowed to reach `pan-vault`, and only on port 8080: the Ingress controller, and
pods labeled `app=gateway` inside the same namespace.

This requires a CNI that enforces NetworkPolicies. Locally, minikube must be
created with `--cni=calico`.

```bash
# From outside the namespace: must fail
kubectl run probe-default --rm -i --restart=Never --image=busybox:1.36 -- \
  wget -qO- --timeout=5 http://pan-vault.cde:8080/healthz

# From inside, with the authorized label: must succeed
kubectl run probe-gateway --rm -i --restart=Never -n cde \
  --labels=app=gateway --image=busybox:1.36 -- \
  wget -qO- --timeout=5 http://pan-vault:8080/healthz
```

Expected result:

```
wget: download timed out
{"status":"ok"}
```

The pair is the evidence: same service, same port, same DNS name, and the only
variable is where the traffic originates.

A `bad address` instead of `download timed out` would indicate a DNS failure and
would prove nothing about network isolation.

The same pair of probes also runs automatically on every push and pull request,
against a kind cluster created inside the CI runner with Calico installed. See
the `Network isolation is enforced` step in
[`.github/workflows/ci.yaml`](../.github/workflows/ci.yaml).

### 12. No cardholder data in logs (Req 3.3.1, 10.2.1)

The PAN is never passed to a logger. There is no filter masking it afterwards: it
simply never enters. Logs are emitted as structured JSON to stdout and contain
the token, the brand and the last four digits.

```bash
kubectl logs -n cde deploy/pan-vault --tail=200 | grep -E '[0-9]{12,19}'
```

Expected result: no matches. No sequence of 12 to 19 digits appears in the logs.

```bash
kubectl logs -n cde deploy/pan-vault --tail=200 | grep 'Token issued'
```

Expected result, with the PAN absent:

```json
{"LogLevel":"Information","Category":"Program","Message":"Token issued tok_... visa 1111","State":{"Token":"tok_...","Brand":"visa","Last4":"1111"}}
```

### 13. Known vulnerabilities (Req 6.3.3)

The image is scanned with Trivy against the Ubuntu and .NET package CVE
databases.

```bash
docker run --rm \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v "$HOME/.cache/trivy:/root/.cache" \
  aquasec/trivy:latest image --severity HIGH,CRITICAL pan-vault:dev
```

Expected result: zero HIGH or CRITICAL findings across the four targets. The
latest report is in [`trivy-report.txt`](trivy-report.txt).

The same scan also runs as a CI gate on every push and pull request, with
`--exit-code 1` so that any HIGH or CRITICAL finding stops the pipeline before
the image can be published. See the `Scan with Trivy` step in
[`.github/workflows/ci.yaml`](../.github/workflows/ci.yaml).

### 14. Targeted risk analysis (Req 12.3.1)

See [`targeted-risk-analysis.md`](targeted-risk-analysis.md), which analyzes the
DEK cryptoperiod.

### 15. Desired state enforced from git (Req 6.5.1, 6.5.2, 1.2.2)

ArgoCD runs in the cluster and reconciles four `Application` resources defined in
`gitops/`, all with automated sync, pruning and self-healing. Every change to a
CDE resource, including network policies, is a commit: reviewable, attributable
and reversible. A change made directly against the cluster is reverted.

```bash
kubectl -n argocd get applications

kubectl -n cde delete deployment pan-vault
kubectl -n cde get deployment pan-vault -w
```

Expected result: the deployment is recreated within seconds without human
intervention, and the application stays `Synced` and `Healthy`. The cluster state
is defined by git, not by whoever holds `kubectl`.

### 16. Secrets versioned only as ciphertext (Req 3.6.1, 8.6.2)

The DEK and the Grafana administrator password live in git as `SealedSecret`
resources: ciphertext produced with the public key of a controller that runs in
the cluster. Only that controller holds the private key, so the repository can be
public and is self-sufficient: cloning it and pointing ArgoCD at it rebuilds the
system, secrets included, with no manual step.

```bash
# The key name is visible, the value is ciphertext
grep -A1 encryptedData gitops/secrets/sealed-secret.yaml

# The live Secret is owned by the SealedSecret, not by a person
kubectl -n cde get secret pan-vault-dek -o jsonpath='{.metadata.ownerReferences[0].kind}'

# Delete it and it comes back from git
kubectl -n cde delete secret pan-vault-dek && sleep 5 && kubectl -n cde get secret pan-vault-dek
```

Expected result: `SealedSecret` as the owner, and the Secret recreated seconds
after being deleted.

**Trust boundary.** This is not a hash and cannot be brute forced: without the
private key there is nothing to compare a guess against. The security of every
ciphertext in the repository rests on the custody of that private key, stored as
a Secret in `kube-system`. Anyone able to read Secrets there can decrypt
everything ever committed, including history. The consequences are documented in
[`targeted-risk-analysis.md`](targeted-risk-analysis.md): RBAC on `kube-system`
is part of the CDE boundary, the controller rotates its sealing key every 30 days,
and a suspected key exposure requires rotating the secrets themselves, not just
removing files from git. In production this role would be taken by a KMS or by
External Secrets Operator, so that key material never exists in git at all.

### 17. Vendor default credentials replaced (Req 2.2.2)

The Grafana chart ships with the well-known administrator password
`prom-operator`. It is replaced by a random value generated with `openssl rand`,
sealed, and consumed by the chart through `existingSecret`. The default Secret no
longer exists in the cluster.

```bash
kubectl -n monitoring get secret monitoring-grafana
kubectl -n monitoring get deploy monitoring-grafana \
  -o jsonpath='{.spec.template.spec.containers[?(@.name=="grafana")].env[?(@.name=="GF_SECURITY_ADMIN_PASSWORD")].valueFrom.secretKeyRef.name}'
```

Expected result: `NotFound` for the chart's default Secret, and `grafana-admin`
as the source of the administrator password.

### 18. Monitoring and alerting (Req 10.7.2, 10.7.3)

The API exposes Prometheus metrics, including `panvault_sad_rejections_total`, a
counter of requests rejected for carrying sensitive authentication data. Metrics
are served on a dedicated port, 9090, and the check is on the socket port rather
than on the `Host` header, so it cannot be bypassed through the Ingress. The
network policy opens that port only to Prometheus pods from the `monitoring`
namespace: namespace selector and pod selector combined, so nothing else in that
namespace qualifies.

A `ServiceMonitor` tells Prometheus where to scrape, a `ConfigMap` carries the
Grafana dashboard as versioned JSON, and a `PrometheusRule` defines two alerts:
`PanVaultDown` when the service stops being scraped, and
`PanVaultSadRejectionSpike` when more than ten requests in five minutes carry SAD.

```bash
# Never through the Ingress, not even with a forged Host header
curl -sk -o /dev/null -w '%{http_code}\n' https://pan-vault.local/metrics
curl -sk -o /dev/null -w '%{http_code}\n' -H 'Host: pan-vault.local:9090' https://pan-vault.local/metrics

# Prometheus reaches it through the network policy
kubectl -n monitoring port-forward svc/monitoring-kube-prometheus-prometheus 9090:9090
# http://localhost:9090/targets  -> pan-vault UP
# http://localhost:9090/alerts   -> PanVaultDown, PanVaultSadRejectionSpike
```

Expected result: `404` for both Ingress requests, the target `UP`, and both
alert rules loaded. Sending a burst of requests with a `cvv` field increments the
counter and, past the threshold, fires the alert.

## Out of scope

The following is not implemented, and the omission is deliberate. A reference
project that pretends to cover everything is less credible than one that states
its limits.

| Requirement | What is missing | Why |
|-------------|-----------------|-----|
| 3.6.1.4, 3.7.x | Real KMS or HSM, DEK custody and rotation | The DEK reaches the cluster as a Kubernetes Secret materialized from a SealedSecret in git, so its custody rests on the sealing key held in `kube-system`. In production this would be AWS KMS, Azure Key Vault or an HSM, with automated rotation. The version byte in the encrypted blob leaves the path open to rotate without rewriting existing data |
| 3.7.4 | DEK rotation implemented | See [`targeted-risk-analysis.md`](targeted-risk-analysis.md) for the proposed cryptoperiod |
| 8.4.2, 8.5.1 | MFA for CDE access | There is no authentication layer. The API is open to anyone who can reach the network |
| 7.2.x | API authentication and authorization | A real tokenization service requires client credentials on every call, and separate permissions for tokenizing and detokenizing |
| 10.2.x, 10.3.x | Audit trail for cardholder data access | There are application logs, but no immutable audit trail recording who, when and which token |
| 10.5.1 | 12 month log retention | Logs go to stdout and live as long as the pod does |
| 11.3.2 | Quarterly ASV scans | Requires an approved scanning vendor and an internet facing system |
| 12.5.2 | Semiannual scope review | An organizational process, not a code artifact |
| 4.2.1 | Certificate from a real CA | The Ingress uses a self-signed certificate. In production this would be cert-manager with Let's Encrypt, or the corporate CA |
| 3.2.1 | Real persistence | The store is an in-memory dictionary. It is lost when the pod restarts, and there is no encryption at rest at the disk level because there is no disk |

## Notes on token scope

An opaque, random token with no mathematical relationship to the PAN is not
cardholder data. Systems that only handle tokens fall outside PCI scope, provided
they have no access to the vault that can reverse them. That is the reason this
service exists: to reduce the scope of the rest of the platform.

Detokenization, which would return the PAN in the clear, is not implemented. When
it is, it will be a separate endpoint with its own permissions and audit trail,
not a parameter on the `GET`.
