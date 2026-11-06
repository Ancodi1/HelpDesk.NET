#!/usr/bin/env python3
"""Read-only backup via explicit libpq service; restore only into a NEW Docker cluster."""
import argparse
import os
from pathlib import Path
import re
import subprocess
from isolated_postgres import IsolatedPostgres, ROOT, clean_env


def outside_repo(value):
    path = Path(value).expanduser().resolve()
    if path.is_relative_to(ROOT):
        raise argparse.ArgumentTypeError("El respaldo debe estar fuera del repositorio.")
    return path


def backup(args):
    # No implicit default connection, no passwords or connection strings in arguments.
    if not re.fullmatch(r"[A-Za-z0-9_-]+", args.service):
        raise ValueError("Nombre de servicio libpq no válido.")
    env = clean_env()
    # libpq credentials/config remain in private files, never command-line passwords.
    for key in ("PGSERVICEFILE", "PGPASSFILE"):
        if key in os.environ:
            env[key] = os.environ[key]
    source = "service=" + args.service
    check = subprocess.run(["psql", "-X", "-qAt", "--no-password", "--dbname", source,
                            "-c", "SELECT current_database();"], env=env, capture_output=True, text=True, timeout=30)
    if check.returncode or check.stdout.strip() != args.confirm_source:
        raise ValueError("Base de origen distinta de --confirm-source o conexión fallida.")
    env["PGOPTIONS"] = "-c default_transaction_read_only=on"
    try:
        with args.output.open("xb") as output:
            os.chmod(args.output, 0o600)
            result = subprocess.run(["pg_dump", "--no-password", "--dbname", source, "--format=custom",
                                     "--no-owner", "--no-acl"], env=env, stdout=output, stderr=subprocess.PIPE, timeout=600)
            if result.returncode:
                raise RuntimeError("Falló pg_dump; diagnóstico omitido para proteger datos.")
    except FileExistsError:
        raise ValueError("El archivo existe; no se sobrescribe.") from None
    except BaseException:
        args.output.unlink(missing_ok=True)
        raise
    print("Respaldo creado (0600). Cifrar y verificar restauración antes de considerarlo recuperable.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="operation", required=True)
    source = commands.add_parser("backup", help="Solo lectura; requiere origen explícito.")
    source.add_argument("--service", required=True)
    source.add_argument("--confirm-source", required=True, help="Nombre exacto de base, sin secretos.")
    source.add_argument("--output", type=outside_repo, required=True)
    restore = commands.add_parser("restore", help="Crea, verifica y elimina un clúster Docker nuevo.")
    restore.add_argument("--backup", type=outside_repo, required=True)
    restore.add_argument("--confirm", required=True, choices=["RESTORE-ISOLATED"])
    args = parser.parse_args()
    if args.operation == "backup":
        backup(args)
    else:
        if not args.backup.is_file():
            raise ValueError("Respaldo no encontrado.")
        with IsolatedPostgres() as pg:
            pg.restore(args.backup, args.confirm)
        print("Restauración aislada comprobada y eliminada; no se ha reemplazado ninguna base existente.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, AssertionError, OSError, subprocess.TimeoutExpired) as error:
        # OSError can include filenames but never output recovered contents or libpq diagnostics.
        raise SystemExit(str(error) or "Guardia de aislamiento rechazada.") from None
