## Stage 1: Push to a remote (GitHub) - prerequisite
**Goal**: Local git repo (set up at the `API-Gateway` root, one repo for the whole project - auth-service/user-service/order-service/traffic-resilience/order-workflow all together) pushed to a GitHub repo (private is fine, free). CI can't run against a purely local repo - this is the real blocker to everything below.
**Status**: Not Started

## Stage 2: Path-filtered CI - build/test only the service that actually changed
**Goal**: A GitHub Actions workflow that builds/tests `auth-service`, `user-service`, and `order-service` independently, triggered only when files under that specific service's folder actually changed in the push/PR - mirroring the deployment-independence property already observed locally via Docker's per-service build-context isolation (see the Karrot Pay monorepo discussion - "money changes only trigger money application rebuilds").
**Open decision to resolve when starting**: two ways to do path filtering, meaningfully different in granularity -
1. `on.push.paths:` at the whole-workflow level (coarse - the entire workflow either runs or doesn't, one workflow file per service).
2. The `dorny/paths-filter` action for per-job conditional logic inside *one* workflow file (finer-grained - one workflow, multiple jobs, each independently gated on its own path filter).
**Success Criteria**: Push a commit touching only `order-service/**` and confirm only that service's job/workflow runs - `auth-service`/`user-service` jobs are skipped entirely, not just fast/cached.
**Status**: Not Started

## Stage 3 (stretch): Push built images to a registry
**Goal**: Once Stage 2 works, push the built Docker image for the changed service to GitHub Container Registry (`ghcr.io`) - directly integrated with GitHub Actions, no separate signup, free for reasonable usage.
**Note**: this is "CI" (build + package), not real "CD" - actually deploying those images somewhere (a real server, a cloud VM) is a bigger, separate question not in scope here; we have no deployment target beyond local Docker Desktop today.
**Status**: Not Started
