---
title: Production hosting
status: accepted
phase: 3
language: en
owner: ""
created: 2026-11-12
last_updated: 2026-11-14
related:
  - ../deployment.md
  - ../architecture.md
  - ../glossary.md
  - identity-auth.md
  - bff-adviser-portal.md
  - bff-customer-portal.md
  - ../backoffice/back-office.md
---

# Production hosting

How the .NET hosts start outside Development, behind the TLS-terminating reverse proxy of [deployment.md](../deployment.md). Covers items 1, 2, 3 and 5 of deployment §13: OpenIddict certificates and issuer on `identity`, Data Protection key persistence, forwarded headers and HTTPS redirection, and health endpoints with a container health check. No resource API, no table, no schema script. Development behaviour does not change, except that hosts set an explicit Data Protection application name (R9).

Serving the SPA build from the BFFs is the 2026-11-12 amendment of [bff-adviser-portal.md](bff-adviser-portal.md) and [bff-customer-portal.md](bff-customer-portal.md). The bootstrap SystemAdmin is the 2026-11-12 amendment of [identity-auth.md](identity-auth.md).

Amendment 2026-11-14: R17. The compose health check calls `/health` every 15 seconds on each host. Each call became a trace and pushed real request traces out of a telemetry viewer's per-resource limit.

---

## 1. Summary

Outside Development, `identity` signs and encrypts with certificates from files and issues tokens with a configured public issuer. The four public hosts persist Data Protection keys to their own folder and trust forwarded headers only from the internal network. No host redirects to HTTPS; the proxy does. Every host answers `/health` and `/alive` on a separate health port only, and can check itself with `--healthcheck`, so a container without a shell can still have a health check.

"Outside Development" below means any environment name other than `Development`. "Public hosts" are `identity`, `bff-adviser-portal`, `bff-customer-portal` and `back-office`.

---

## 2. Scope

**In**

- `identity`: certificate loading, explicit issuer, transport security requirement outside Development
- Public hosts: Data Protection persistence and application name; forwarded headers
- `identity` and `webapi`: no HTTPS redirection; `webapi`: no HSTS
- ServiceDefaults: health endpoints on the health port; `--healthcheck` mode for every host
- Tests: functional tests that start hosts outside Development; unit tests for the health check client

**Out**

- Serving the SPA build (BFF amendments 2026-11-12)
- Bootstrap SystemAdmin (identity-auth amendment 2026-11-12)
- The compose file, the Caddyfile and the deploy workflow ([deployment.md](../deployment.md) §5, §9; config)
- Loading two signing certificates at once for rotation. A certificate swap signs everyone out once
- Encrypting Data Protection keys at rest
- Rate limiting inside the hosts
- Health checks that probe dependencies (SQL, identity). `/health` keeps the checks ServiceDefaults registers today

---

## 3. Stories

