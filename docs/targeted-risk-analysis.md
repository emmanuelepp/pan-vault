# Targeted risk analysis: DEK cryptoperiod

**Requirement:** PCI DSS v4.0.1, Req 12.3.1
**Element analyzed:** Req 3.7.4, rotation of cryptographic keys at the end of the cryptoperiod
**Date:** 2026-08-20
**Next review:** 2027-08-20

> This document is a reference exercise about a demonstration system. It is not a
> risk analysis of a production environment.

## Why this analysis

PCI DSS v4.0.1 does not set a rotation frequency for data encryption keys.
Req 3.7.4 requires rotation "at the end of the defined cryptoperiod" and delegates
the definition of that period to the entity through a targeted risk analysis
under Req 12.3.1.

This document defines the cryptoperiod of the DEK that `pan-vault` uses to encrypt
PANs.

## Protected element

| | |
|---|---|
| Asset | Primary account numbers (PAN) stored encrypted |
| Key | 256 bit DEK, AES-GCM |
| Key scope | Every PAN in the vault. One active key at a time |
| Location | Kubernetes Secret, mounted as an environment variable in the pod |
| Estimated volume | Demonstration: tens of records. Reference scenario: up to 10 million PANs |

## Threats considered

**T1. Key exposure.** Someone obtains the DEK through the Kubernetes Secret,
through an environment variable captured in a process dump, or from a poorly
protected backup. With the key and the ciphertext, every PAN becomes readable.

**T2. Cryptanalysis through volume accumulation.** AES-GCM degrades when a nonce
is reused under the same key. With random 96 bit nonces, collision probability
follows the birthday bound: it reaches roughly 2^-32 at around 2^32 encryption
operations under the same key.

**T3. Persistent access.** An attacker who obtained the key in the past retains
the ability to decrypt future data for as long as that key stays active. Without
rotation, a one-time compromise becomes permanent.

**T4. Personnel turnover.** Someone with legitimate access to the Secret leaves
the organization. Without rotation, their knowledge remains usable.

## Factors affecting likelihood

**Reducing likelihood:**

- The key is never in the code or in the container image. The application does not
  start without it, so a misconfigured deployment fails visibly instead of
  degrading silently
- The `cde` namespace denies all inbound traffic by default. Only two explicit
  sources can reach the service
- The container runs unprivileged, with no shell and no Kubernetes API token,
  which sharply limits what can be done after gaining execution inside the pod
- The root filesystem is read-only, so an extraction tool cannot be persisted

**Increasing likelihood:**

- The Kubernetes Secret is encrypted at rest in etcd only if the cluster is
  configured for it, and it is not protected by an HSM
- Any principal with read permission on Secrets in the `cde` namespace can read
  the DEK in the clear. This project defines no restrictive RBAC
- The value is exposed as a process environment variable, visible in a memory dump

## Impact

Maximum. A compromised key exposes every stored PAN, not a subset. There is no
separation by tenant, by date range, or by any other dimension: a single key
protects the entire vault.

## Volume analysis (T2)

The cryptographic limit is far from being the deciding factor.

With random 96 bit nonces, the point at which collision probability becomes
relevant is around 2^32 encryption operations, roughly 4.3 billion. A system
tokenizing 10 million PANs per year would take more than 400 years to approach
that threshold.

**Conclusion:** the cryptoperiod must be set by operational exposure (T1, T3, T4),
not by cryptographic exhaustion. Volume is not a practical constraint in any
reasonable scenario for this service.

## Defined cryptoperiod

**Twelve months**, with immediate rotation upon any of the following:

- Suspected or confirmed key exposure
- Departure of a person with read access to Secrets in the `cde` namespace
- Change of cloud provider or cluster migration
- Discovery of a relevant vulnerability in the AES-GCM implementation in use

Twelve months balances two costs. Rotating more often forces re-encrypting the
entire vault more frequently, and each re-encryption is a window in which PANs
pass through memory in the clear. Rotating less often extends the lifetime of an
undetected compromise, which is the dominant threat per the analysis above.

## Technical feasibility of rotation

The encrypted blob format was designed to allow rotation without a service
outage.

```
[version:1][nonce:12][tag:16][ciphertext:N]
```

The leading version byte is authenticated as associated data, so altering it
invalidates the tag and cannot be used to force a downgrade to an earlier scheme.

That byte makes it possible for records encrypted under different keys to
coexist: a new version can indicate either a key identifier or a different scheme.
Without it, any rotation would require re-encrypting every record at once, with
the service stopped, and there would be no way to tell an old blob from a tampered
one.

The planned rotation procedure is:

1. Introduce the new DEK alongside the previous one, both available for decryption
2. Encrypt everything new with the new key, marking it with a distinct version
3. Re-encrypt existing records incrementally, in the background
4. Retire the previous key once no record still uses it

Implementation is out of scope for this project. See the corresponding section in
[`pci-dss-v4-mapping.md`](pci-dss-v4-mapping.md).

## Compensating controls while rotation is absent

In the current state of the project there is no automated rotation. The controls
that reduce exposure until there is one are:

- Default-deny network segmentation, verifiable
- Unprivileged execution with no tooling inside the container
- Absence of the Kubernetes API token in the pod
- The key is neither versioned nor present in the image
- The store is in-memory, so no encrypted PANs persist across a pod restart, which
  in practice bounds the exposure window

That last point is a characteristic of the demonstration project and not a control
that would hold in production.

## Review

This analysis is reviewed at least every twelve months, and sooner if any of the
following changes:

- The storage model stops being in-memory
- A KMS or HSM is introduced
- The estimated volume changes by more than an order of magnitude
- Client authentication is added, which alters the threat model
