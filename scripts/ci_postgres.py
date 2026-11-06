#!/usr/bin/env python3
"""Integration tests and real recovery drill on fresh PostgreSQL only."""
import argparse
import subprocess
import sys
from isolated_postgres import IsolatedPostgres, ROOT, require


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", choices=["Debug", "Release"], default="Release")
    args = parser.parse_args()
    with IsolatedPostgres() as pg:
        pg.provision_tests()
        print("Aislamiento Docker/TCP, roles y marca demo verificados; ejecutando suite.", flush=True)
        pg.verify()
        result = subprocess.run(["dotnet", "test", "HelpDesk.NET.slnx", "--no-build", "--no-restore",
                                 "-c", args.configuration, "-m:1", "-nr:false", "--filter", "Category=PostgreSQL"],
                                cwd=ROOT, env=pg.test_environment(), timeout=600)
        if result.returncode:
            raise RuntimeError("Falló suite PostgreSQL; no se ejecuta recuperación.")
        pg.verify()
        expected = pg.fingerprint("helpdesk_demo_db")
        archive = pg.directory / "demo.dump"
        service = pg.directory / "pg_service.conf"
        service.write_text(f"[ci-isolated]\nhost=127.0.0.1\nport={pg.port}\ndbname=helpdesk_demo_db\nuser=helpdesk_demo_user\nsslmode=disable\nconnect_timeout=5\n")
        passfile = pg.directory / "pgpass"
        passfile.write_text(f"127.0.0.1:{pg.port}:helpdesk_demo_db:helpdesk_demo_user:{pg.demo_password}\n")
        service.chmod(0o600)
        passfile.chmod(0o600)
        backup_env = pg.test_environment()
        backup_env.update(PGSERVICEFILE=str(service), PGPASSFILE=str(passfile))
        subprocess.run([sys.executable, str(ROOT / "scripts/recovery.py"), "backup", "--service", "ci-isolated",
                        "--confirm-source", "helpdesk_demo_db", "--output", str(archive)],
                       env=backup_env, cwd=ROOT, timeout=600, check=True)
        # Separate fresh cluster, no access to the source data directory.
        with IsolatedPostgres() as recovery:
            restored = recovery.restore(archive, "RESTORE-ISOLATED")
            require(restored == expected)
        print("Recuperación real correcta: filas, migraciones, secuencias y constraints validados; recursos eliminados.", flush=True)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, AssertionError, subprocess.TimeoutExpired) as error:
        raise SystemExit(str(error) or "Guardia de aislamiento rechazada.") from None
