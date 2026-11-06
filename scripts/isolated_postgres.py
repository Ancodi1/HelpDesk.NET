#!/usr/bin/env python3
"""Fresh Docker PostgreSQL only. Never accepts a connection or existing container."""
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
import uuid

IMAGE = "postgres:16@sha256:5db6dac2ef5dcff512f4f00a009c3c332b9f2f28f24277175bf3e4a3d02ef83a"
ROOT = Path(__file__).resolve().parents[1]


def clean_env():
    return {k: v for k, v in os.environ.items()
            if not k.upper().startswith(("PG", "HELPDESK_", "DEMO", "JWT", "CONNECTIONSTRINGS", "ASPNETCORE_", "DOTNET_ENVIRONMENT"))}


def command(args, *, data=None, env=None, timeout=120):
    result = subprocess.run(args, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            env=env or clean_env(), timeout=timeout, check=False)
    if result.returncode:
        # Arguments, stdout and stderr may contain credentials or recovered data.
        raise RuntimeError(f"Falló {Path(args[0]).name}; diagnóstico omitido para proteger secretos.")
    return result.stdout


def require(condition):
    # Safety checks must remain active even with PYTHONOPTIMIZE or python -O.
    if not condition:
        raise RuntimeError("Verificación de aislamiento o integridad rechazada.")


