-- Apply manually after 006. No role, password, or business-record changes.
BEGIN;
CREATE TABLE IF NOT EXISTS public.auth_sessions (
    session_hash BYTEA PRIMARY KEY CHECK (octet_length(session_hash)=32),
    user_id INTEGER NOT NULL REFERENCES public.users(user_id) ON DELETE RESTRICT,
    auth_version INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    expires_at TIMESTAMPTZ NOT NULL DEFAULT (clock_timestamp()+interval '8 hours'),
    revoked_at TIMESTAMPTZ,
    CHECK (expires_at > created_at)
);
CREATE INDEX IF NOT EXISTS auth_sessions_expiry ON public.auth_sessions(expires_at);
CREATE INDEX IF NOT EXISTS auth_sessions_user ON public.auth_sessions(user_id);
-- Resolution metadata belongs to the recovery workflow; this is not an Audit Logs table.
ALTER TABLE public.password_recovery_requests
    ADD COLUMN IF NOT EXISTS previous_owner_id INTEGER REFERENCES public.users(user_id),
    ADD COLUMN IF NOT EXISTS resolved_by_owner_id INTEGER REFERENCES public.users(user_id),
    ADD COLUMN IF NOT EXISTS resolved_at TIMESTAMPTZ;
COMMIT;
