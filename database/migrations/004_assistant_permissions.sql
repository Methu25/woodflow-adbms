-- Apply manually to woodflow_db. Existing roles/accounts are unchanged.
BEGIN;
CREATE TABLE IF NOT EXISTS public.assistant_permission_settings (
    id SMALLINT PRIMARY KEY CHECK (id = 1),
    granted_keys TEXT[] NOT NULL DEFAULT ARRAY['dashboard.view']::TEXT[],
    version BIGINT NOT NULL DEFAULT 0 CHECK (version >= 0)
);
INSERT INTO public.assistant_permission_settings (id) VALUES (1) ON CONFLICT (id) DO NOTHING;
COMMIT;