1. As the owner I restart `identity` and every issued token still validates, because the signing key comes from a file.
2. As a user I stay signed in to a portal across a deploy, because the BFF's Data Protection keys survive container replacement.
3. As a browser behind Caddy I get `https://` redirect URIs and secure cookies from every public host, and no redirect loop.
4. As `docker compose` I can tell when a chiseled container is healthy, without a shell in the image.
5. As an anonymous visitor I cannot read `/health` or `/alive` through a public hostname.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Outside Development, `identity` loads the signing certificate from `Identity:SigningCertificatePath` and the encryption certificate from `Identity:EncryptionCertificatePath`. Both are PKCS #12 (`.pfx`) files with private keys, opened with the one password `Identity:CertificatePassword`. It does not call `AddDevelopmentSigningCertificate` or `AddDevelopmentEncryptionCertificate`. |
| R2 | Outside Development, start-up of `identity` fails before it listens when any of the three certificate keys is missing, a file is missing, or the password is wrong. The error names the key or the path. It never contains the password. |
| R3 | Outside Development, `identity` sets the issuer from `Identity:Issuer`, an absolute `https` URL ending in `/`. Start-up fails when it is missing or not such a URL. Discovery (`/.well-known/openid-configuration`) returns exactly that value as `issuer`. |
| R4 | Outside Development, `identity` keeps the OpenIddict transport security requirement: it does not call `DisableTransportSecurityRequirement`. A request that reaches it as `http` after forwarded headers are applied is rejected by OpenIddict. Development keeps the current call. |
| R5 | Development keeps the development certificates and the issuer derived from the request, as today. The three certificate keys and `Identity:Issuer` are ignored in Development. |
| R6 | Outside Development, each public host applies forwarded headers before any other middleware. It trusts `X-Forwarded-Proto` and `X-Forwarded-For` only. It does not trust `X-Forwarded-Host`; the proxy forwards the original `Host`. |
| R7 | Outside Development, the trusted networks come from `ForwardedHeaders:KnownNetworks` (a list of CIDR ranges). The default loopback entries are removed. Start-up of a public host fails when the list is empty or an entry is not a valid CIDR range. A request from outside the listed networks keeps its own scheme and remote address. |
| R8 | Development keeps the current forwarded-header behaviour of the BFFs and `back-office` (`X-Forwarded-Proto` from any address). `identity` gets none in Development. |
| R9 | Each public host sets an explicit Data Protection application name, in every environment: `mywealth-identity`, `mywealth-bff-adviser-portal`, `mywealth-bff-customer-portal`, `mywealth-back-office`. Two hosts never share an application name. |
| R10 | When `DataProtection:KeysPath` is set, the host persists its key ring to that folder. Outside Development, start-up of a public host fails when it is not set. In Development it is optional; without it the key ring stays where ASP.NET Core puts it today. Keys are not encrypted at rest (deployment §11 known gap). |
| R11 | `identity` and `webapi` do not call `UseHttpsRedirection` in any environment. `webapi` does not call `UseHsts`. HTTPS redirection and HSTS belong to the reverse proxy. |
| R12 | The health port is `Health:Port`, default `8081`. Outside Development, `/health` and `/alive` are mapped only for requests that arrive on the health port. On any other port they are not endpoints: the request falls through to the host's normal routing, and must not return the health response. The host must be configured to listen on the health port (`ASPNETCORE_HTTP_PORTS=8080;8081` in deployment §5). |
| R13 | Development keeps `/health` and `/alive` on every port, as today. |
| R14 | `/health` runs every registered check; `/alive` runs only checks tagged `live`, as today. The response body is the default health check text (`Healthy` / `Unhealthy`). No detail is exposed. |
| R15 | Every host (`identity`, `webapi`, `worker`, both BFFs, `back-office`) checks its first argument before it builds the web host. When it is `--healthcheck`, the process sends `GET http://localhost:{Health:Port}/health` with a 5-second timeout, writes one line to standard output, and exits with code `0` on HTTP 200 and `1` on any other status, a timeout or a connection error. It does not start the web host, read the database or run the schema applicator. |
| R16 | Configuration keys of this spec are read through the normal configuration chain, so environment variables (`Identity__Issuer`, `ForwardedHeaders__KnownNetworks__0`, …) set them. |
| R17 | In every environment, a request whose path is `/health` or `/alive`, or starts with either segment, produces no ASP.NET Core trace. The filter matches the path on any port. Every other request is traced as before. Metrics and logs do not change. (Amendment 2026-11-14.) |

---

## 5. Domain

None.

---

## 6. Database

None. Script: **none**.

---

## 7. Application use cases

None on `webapi` / `Application`. Host composition only:

| Kind | Name | Where | Checks |
| --- | --- | --- | --- |
| Host | OpenIddict certificates and issuer | `IdentityHost` | R1–R5 |
| Host | Forwarded headers outside Development | ServiceDefaults extension, called by the public hosts | R6–R8 |
| Host | Data Protection | ServiceDefaults extension, called by the public hosts with their application name | R9, R10 |
| Host | Health endpoints | `MapDefaultEndpoints` in ServiceDefaults | R12–R14 |
| Host | `--healthcheck` | ServiceDefaults helper, called first in every `Program` | R15 |

---

## 8. API

| Method | Route | Port | Auth | Success | Failure |
| --- | --- | --- | --- | --- | --- |
| GET | `/health` | health port (outside Development) | Anonymous | 200 `Healthy` | 503 `Unhealthy` |
| GET | `/alive` | health port (outside Development) | Anonymous | 200 `Healthy` | 503 `Unhealthy` |

No resource route changes. No route is added to `api-design.md`.

### 8.1 Configuration keys

