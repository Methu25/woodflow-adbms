-- Apply manually to woodflow_db. No role changes or account deletion.
BEGIN;
LOCK TABLE public.users IN ACCESS EXCLUSIVE MODE;
ALTER TABLE public.users ADD COLUMN IF NOT EXISTS account_status VARCHAR(20);
ALTER TABLE public.users ADD COLUMN IF NOT EXISTS auth_version INTEGER NOT NULL DEFAULT 0;
UPDATE public.users SET account_status = CASE WHEN is_active IS TRUE THEN 'ACTIVE' ELSE 'DEACTIVATED' END
WHERE account_status IS NULL;
UPDATE public.users SET is_active = (account_status = 'ACTIVE') WHERE is_active IS DISTINCT FROM (account_status = 'ACTIVE');
ALTER TABLE public.users ALTER COLUMN account_status SET DEFAULT 'ACTIVE';
ALTER TABLE public.users ALTER COLUMN account_status SET NOT NULL;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='public.users'::regclass AND conname='users_account_status_check') THEN
        ALTER TABLE public.users ADD CONSTRAINT users_account_status_check
            CHECK (account_status IN ('ACTIVE', 'SUSPENDED', 'DEACTIVATED'));
    END IF;
END $$;
-- Maintain compatibility with existing is_active writes, including setup and local tools.
CREATE OR REPLACE FUNCTION public.sync_user_account_status() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.is_active IS NOT TRUE AND NEW.account_status = 'ACTIVE' THEN
            NEW.account_status := 'DEACTIVATED';
        END IF;
    ELSE
        IF OLD.account_status = 'DEACTIVATED' AND
            (NEW.account_status <> 'DEACTIVATED' OR NEW.is_active IS TRUE) THEN
            RAISE EXCEPTION 'Deactivated accounts cannot be restored' USING ERRCODE = '23514';
        END IF;
        IF NEW.account_status = OLD.account_status AND NEW.is_active IS DISTINCT FROM OLD.is_active THEN
            NEW.account_status := CASE WHEN NEW.is_active IS TRUE THEN 'ACTIVE' ELSE 'DEACTIVATED' END;
        END IF;
        IF NEW.account_status IS DISTINCT FROM OLD.account_status OR NEW.role IS DISTINCT FROM OLD.role THEN
            NEW.auth_version := OLD.auth_version + 1;
        END IF;
    END IF;
    NEW.is_active := (NEW.account_status = 'ACTIVE');
    RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER users_sync_account_status BEFORE INSERT OR UPDATE ON public.users
FOR EACH ROW EXECUTE FUNCTION public.sync_user_account_status();
COMMIT;
