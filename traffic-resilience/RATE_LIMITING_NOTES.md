# Rate Limiting — Benefits, Trade-offs, and Design Notes

Discussion notes captured before starting `TRAFFIC_RESILIENCE_IMPLEMENTATION_PLAN.md` Stage 1.

## Benefits

- **Protects finite backend capacity** — if `user-service` can only sustain, say, 200 req/s before latency degrades, rate limiting caps the blast radius of any burst (legitimate spike, retry storm, or actual abuse) before it reaches that ceiling.
- **Fairness across clients** — in a multi-tenant system, without limits one noisy client can starve everyone else sharing the same backend.
- **Backpressure signal, not silent failure** — a `429` (ideally with a `Retry-After` header) tells a well-behaved client "slow down, try again later" instead of the backend just falling over and returning 500s or timing out.
- **Cheap first line of defense against abuse** — brute-force login attempts, scraping, credential stuffing — rate limiting blunts the simple/single-source version of these cheaply, at the edge, before it costs you any real backend work.
- **Predictable capacity planning** — once you know the worst-case rate any single client can generate, sizing the backend becomes a bounded problem instead of an open-ended one.

## Trade-offs and limitations

**It creates false positives.** A legitimate burst — a mobile app retrying after a brief network blip, a batch job, a traffic spike from something going viral — gets throttled exactly like abuse would. Rate limiting can't distinguish "malicious" from "unusually bursty but legitimate" on its own.

**The limiting key (dimension) you choose has real consequences:**

| Key | Problem |
|---|---|
| Per-IP | Breaks for users behind NAT/corporate proxies or CDNs (many real users share one IP); trivially bypassed by an attacker rotating IPs |
| Per-user/API-key | Fairer, but requires the request to already be authenticated — doesn't help on endpoints *before* auth, like `/login` itself, which is exactly what needs protecting against brute force |
| Global (whole gateway) | Simplest to reason about, but defeats the fairness goal entirely — one bad actor still crowds out everyone else within the shared budget |

**It doesn't scale correctly by default across multiple gateway replicas.** A classic gotcha: if you ever run more than one Traefik instance, each one enforces its *own* counter unless they share state (e.g. via Redis). A "100 req/s limit" silently becomes "100 x replica count" in practice.

**It's a blunt instrument against real distributed abuse.** A botnet with thousands of source IPs, each individually staying under the per-IP threshold, sails straight through. Rate limiting stops a single bad actor or naive script, not a coordinated distributed attack — that needs WAF/fingerprinting/upstream DDoS protection, a different layer entirely.

**Algorithm choice matters and is easy to get subtly wrong.** A naive *fixed window* counter (e.g. "max 100 requests per calendar minute") has a boundary-burst flaw: a client can send 100 requests in the last second of one window and another 100 in the first second of the next — 200 requests in ~2 seconds, technically compliant. A *token bucket* (steady refill rate + a burst allowance) smooths this out and matches real traffic patterns better — this is what Traefik's `ratelimit` middleware implements (`average` = refill rate, `burst` = bucket size) — confirm against the docs/labels when implementing.

**It doesn't replace capacity planning.** Rate limiting caps damage; it doesn't create capacity. If legitimate traffic genuinely grows past the configured limit, the fix is raising the limit or scaling out — not leaning on `429`s indefinitely.

## Designing a good rate limit — the actual questions to answer

1. **What's the real backend capacity you're protecting, not a guess?** Ideally derived from load testing (e.g. "P99 latency stays healthy up to 200 req/s"), not picked out of the air.
2. **What dimension do you key on, and why?** Per-route almost always (a login endpoint and a health-check endpoint shouldn't share a budget); per-IP vs. per-identity depends on whether the endpoint is authenticated yet.
3. **What algorithm?** Token bucket as a strong default — allows natural bursts up to a cap while enforcing a steady average, rather than punishing anything bursty.
4. **What do you return when limited?** `429` + `Retry-After`, always — silently dropping or returning a generic error just causes clients to retry blindly, which can itself cause a retry-storm cascade.
5. **Does it need to be correct across replicas, or is approximate fine?** If scaling the gateway horizontally later, decide whether per-instance limits (fast, no shared state, approximate) are good enough or a Redis-backed shared counter (correct, adds a dependency and latency) is needed.
6. **Layered, not single-point.** Real systems often rate limit at multiple layers — CDN/edge (cheapest, rejects earliest), gateway (per-route/per-key, what we're building), and sometimes the service itself as defense-in-depth.
7. **Instrument it.** A limit configured once and never revisited drifts out of sync with real traffic. Visibility into how often it actually triggers and against whom is what lets you tune it rather than guess.