| Key | Host | Outside Development | Example |
| --- | --- | --- | --- |
| `Identity:Issuer` | `identity` | Required | `https://id.<domain>/` |
| `Identity:SigningCertificatePath` | `identity` | Required | `/certs/signing.pfx` |
| `Identity:EncryptionCertificatePath` | `identity` | Required | `/certs/encryption.pfx` |
| `Identity:CertificatePassword` | `identity` | Required | — |
| `ForwardedHeaders:KnownNetworks` | public hosts | Required, at least one CIDR | `172.30.0.0/24` |
| `DataProtection:KeysPath` | public hosts | Required | `/keys` |
| `Health:Port` | every host | Optional, default `8081` | `8081` |

---

## 9. UI

None.

---

## 10. Tests

Functional tests start hosts with `UseEnvironment("Production")` and supply the required keys of §8.1. Test certificates are generated in the test (self-signed, with the key usages of deployment §6) and written to a temporary folder; no PFX is committed.

| Project | Assert |
| --- | --- |
| Application.FunctionalTests | `identity` in Production with valid certificate files: discovery `issuer` equals `Identity:Issuer`; JWKS contains the key of the signing PFX (same `kid` / modulus) — R1, R3 |
| Application.FunctionalTests | `identity` in Production: each of the three certificate keys missing, a missing file, a wrong password, and a missing or `http` `Identity:Issuer` → host fails to start; the message names the key or path and does not contain the password — R2, R3 |
| Application.FunctionalTests | `identity` in Production: a request from a trusted network with `X-Forwarded-Proto: https` to discovery is served with `https` endpoint URLs; the same request from outside the trusted network is rejected by the transport security requirement — R4, R6, R7 |
| Application.FunctionalTests | `bff-adviser-portal` in Production: `X-Forwarded-Proto: https` from a trusted network → `/bff/login` challenge has an `https://…/signin-oidc` `redirect_uri` and the correlation cookie is `Secure`; from an untrusted address → the scheme is not changed — R6, R7 |
| Application.FunctionalTests | Each public host in Production with an empty or invalid `ForwardedHeaders:KnownNetworks` or no `DataProtection:KeysPath` → fails to start — R7, R10 |
| Application.FunctionalTests | `bff-adviser-portal` in Production with `DataProtection:KeysPath`: a key file appears in that folder; a cookie issued by one factory instance is accepted by a second instance using the same folder — R9, R10 |
| Application.FunctionalTests | `identity` and `webapi` in Production: a plain `http` request is not redirected (no 307/308); `webapi` response has no `Strict-Transport-Security` header — R11 |
| Application.FunctionalTests | `webapi` in Production: `GET /health` that arrives on local port `8081` → 200 `Healthy`; on local port `8080` → not the health response (404 or the host's fallback); `/alive` the same. The port is the connection's local port, not the `Host` header, because the proxy passes the client's `Host` through — R12, R14 |
| Application.FunctionalTests | `webapi` in Development: `GET /health` on the default port → 200, as today — R13 |
| Application.FunctionalTests | `webapi` in Production with a recording trace processor: `GET /health` and `GET /alive` on the health port record no server trace; a request to another path on the traffic port records one — R17 |
| Application.UnitTests (or a ServiceDefaults test project) | Health check client: 200 → exit 0; 503 → exit 1; connection refused → exit 1; no answer within the timeout → exit 1 — R15 |

The `--healthcheck` argument branch in each `Program` is wiring. It is proven by building the images and running the compose health checks on the rehearsal VPS (deployment §12).

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Certificates | Two PFX files, one password, paths from configuration. No certificate store, no Key Vault |
| Issuer | Explicit from configuration outside Development |
| Forwarded headers | `X-Forwarded-Proto` and `X-Forwarded-For` from configured CIDR ranges. Not `X-Forwarded-Host` |
| HTTPS redirection and HSTS | Reverse proxy only |
| Data Protection | One folder and one application name per public host |
| Health | Separate port, default `8081`; `--healthcheck` self-check |
| Fail fast | Missing production keys stop start-up instead of falling back to Development behaviour |

Still open, not this slice:

- Certificate rotation with two signing keys loaded at once
- Data Protection key encryption at rest
- Readiness checks for SQL and `identity` in `/health`

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. `identity` certificates and issuer from configuration outside Development (R1–R5)
2. Forwarded headers for the public hosts outside Development; no HTTPS redirection on `identity` and `webapi`; no HSTS on `webapi` (R6–R8, R11)
3. Data Protection application name and key folder for the public hosts (R9, R10)
4. Health endpoints on the health port outside Development (R12–R14)
5. `--healthcheck` self-check in every host (R15)
6. No traces for health endpoint requests (R17, amendment 2026-11-14)
