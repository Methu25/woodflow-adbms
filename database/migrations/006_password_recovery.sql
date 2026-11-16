-- Apply manually after 003_user_account_status.sql. No account or role changes.
BEGIN;
CREATE TABLE IF NOT EXISTS public.password_recovery_requests (
    request_id UUID PRIMARY KEY,
    requester_user_id INTEGER NOT NULL REFERENCES public.users(user_id),
    selected_owner_id INTEGER NOT NULL REFERENCES public.users(user_id),
    status VARCHAR(12) NOT NULL DEFAULT 'PENDING'
        CHECK (status IN ('PENDING','APPROVED','COMPLETED','DENIED','EXPIRED')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    expires_at TIMESTAMPTZ NOT NULL,
    requester_auth_version INTEGER NOT NULL,
    processed_by_owner_id INTEGER REFERENCES public.users(user_id),
    approving_owner_auth_version INTEGER,
    processed_at TIMESTAMPTZ,
    code_hash BYTEA,
    consumed_at TIMESTAMPTZ,
    CHECK (requester_user_id <> selected_owner_id),
    CHECK (processed_by_owner_id IS NULL OR processed_by_owner_id = selected_owner_id),
    CHECK (code_hash IS NULL OR octet_length(code_hash) = 32),
    CHECK ((status = 'APPROVED') = (code_hash IS NOT NULL)),
    CHECK ((status = 'COMPLETED') = (consumed_at IS NOT NULL)),
    CHECK (status NOT IN ('APPROVED','COMPLETED','DENIED') OR
        (processed_by_owner_id IS NOT NULL AND processed_at IS NOT NULL)),
    CHECK (status NOT IN ('APPROVED','COMPLETED') OR approving_owner_auth_version IS NOT NULL)
);
CREATE UNIQUE INDEX IF NOT EXISTS recovery_one_open_per_user
    ON public.password_recovery_requests(requester_user_id) WHERE status IN ('PENDING','APPROVED');
CREATE UNIQUE INDEX IF NOT EXISTS recovery_code_hash
    ON public.password_recovery_requests(code_hash) WHERE code_hash IS NOT NULL;
CREATE INDEX IF NOT EXISTS recovery_owner_inbox
    ON public.password_recovery_requests(selected_owner_id, created_at DESC);
CREATE INDEX IF NOT EXISTS recovery_requester_history
    ON public.password_recovery_requests(requester_user_id, created_at DESC);
COMMIT;
