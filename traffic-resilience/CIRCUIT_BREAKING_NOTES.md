# Circuit Breaking — Real Production Use Cases, Trade-offs, and Design Notes

Discussion notes captured before starting `TRAFFIC_RESILIENCE_IMPLEMENTATION_PLAN.md` Stage 2.

## Real production use case: how Netflix ended up inventing this (Hystrix)

Circuit breaking as a mainstream distributed-systems pattern is largely traceable to one very concrete, well-documented failure mode Netflix ran into around 2011-2012: a single **slow** (not even fully down — just *slow*) dependency deep in a call chain could take down the entire API layer.

The mechanism: Service A calls Service B calls Service C. Each service has a finite thread/connection pool for handling requests. If C starts responding slowly (a struggling database, a GC pause, whatever), B's threads calling C don't fail fast — they sit there waiting for a response that eventually times out (or never comes). Under enough load, B's entire thread pool fills up with threads stuck waiting on C. Now B can't serve *any* request — not just ones that needed C — because there are no threads left. A, waiting on B, experiences the exact same thing one layer up. One slow dependency, three layers down, took the whole system offline.

Netflix wrote this up directly in their engineering blog ("Fault Tolerance in a High Volume, Distributed System") and built **Hystrix** specifically to contain it — trip a breaker after a dependency's failure rate crosses a threshold, and stop sending it traffic entirely for a cooldown period, so callers fail *fast* instead of hanging.

**Core insight**: circuit breaking isn't really about "the backend is down" — it's about preventing a struggling-but-technically-alive backend from consuming so many resources upstream that it drags healthy parts of the system down with it.

## Benefits (grounded in that story)

