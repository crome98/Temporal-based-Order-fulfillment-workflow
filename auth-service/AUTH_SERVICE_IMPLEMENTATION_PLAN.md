## Stage 1: Standalone auth-service (no Docker, no Traefik yet)
**Goal**: A tiny ASP.NET Core minimal API with two routes: `POST /login` (issue a JWT for a hardcoded demo user) and `GET /validate` (check a Bearer token, return 200/401).
**Success Criteria**: You can run it with `dotnet run` and hit both routes with `curl` from your own terminal.
**Tests**: `curl -X POST /login` returns a token. `curl /validate` with no header returns 401. `curl /validate` with the token returns 200.
**Status**: Complete

## Stage 2: Containerize it
**Goal**: Dockerfile for auth-service, added as a service in docker-compose.yml (not wired to Traefik yet — just reachable directly for testing).
**Success Criteria**: `docker compose up auth-service` works, same curl tests pass against the container.
**Tests**: Same as Stage 1, run against the containerized port.
**Status**: Complete

## Stage 3: Wire Traefik ForwardAuth
**Goal**: Add a `forward-auth` middleware in Traefik labels pointing at `auth-service`'s `/validate`, applied to `user-service` and `order-service` routers.
**Success Criteria**: Requests to `/api/v1/users` without a token get 401 from Traefik (never reach whoami). With a valid token, they pass through.
**Tests**: `curl localhost:8080/api/v1/users` -> 401. `curl -H "Authorization: Bearer <token>" localhost:8080/api/v1/users` -> 200 (whoami response).
**Status**: Complete

## Stage 4: Discuss what's simplified vs. production
**Goal**: Walk through the gaps between this toy setup and a real IdP (token expiry/refresh, key rotation, HTTPS, `accounts.daangn.com`-style centralization) so you know what you're *not* seeing here.
**Success Criteria**: You can articulate the difference between "gateway does routing" and "IdP does identity" and why they're kept separate.
**Status**: Complete

**Summary of gaps identified vs. real production:**
- Plaintext password comparison instead of hashed (bcrypt/argon2) storage in a real DB
- Single hardcoded symmetric signing key instead of asymmetric (RS256/ES256) keys loaded from a secret store — symmetric means every verifier can also forge tokens
- No key rotation (`kid` header) — a leaked key can't be retired without breaking all outstanding tokens
- No revocation — a signed token is valid until `exp` no matter what; no refresh-token store or token blocklist
- No refresh-token flow — session just ends at 15 minutes, full re-login required
- Plain HTTP instead of TLS — a JWT is signed, not encrypted, so claims are readable on the wire
- `ValidateIssuer`/`ValidateAudience` explicitly disabled — a real system would scope tokens to a specific issuer/audience

**Core lesson that still holds despite the gaps**: the gateway (Traefik) only does routing and delegates the auth yes/no question via `forwardAuth`; a separate service owns identity and can evolve independently (harden its password storage, rotate keys, etc.) without Traefik ever changing. This mirrors why Karrot centralized identity into `accounts.daangn.com` rather than letting each service own its own auth logic.

## Stage 5: Switch signing from HS256 (symmetric) to RS256 (asymmetric)
**Goal**: Replace the single shared `SymmetricSecurityKey` with an RSA key pair — sign with a private key in `/login`, verify with a public-key-only `RsaSecurityKey` in `/validate` — so the verifier is structurally incapable of forging tokens, and add a `kid` header for future rotation.
**Success Criteria**: Full flow (blocked -> login -> allowed through gateway) still works end-to-end with RS256; wrong password still 401s.
**Tests**: Same three-step gateway test as Stage 3, rerun after the algorithm switch.
**Status**: Complete

## Stage 6 (stretch goal): CI-built image instead of local `build:`
**Goal**: Replace `auth-service`'s `build:` key with `image: <registry>/auth-service:<tag>`, and sketch (or actually wire) a CI step that builds/tags/pushes that image on merge to main — mirroring the real merge -> CI -> registry -> CD flow discussed.
**Success Criteria**: `docker-compose.yml` pulls a pre-built tagged image rather than building locally; you can explain what a CI pipeline step for this would look like even if it isn't hooked up to a real registry.
**Status**: Not Started
