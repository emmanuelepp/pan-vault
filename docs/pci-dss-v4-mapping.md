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

## Out of scope

The following is not implemented, and the omission is deliberate. A reference
project that pretends to cover everything is less credible than one that states
its limits.

| Requirement | What is missing | Why |
|-------------|-----------------|-----|
| 3.6.1.4, 3.7.x | Real KMS or HSM, DEK custody and rotation | The DEK lives in a Kubernetes Secret. In production this would be AWS KMS, Azure Key Vault or an HSM, with automated rotation. The version byte in the encrypted blob leaves the path open to rotate without rewriting existing data |
| 3.7.4 | DEK rotation implemented | See [`targeted-risk-analysis.md`](targeted-risk-analysis.md) for the proposed cryptoperiod |
| 8.4.2, 8.5.1 | MFA for CDE access | There is no authentication layer. The API is open to anyone who can reach the network |
| 7.2.x | API authentication and authorization | A real tokenization service requires client credentials on every call, and separate permissions for tokenizing and detokenizing |
| 10.2.x, 10.3.x | Audit trail for cardholder data access | There are application logs, but no immutable audit trail recording who, when and which token |
| 10.5.1 | 12 month log retention | Logs go to stdout and live as long as the pod does |
| 11.3.2 | Quarterly ASV scans | Requires an approved scanning vendor and an internet facing system |
| 12.5.2 | Semiannual scope review | An organizational process, not a code artifact |
| 4.2.1 | Certificate from a real CA | The Ingress uses a self-signed certificate. In production this would be cert-manager with Let's Encrypt, or the corporate CA |
| 3.2.1 | Real persistence | The store is an in-memory dictionary. It is lost when the pod restarts, and there is no encryption at rest at the disk level because there is no disk |
| 6.4.3, 11.6.1 | Secret management in GitOps | The DEK is created with `kubectl create secret` and is not versioned, so the repository is not self-sufficient. In phase 2 this moves to Sealed Secrets, which allows versioning the encrypted secret, or to External Secrets Operator against the provider's secret manager |

## Notes on token scope

An opaque, random token with no mathematical relationship to the PAN is not
cardholder data. Systems that only handle tokens fall outside PCI scope, provided
they have no access to the vault that can reverse them. That is the reason this
service exists: to reduce the scope of the rest of the platform.

Detokenization, which would return the PAN in the clear, is not implemented. When
it is, it will be a separate endpoint with its own permissions and audit trail,
not a parameter on the `GET`.