class IsolatedPostgres:
    def __enter__(self):
        self.temp = tempfile.TemporaryDirectory(prefix="helpdesk-isolated-")
        self.directory = Path(self.temp.name)
        self.marker = str(uuid.uuid4())
        self.name = "helpdesk-ci-" + self.marker
        self.volume = self.name + "-data"
        self.network = self.name + "-net"
        self.container_id = None
        self.password = secrets.token_hex(32)
        password_file = self.directory / "admin-password"
        password_file.write_text(self.password)
        password_file.chmod(0o600)
        try:
            command(["docker", "volume", "create", "--label", "helpdesk.isolation=" + self.marker, self.volume])
            command(["docker", "network", "create", "--label", "helpdesk.isolation=" + self.marker, self.network])
            self.container_id = command([
                "docker", "run", "-d", "--name", self.name, "--label", "helpdesk.isolation=" + self.marker,
                "--network", self.network, "-p", "127.0.0.1::5432", "--mount",
                f"type=volume,src={self.volume},dst=/var/lib/postgresql/data", "--mount",
                f"type=bind,src={password_file},dst=/run/secrets/pg,readonly",
                "-e", "POSTGRES_PASSWORD_FILE=/run/secrets/pg", IMAGE
            ]).decode().strip()
            for _ in range(90):
                try:
                    self.sql("SELECT 1")
                    break
                except RuntimeError:
                    time.sleep(.5)
            else:
                raise RuntimeError("PostgreSQL aislado no arrancó.")
            self.verify()
            return self
        except BaseException:
            self.__exit__(None, None, None)
            raise

    def sql(self, sql, database="postgres", user="postgres"):
        return command(["docker", "exec", "-i", self.name, "psql", "-X", "-qAt",
                        "-v", "ON_ERROR_STOP=1", "-U", user, "-d", database],
                       data=sql.encode()).decode().strip()

    def verify(self):
        info = json.loads(command(["docker", "inspect", self.name]))[0]
        require(info["Id"] == self.container_id)
        require(info["Config"]["Image"] == IMAGE)
        require(info["Config"]["Labels"]["helpdesk.isolation"] == self.marker)
        require(set(info["NetworkSettings"]["Networks"]) == {self.network})
        mounts = info["Mounts"]
        require(len(mounts) == 2)
        require({m["Name"] for m in mounts if m["Type"] == "volume"} == {self.volume})
        require(all(m["Source"] == str(self.directory / "admin-password") and not m["RW"]
                    for m in mounts if m["Type"] == "bind"))
        network = json.loads(command(["docker", "network", "inspect", self.network]))[0]
        require(network["Driver"] == "bridge" and set(network["Containers"]) == {self.container_id})
        require(network["Labels"]["helpdesk.isolation"] == self.marker)
        binding = info["NetworkSettings"]["Ports"]["5432/tcp"]
        require(len(binding) == 1 and binding[0]["HostIp"] == "127.0.0.1")
        self.port = binding[0]["HostPort"]
        identity_sql = "SELECT system_identifier::text || '|' || pg_postmaster_start_time()::text FROM pg_control_system();"
        env = clean_env()
        env.update(PGPASSWORD=self.password, PGSSLMODE="disable", PGCONNECT_TIMEOUT="5")
        tcp = command(["psql", "-X", "-qAt", "--no-password", "-h", "127.0.0.1", "-p", self.port,
                       "-U", "postgres", "-d", "postgres", "-c", identity_sql], env=env).decode().strip()
        require(tcp == self.sql(identity_sql))
        databases = set(self.sql("SELECT datname FROM pg_database").splitlines())
        require(databases <= {"postgres", "template0", "template1", "helpdesk_test_db", "helpdesk_demo_db", "helpdesk_restore_db"})

    def provision_tests(self):
        self.verify()
        self.test_password = secrets.token_hex(32)
        self.demo_password = secrets.token_hex(32)
        for database, role, password in [("helpdesk_test_db", "helpdesk_test_user", self.test_password),
                                         ("helpdesk_demo_db", "helpdesk_demo_user", self.demo_password)]:
            self.sql(f"CREATE ROLE {role} LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;")
            self.sql(f"CREATE DATABASE {database} OWNER {role};")
            self.sql(f"REVOKE ALL ON DATABASE {database} FROM PUBLIC; GRANT CONNECT ON DATABASE {database} TO {role};")
            require(self.sql("SELECT current_database(), current_user, rolsuper, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname=current_user;", database, role) == f"{database}|{role}|f|f|f")
        self.sql(f"COMMENT ON DATABASE helpdesk_demo_db IS 'HelpDesk.NET demo {self.marker}';")
        self.sql("REVOKE CONNECT ON DATABASE postgres FROM PUBLIC; REVOKE CONNECT ON DATABASE template1 FROM PUBLIC;")
        self.verify()

    def test_environment(self):
        env = clean_env()
        env["DOTNET_ENVIRONMENT"] = "Testing"
        for key, database, role, password in [
            ("HELPDESK_TEST_CONNECTION_STRING", "helpdesk_test_db", "helpdesk_test_user", self.test_password),
            ("HELPDESK_DEMO_TEST_CONNECTION_STRING", "helpdesk_demo_db", "helpdesk_demo_user", self.demo_password)]:
            env[key] = f"Host=127.0.0.1;Port={self.port};Database={database};Username={role};Password={password};Enlist=false"
            if os.environ.get("GITHUB_ACTIONS") == "true":
                print("::add-mask::" + password, flush=True)
                print("::add-mask::" + env[key], flush=True)
        env["HELPDESK_DEMO_TEST_INSTANCE_ID"] = self.marker
        return env

    def restore(self, path, confirmation):
        if confirmation != "RESTORE-ISOLATED":
            raise ValueError("Se requiere --confirm RESTORE-ISOLATED.")
        self.verify()
        # No DROP, --clean or reuse: CREATE fails if the target already exists.
        self.sql("CREATE ROLE helpdesk_restore_user LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;")
        self.sql("CREATE DATABASE helpdesk_restore_db OWNER helpdesk_restore_user;")
        self.sql("REVOKE ALL ON DATABASE helpdesk_restore_db FROM PUBLIC; GRANT CONNECT ON DATABASE helpdesk_restore_db TO helpdesk_restore_user;")
        # Archive may contain executable SQL: trusted archives only, unprivileged role,
        # disposable cluster, exclusive network, no host data mounted.
        with Path(path).open("rb") as source:
            result = subprocess.run(["docker", "exec", "-i", self.name, "pg_restore", "-U", "helpdesk_restore_user",
                                     "-d", "helpdesk_restore_db", "--no-owner", "--no-acl", "--exit-on-error",
                                     "--single-transaction"], stdin=source, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=600)
        if result.returncode:
            raise RuntimeError("Falló restauración aislada; no se mostraron datos del archivo.")
        self.verify()
        return self.fingerprint("helpdesk_restore_db")

    def fingerprint(self, database):
        # Includes row contents/hashes, migrations and sequence values without logging them.
        import hashlib
        parts = []
        for table, key in [("Users", "Id"), ("Tickets", "Id"), ("TicketComments", "Id"),
                           ("TicketActivities", "Id"), ("__EFMigrationsHistory", "MigrationId")]:
            parts.append(self.sql(f'SELECT row_to_json(t)::text FROM (SELECT * FROM public."{table}" ORDER BY "{key}") t;', database))
        sequences = self.sql("SELECT sequencename FROM pg_sequences WHERE schemaname='public' ORDER BY sequencename;", database)
        for sequence in sequences.splitlines():
            require(sequence.replace("_", "").isalnum())
            parts.append(self.sql(f'SELECT last_value, is_called FROM public."{sequence}";', database))
        require(self.sql("SELECT count(*) FROM pg_constraint WHERE connamespace='public'::regnamespace AND NOT convalidated;", database) == "0")
        # PostgreSQL can move varchar->text casts into array elements on dump/restore.
        # CHECK SQL text is not canonical. Compare inventory/keys/validation, and keep
        # definitions for PK/FK/unique constraints where their representation is stable.
        parts.append(self.sql("SELECT conrelid::regclass::text, conname, contype, conkey, confrelid::regclass::text, confkey, condeferrable, condeferred, convalidated, CASE WHEN contype='c' THEN '' ELSE pg_get_constraintdef(oid) END FROM pg_constraint WHERE connamespace='public'::regnamespace ORDER BY conrelid::regclass::text, conname;", database))
        parts.append(self.sql("SELECT indexname, indexdef FROM pg_indexes WHERE schemaname='public' ORDER BY indexname;", database))
        return hashlib.sha256("\n".join(parts).encode()).hexdigest()

    def __exit__(self, *_):
        # Only resources whose names were generated in this process. No global prune.
        for args in [["docker", "rm", "-f", "-v", self.name], ["docker", "volume", "rm", self.volume],
                     ["docker", "network", "rm", self.network]]:
            try:
                command(args)
            except (RuntimeError, subprocess.TimeoutExpired):
                print("Aviso: revisar limpieza del recurso aislado " + args[-1], flush=True)
        self.temp.cleanup()
