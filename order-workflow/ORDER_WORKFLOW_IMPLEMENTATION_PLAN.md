## Stage 1: Give order-service real state
**Goal**: Replace the `order-service` `whoami` stub with a real minimal API backed by real state. Needed before contention or workflow coordination can be demonstrated meaningfully — you can't race on state that doesn't exist yet.
**Success Criteria**: Can create an order and read/update it via curl through the gateway, same auth (`user-auth`/`order-auth` forwardAuth) still applies.
**Status**: In Progress (1a-1c complete with in-memory storage + idempotency; revising to Postgres + Clean Architecture + MediatR + Outbox below, since the in-memory version can't support the idempotency race fix or the future pub-sub event dispatch)

### Stage 1a: In-memory version (done first, superseded below)
Built `POST /api/v1/orders` / `GET /api/v1/orders/{id}` / `PATCH /api/v1/orders/{id}` with `ConcurrentDictionary` storage and an `Idempotency-Key` header check. Confirmed working end-to-end through the gateway. Surfaced a real check-then-act (TOCTOU) race in the idempotency check itself — the reason for the Postgres revision below (an in-memory dictionary can't give a duplicate-insert guarantee the way a DB unique constraint can).
**Status**: Complete

### Stage 1b: Postgres + schema
**Goal**: Add a `postgres` service to `docker-compose.yml` with a persistent volume, and a schema with `orders`, `idempotency_keys` (unique constraint on key), and `outbox_messages` tables.
**Status**: Complete (`order-db` service added, schema in `order-service/db/init.sql`, mounted via `docker-entrypoint-initdb.d`, verified with `\dt` showing all three tables)

### Stage 1c: Clean Architecture skeleton + packages
**Goal**: Reorganize `order-service/OrderService` into `Domain/`, `Application/`, `Infrastructure/`, `Api/` folders (single project). Add `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, and `MediatR` packages.
**Status**: Complete (folders created; `MediatR`, `Microsoft.EntityFrameworkCore.Design` 8.0.11, `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11 added — pinned to 8.x since the latest versions only support net10.0; build verified green)

### Stage 1d: Domain + Application layers
**Goal**: `Domain/`: `Order` entity, `OrderCreatedEvent` domain event. `Application/`: MediatR `CreateOrderCommand`/`GetOrderQuery`/`UpdateOrderStatusCommand` + handlers, plus repository *interfaces* (`IOrderRepository`, ports only — no EF Core references in this layer).
**Status**: Complete. `IOrderRepository.CreateOrderAsync` returns one atomic `CreateOrderResult(Order, WasCreated)` rather than splitting idempotency check-then-act across two interface methods (the earlier in-memory race, relocated to the repository contract rather than fixed). `CreateOrderCommandHandler` only publishes `OrderCreatedEvent` when `WasCreated` is true. `UpdateOrderStatusCommandHandler` still has a deliberate, unfixed check-then-act race (fetch, then update) - intentionally left as Stage 2's subject. Hit and fixed a real C# gotcha along the way: leftover top-level `class Order` in the old `Program.cs` (global namespace, `internal` by default) was silently shadowing the real `OrderService.Domain.Order` in every file via enclosing-namespace lookup, which C# prioritizes over `using`-imported namespaces - cleared the old Stage 1a implementation out of `Program.cs` to fix it.

### Stage 1e: Infrastructure layer
**Goal**: `OrdersDbContext` (EF Core + Npgsql), `OrderRepository` implementing `IOrderRepository`, idempotency handling via the DB unique constraint (insert -> catch unique-violation -> return existing), DI registration.
**Status**: Complete. `OrdersDbContext` maps `Order`/`IdempotencyKeyEntity` onto the hand-written lowercase `init.sql` schema via explicit Fluent API column mappings (database-first, not code-first migrations). `OrderRepository.CreateOrderAsync` stages both the order and idempotency-key inserts in one `SaveChangesAsync()` (one transaction), catches the Postgres unique-violation (`SqlState 23505`) via an exception filter, clears the change tracker, and looks up the real winning order - later extended with an `IdempotencyConflictException` (-> `409`) when a reused key's content doesn't match the original.

### Stage 1f: Api layer wiring
**Goal**: `Program.cs` wires DI (MediatR, EF Core, repositories) and exposes the same three endpoints, each just building a MediatR command/query and sending it via `IMediator` — no business logic in `Program.cs` itself.
**Success Criteria**: Same curl tests as the in-memory version (create with idempotency key, retry same key -> same order, GET, PATCH) pass again, now backed by real Postgres.
**Status**: Complete. Verified end-to-end through the gateway: create returns `201` + `Location` header; retry with the same key returns the original order (ignoring a different amount sent the second time) with no `Location` header; `GET`/`PATCH` both confirmed against real Postgres. Follow-up refinement added: idempotency key reused with *different* content (different `Amount`) now returns `409 Conflict` via `IdempotencyConflictException` instead of silently returning the old order — matching the Stripe-style behavior discussed earlier. Compares a single field (`Amount`) directly rather than hashing the whole request, appropriate for our current single-field command; noted in the order-lines discussion that this would need to generalize to a request-hash comparison once the command has multiple fields.

### Stage 1g: Outbox pattern
**Deferred - merged into Stage 6.** Reasoning: the outbox pattern's actual value only shows up once there's a real, independent downstream consumer (a broker) that can fail separately from the DB commit. Building the outbox table + write-side now, with no real broker yet, would only re-demonstrate "two inserts commit atomically in one `SaveChangesAsync`" - already proven via the idempotency-key row in Stage 1e. Also avoids locking in an `outbox_messages` schema before Stage 6 decides which broker (RabbitMQ vs. Kafka) actually needs to consume it. See Stage 6 below for the merged scope.
**Status**: Deferred to Stage 6

## Stage 2: Backend data contention
**Goal**: Simulate two concurrent requests racing to update the same order (e.g. both trying to change its status at once) and observe the race play out unguarded first.
**Success Criteria**: Reproduce a visible race condition (lost update) with concurrent requests, then fix it with one of: optimistic concurrency (version/ETag check, reject stale writes), pessimistic locking (DB row lock), or a distributed lock (Redis) if state ends up shared across replicas.
**Status**: Not Started

## Stage 3: Stand up payment-service and ship-service
**Goal**: Two new minimal-API stubs (same ASP.NET Core style as auth-service/order-service), each owning their own step: `payment-service` (`POST /payments` — charge, can succeed or be made to fail on demand for testing) and `ship-service` (`POST /shipments` — ship, same controllable success/failure). Wired into docker-compose and the gateway like the existing services.
**Success Criteria**: Each can be called independently through the gateway and returns success/failure predictably (e.g. a test flag/amount threshold to force a decline).
**Status**: Not Started

## Stage 4: Coordinate the full order -> payment -> ship workflow
**Goal**: Wire the three steps into one flow: place order (order-service) -> charge payment (payment-service) -> ship (ship-service).
**Open decision to resolve when starting this stage**: orchestration (a central coordinator service calls each step in sequence) vs. choreography (each service reacts to events from the others, no central coordinator) vs. a workflow engine (Temporal/Camunda-style) — these differ significantly in architecture, so pick deliberately rather than defaulting.
**Carried over from the traffic-resilience Stage 3 discussion (see `traffic-resilience/PRODUCTION_COMPARISON_NOTES.md`)**: these three services calling each other directly is east-west (service-to-service) traffic, which bypasses the gateway entirely — none of the rate limiting/circuit breaking/auth built in `traffic-resilience`/`auth-service` applies to `order-service` -> `payment-service` -> `ship-service` calls the way it does to external requests through Traefik. Explicitly decide and address this when building the coordination - don't let it happen by accident/unnoticed. Three real options, refined through discussion (not just the original two):
1. Route internal calls back through the gateway too (awkward - Traefik isn't designed for internal service-to-service routing).
2. Build resilience (timeouts/retries/circuit breaking) directly into the calling service's HTTP client - but this is inferior to option 3: even centralized into a shared library, every service still has to *remember* to reference and correctly configure it (duplicated *usage*, not just duplicated code).
3. **(Leaning this way)** Actually stand up a lightweight local service mesh (sidecar model) to genuinely experience how production solves this - Istio needs real Kubernetes, so consider Linkerd or hand-rolling per-service Envoy sidecar containers in docker-compose instead. This is the production-correct answer: push the concern down into infrastructure every call passes through, the same principle already applied via `auth-service` + `forwardAuth` for identity - rather than up into application code every developer has to remember to use correctly.
**Success Criteria**: A full order can complete successfully end-to-end through all three steps via whichever coordination style is chosen.
**Status**: Not Started

## Stage 5: Compensation for the order -> payment -> ship flow (saga pattern)
**Goal**: Implement compensating actions for each failure point:
- Payment declined -> cancel the order (no shipment ever attempted).
- Shipping fails -> refund the payment (compensating call to payment-service) and cancel the order.
**Success Criteria**: Force a decline at the payment step and confirm the order ends up cancelled with no shipment attempted. Force a shipping failure and confirm the payment gets refunded and the order ends up cancelled — no step left half-done.
**Status**: Not Started

## Stage 6 (stretch goal): Migrate the flow to pub-sub (includes the outbox pattern, merged from Stage 1g)
**Goal**: Once Stages 4-5 work as synchronous orchestration (direct HTTP calls), migrate the same order -> payment -> ship flow to an event-driven/pub-sub model on a message broker (RabbitMQ or Kafka, added to docker-compose). `order-service` publishes `OrderPlaced`; `payment-service` subscribes and publishes `PaymentCharged`/`PaymentDeclined`; `ship-service` subscribes to `PaymentCharged`; compensation (Stage 5) becomes event-driven too (e.g. `PaymentDeclined` triggers an `OrderCancelled` handler instead of a direct function call).
**Outbox pattern, built as part of this stage, not before**: `CreateOrderCommandHandler` writes an `OutboxMessage` row (representing `OrderCreated`) in the *same* `SaveChangesAsync()` call as creating the order - guarantees the event is never lost even if the actual broker publish fails independently of the DB commit (the dual-write problem, this time with a *real* second system - the broker - not just a second table). A background processor (hosted service) reads unprocessed outbox rows and publishes them to the chosen broker, marking them processed only after a confirmed publish. Building this now, with a real broker and real consumer already in place, avoids re-testing atomicity we already proved in Stage 1e and avoids locking in a message schema before the broker choice is made.
**Success Criteria**: The same end-to-end success and compensation scenarios from Stages 4-5 still work, now via published/subscribed events instead of direct calls. Additionally: force a crash between the DB commit and the broker publish, and confirm the outbox row survives to be retried/published on recovery rather than the event being silently lost.
**Discussion to have once this is running**: compare the two implementations side by side and cover the actual trade-offs, not just "pub-sub is better" —
- **Why employ pub-sub at all**: decoupling (a service being briefly down doesn't block the caller — the event just waits in the queue), easier fan-out (new subscribers, e.g. an analytics service listening to `OrderPlaced`, can be added without touching the existing flow), and it's the pattern real systems (e.g. Karrot, per the earlier research: RabbitMQ for internal events, Kafka for cross-company events) actually use for this kind of cross-service coordination.
- **What it costs**: messages can be delivered more than once or out of order (subscribers need to be idempotent, which the synchronous version never had to worry about), a failure is no longer "the HTTP call returned an error" — it's "nothing happened yet, or something happened twice," and tracing an order's full journey end-to-end gets harder (no single call stack, need correlation IDs and log aggregation across services).
- **When each is the right choice**: orchestration/synchronous is easier to reason about and debug for a small number of tightly-coupled steps (like our 3-step flow) — pub-sub earns its complexity once you have many subscribers, need resilience to a dependent service being down, or the steps genuinely don't need to happen in lockstep.
**Status**: Not Started