- **Fail fast instead of hanging** — a caller that gets an immediate rejection can free its thread/connection immediately, instead of holding it hostage waiting on a doomed call.
- **Gives the struggling backend room to recover** — continuing to hammer an already-overloaded service with retries makes recovery *harder*, not easier (it's competing with its own recovery for the same limited resources).
- **Enables graceful degradation** — Netflix's actual pattern: when the personalized-recommendations service's breaker was open, they'd show generic/cached recommendations instead of a broken page. The breaker tripping becomes a trigger for a deliberate degraded experience, not just an error.
- **Contains blast radius** — the failure of one dependency stays local to features that need it, instead of cascading backward through every caller in the chain.

## Trade-offs and limitations

**Threshold tuning is genuinely hard, and getting it wrong has real cost either direction.** Too sensitive -> trips on ordinary transient blips (a GC pause, a brief network hiccup), degrading service unnecessarily. Too lax -> doesn't protect in time, cascading failure happens anyway. Not a "set it once" config; needs tuning against real traffic patterns.

**It doesn't fix anything — it only contains damage.** A circuit breaker tripping is a symptom being masked, not a problem being solved. Without alerting on breaker state changes, a genuinely broken dependency can stay broken for a long time while the breaker quietly absorbs the pain.

**The half-open recovery test can itself cause a second failure.** When a tripped breaker periodically lets a probe request through to check "has the backend recovered yet?", if *many* callers/replicas all do this at the same moment, that's a thundering herd hitting a system that's *just barely* coming back online — potentially re-tripping it immediately. Real implementations (e.g. Envoy) deliberately limit how much traffic gets let through during the half-open/recovery phase.

**It's rarely exercised, which means bugs in its config often surface for the first time during a real incident** — the worst possible time to discover a broken fallback. This is precisely why Netflix built **Chaos Monkey** and the broader Chaos Engineering practice: deliberately injecting failures in production, on purpose, specifically to verify breakers and fallbacks behave correctly *before* a real outage tests them for the first time.

**Distributed state, same issue as rate limiting.** Multiple gateway/proxy replicas each tracking their own breaker state independently means inconsistent behavior across replicas. Breakers tend to tolerate this better than rate limits (the goal is "roughly reduce load," not an exact global count), but it's still approximate, not exact, in a scaled-out setup.

## Design considerations

1. **What triggers "open"?** Hystrix's real design was a **rolling window of request volume + error percentage** (e.g. "at least 20 requests in the last 10 seconds, and >=50% of them failed") — not a naive "N consecutive failures" counter, since a single failure at low volume shouldn't trip a breaker meant for sustained degradation. Traefik's own `circuitbreaker` middleware is expression-based against live metrics (`NetworkErrorRatio`, `ResponseCodeRatio`, `LatencyAtQuantileMS`) — same philosophy.
2. **What happens when it's open?** Fail fast with a clear error, or serve a fallback (cached/default/degraded response)? Netflix treated this as a deliberate per-service design decision, not an afterthought.
3. **Recovery policy** — how long does "open" last before testing recovery, and how much traffic gets let through during that test (a single canary request vs. a percentage)?
4. **Co-design with timeouts and retries** — a circuit breaker's failure signal is often the same signal a timeout or retry policy reacts to. Configuring these three independently, without thinking about how they interact, is a common real mistake (e.g. a too-long timeout means a latency-based trip condition never fires because requests "succeed," just slowly).
5. **Observability is not optional** — every real implementation treats a breaker state change (closed -> open, open -> half-open) as an event worth alerting on, since it's a direct signal that a downstream dependency is unhealthy.

**Tie-in to Karrot**: their SRE team explicitly owns "service mesh networking" on Kubernetes — if they're running Istio/Envoy, Envoy's built-in circuit breaking (connection/request/retry limits per upstream, plus outlier detection that ejects a misbehaving pod from the load-balancing pool temporarily) is almost certainly part of their resilience posture, operating at exactly this philosophy, just implemented per-sidecar rather than at one central gateway like our Traefik setup.

## Related concept: what "sidecar" means

A **sidecar** is a helper proxy container deployed *inside the same Pod* as each service instance, rather than one shared proxy sitting in front of everything (named after a motorcycle sidecar — attached to and traveling with the main vehicle). In Istio, that sidecar is an Envoy proxy that transparently intercepts all network traffic in and out of its Pod.

**The architectural difference from our Traefik setup:**

```
Our setup (centralized gateway):

  outside world --> [ Traefik ] --> user-service
                          |
                          +-------> order-service

  order-service calling payment-service directly (if it existed)
  would NOT pass through Traefik at all - no gateway involved,
  no rate limiting, no circuit breaking, nothing.


Sidecar / service mesh setup (e.g. Istio):

  outside world --> [ Envoy sidecar ] --> user-service (same Pod)
                                              |
                    order-service Pod:        v
              [ Envoy sidecar ] <---> order-service (same Pod)
                    |
                    v
              [ Envoy sidecar ] <---> payment-service (same Pod)
```

Our Traefik only sees "north-south" traffic (client-to-service, from outside the cluster in). Service-to-service calls ("east-west" traffic, e.g. `order-service` calling `payment-service` directly) would bypass Traefik entirely and get none of our resilience features. In a sidecar/mesh setup, every hop — external or internal — passes through a local Envoy sidecar, so circuit breaking, retries, timeouts, and mTLS apply uniformly everywhere, not just at one central chokepoint.

This matters specifically for circuit breaking because the Netflix A->B->C cascading-failure story happened entirely in internal service-to-service calls — a central gateway like our Traefik instance would never even see that traffic, so it couldn't have protected against it. A sidecar on every Pod means B's outbound calls to C get their own circuit breaker, right at the point where B is about to get stuck waiting.

"Per-sidecar" means the circuit breaker's state and configuration live independently in each Pod's own sidecar, rather than one shared state in a single gateway process — more fine-grained control, no single central bottleneck, at the cost of many more proxy instances to configure and reason about (though tools like Istio automate the injection and centralize the *configuration* even though enforcement stays distributed).
