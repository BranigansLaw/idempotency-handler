-- The idempotency ledger for externally retryable commands. Keyed by (scope,
-- idempotency_key); `request_hash` binds the key to a specific request so reusing a key
-- with a different payload is rejected. The stored response (status + body) is replayed
-- on a duplicate of a completed command. A row is written inside the same transaction as
-- the command's side effect, so the ledger entry and the side effect commit or roll back
-- together. While a command is in flight the row is uncommitted, so a concurrent
-- duplicate blocks on this primary key until the original commits (then replays) or rolls
-- back (then proceeds).
CREATE TABLE IF NOT EXISTS idempotency_keys (
    scope text NOT NULL,
    idempotency_key text NOT NULL,
    request_hash bytea NOT NULL,
    response_status_code integer NULL,
    response_body bytea NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (scope, idempotency_key)
);
