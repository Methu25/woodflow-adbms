-- Apply manually after 004. No existing accounts, roles or grants are replaced.
BEGIN;
CREATE TABLE IF NOT EXISTS public.assistant_user_permissions (
    user_id INTEGER PRIMARY KEY REFERENCES public.users(user_id) ON DELETE RESTRICT,
    granted_keys TEXT[], -- NULL means inherit the ASSISTANT role defaults.
    version BIGINT NOT NULL DEFAULT 0 CHECK (version >= 0)
);
-- Retain the version on reset, preventing stale edits after override/reset cycles.
-- Role changes discard old overrides so they cannot resurface after promotion/demotion.
CREATE OR REPLACE FUNCTION public.clear_assistant_permission_override() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.role IS DISTINCT FROM OLD.role THEN
        UPDATE public.assistant_user_permissions SET granted_keys = NULL, version = version + 1
        WHERE user_id = NEW.user_id;
    END IF;
    RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER users_clear_permission_override AFTER UPDATE OF role ON public.users
FOR EACH ROW EXECUTE FUNCTION public.clear_assistant_permission_override();
COMMIT;
