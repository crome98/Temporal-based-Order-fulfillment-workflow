# Stage 3 — What's Simplified vs. Production

Discussion notes captured after completing rate limiting (Stage 1) and circuit breaking (Stage 2).

## What we built, in one line

A single Traefik instance enforcing rate limiting and circuit breaking at the edge, in front of one backend replica each. That framing matters for everything below - almost every simplification traces back to "single instance, single replica, edge-only."

## Rate limiting — the gap

We never configured `sourceCriterion`, so Traefik's `ratelimit` middleware is keying off its default (client IP). Every request from one machine shares one bucket - fine for a demo, useless for real fairness across many users, exactly the "per-IP breaks down" problem from `RATE_LIMITING_NOTES.md`.

**The bigger structural gap**: our limiter's state lives entirely inside this one Traefik process. Scale to multiple gateway replicas and each one enforces its *own* count - the "100 req/s silently becomes 100 x replica count" problem, also already flagged in the rate limiting notes, but now actually built (single-instance version) so the failure mode is concrete rather than theoretical.

**How production actually solves this**: a shared, external rate-limit service. Envoy's ecosystem has a well-known pattern for exactly this - a dedicated gRPC service (Lyft's open-source `ratelimit` project is the canonical implementation) that every Envoy proxy calls out to for a decision, backed by a shared store (typically Redis) so the count is globally consistent no matter how many proxy replicas are asking. It's the same *shape* as our `forwardAuth` delegation - "don't decide locally, ask a shared authority" - just applied to rate limiting instead of identity.

## Circuit breaking — the gap

Ours is scoped **per-router**, aggregating *all* traffic to `user-service` as one pool. That's invisible right now because we only run one replica - but scale to five replicas behind the same router, and our breaker can't tell "one bad replica out of five" from "the whole service degrading." A 20% real failure rate (1-of-5 replicas totally dead) wouldn't even cross our `0.30` threshold, even though a fifth of all traffic is being wasted on a replica that will never succeed.

**How production actually solves this — and it's two separate mechanisms, not one**:
- **Outlier detection** (Envoy/Istio) - tracks health *per individual backend endpoint*, not per-router. A misbehaving pod gets temporarily ejected from the load-balancing pool while its siblings keep serving traffic normally. This is the closer real-world analog to what we built, just applied at finer granularity.
- **Circuit breakers** (Envoy's own separate concept, confusingly reusing the same name) - resource-based limits like max connections, max pending requests, max retries *to a given upstream cluster*, protecting the **caller** from exhausting its own resources on one dependency - closer to the original Netflix/Hystrix motivation (thread pool exhaustion) than to our error-ratio trigger.

## What's missing entirely: retries and load shedding

We built exactly two of the four real production concerns. The other two:

- **Retries** - never added any. Retries are dangerous *without* being co-designed with circuit breaking, which is exactly the point made in `CIRCUIT_BREAKING_NOTES.md`: blind retries against a struggling backend just add more load to something already failing. Envoy's config reflects this directly - `maxRetries` is literally one of the fields inside its circuit breaker resource limits, not a separate independent setting. They're deliberately coupled, not two unrelated features.
- **Load shedding** - a fundamentally different question than anything we built: "am *I* (the proxy/gateway) currently too loaded to handle more work at all," independent of whether the backend is healthy or the client is over their rate limit. Envoy has a dedicated subsystem for this (the *overload manager*) that watches the proxy's own memory/connection pressure and starts proactively rejecting requests before it falls over - a form of self-protection our single lightly-loaded Traefik instance never needed to demonstrate.

## Single gateway vs. sidecar mesh — ties back to the sidecar discussion

Everything we built only protects **north-south** traffic - external requests hitting `user-service`/`order-service` through Traefik. If `order-service` called `payment-service` directly (service-to-service, "east-west"), none of this - rate limiting, circuit breaking, retries, load shedding - would apply at all, because that traffic never passes through our one Traefik instance. In a sidecar/mesh setup, every pod's own Envoy sidecar enforces these same concerns on *every* hop, internal or external - which is precisely why the Netflix A->B->C cascading failure (an entirely internal chain) needed a solution that lives at every hop, not just at the edge.

**This is directly relevant to the order-workflow work planned next**: when `order-service` starts calling `payment-service` and `ship-service` directly, that traffic will bypass every resilience control built here, exactly as described above - see the note carried into `order-workflow/ORDER_WORKFLOW_IMPLEMENTATION_PLAN.md`.

## Tying it together with what we actually hit today

The messiness encountered hands-on is a small, honest preview of real production experience: `docker stop` silently deregistering a route instead of producing a countable failure, `docker pause` causing a hang instead of an error because no timeout was configured, and `serversTransport` labels being silently dropped by this Traefik version - none of these were "wrong," they were genuine tooling rough edges worked around empirically, the same way real infrastructure engineers debug production systems: read the actual logs/config dump, don't trust assumptions, verify directly.
