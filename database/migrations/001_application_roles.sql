-- Run manually against woodflow_db. Never applied by application startup.
BEGIN;
LOCK TABLE public.users IN ACCESS EXCLUSIVE MODE;

-- Replace role-only CHECK constraints, including non-default constraint names.
DO $$
DECLARE constraint_name text;
BEGIN
    FOR constraint_name IN
        SELECT c.conname FROM pg_constraint c
        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY(c.conkey)
        WHERE c.conrelid = 'public.users'::regclass AND c.contype = 'c' AND a.attname = 'role'
            AND array_length(c.conkey, 1) = 1
    LOOP
        EXECUTE format('ALTER TABLE public.users DROP CONSTRAINT %I', constraint_name);
    END LOOP;
END $$;

UPDATE public.users SET role = 'ASSISTANT'
WHERE role IN ('CASHIER', 'WORKSHOP_SUPERVISOR');

ALTER TABLE public.users ADD CONSTRAINT users_role_check
    CHECK (role IN ('OWNER', 'ASSISTANT'));
COMMIT;
