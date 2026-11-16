# Coding agent instructions

- Read and follow [TEAM_DEVELOPMENT_SETUP.md](TEAM_DEVELOPMENT_SETUP.md) before changes. It is the authoritative setup and team workflow guide.
- Use the existing layered backend, repository pattern, Angular structure and WoodFlow design system. Respect assigned module ownership.
- Never modify protected authentication, authorization, account management, permissions, bootstrap, session, recovery code or security tests without explicit security-owner authorization. See section 9 of the guide. Read/use them and integrate new modules with existing backend RBAC instead of bypassing checks.
- Coordinate shared integration files with their owners. Avoid unrelated changes or refactoring. Do not modify the teammate-owned Audit Logs module without authorization.
- Preserve existing database behavior and data. Fresh databases use consolidated schema only; existing databases use reviewed outstanding migrations. Do not rewrite migrations 001–007.
- Never expose secrets or include credentials, certificate keys, local settings, database exports or generated artifacts in Git.
- Run relevant portability, regression and security checks. Distinguish isolated/mocked checks from live PostgreSQL verification; never point destructive tests at the working database.
- Never stage, commit, push or perform destructive database operations unless explicitly requested. Do not remove local credentials or certificates.
