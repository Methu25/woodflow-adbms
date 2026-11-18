-- Run manually after 001_application_roles.sql. Only a code hash is persisted.
BEGIN;
CREATE TABLE IF NOT EXISTS public.owner_setup_state (
    id SMALLINT PRIMARY KEY CHECK (id = 1),
    code_hash BYTEA CHECK (code_hash IS NULL OR octet_length(code_hash) = 32),
    expires_at TIMESTAMPTZ,
    consumed_at TIMESTAMPTZ,
    initialized_at TIMESTAMPTZ,
    initialized_owner_id INT REFERENCES public.users(user_id) ON DELETE RESTRICT,
    CHECK ((code_hash IS NULL) = (expires_at IS NULL)),
    CHECK (initialized_at IS NULL OR (code_hash IS NULL AND consumed_at IS NOT NULL))
);
INSERT INTO public.owner_setup_state (id) VALUES (1) ON CONFLICT (id) DO NOTHING;
COMMIT;
