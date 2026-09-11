CREATE TABLE orders (
    id UUID PRIMARY KEY,
    status TEXT NOT NULL,
    amount NUMERIC NOT NULL
);

CREATE TABLE idempotency_keys (
    key TEXT PRIMARY KEY,
    status TEXT NOT NULL,
    order_id UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE outbox_messages (
    id UUID PRIMARY KEY,
    event_type TEXT NOT NULL,
    payload JSONB NOT NULL,
    processed BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
