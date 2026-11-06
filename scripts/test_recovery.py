"""Guard tests must not contact Docker or any database."""
import argparse
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from isolated_postgres import IsolatedPostgres, ROOT, clean_env
from recovery import outside_repo, backup


class RecoveryGuards(unittest.TestCase):
    def test_restore_rejects_confirmation_before_docker_or_sql(self):
        with patch("isolated_postgres.command") as command:
            with self.assertRaises(ValueError):
                IsolatedPostgres().restore(Path("/nonexistent"), "wrong")
            command.assert_not_called()

    def test_wrong_container_identity_stops_before_tcp_or_sql(self):
        pg = IsolatedPostgres()
        pg.name = "generated-container"
        pg.container_id = "expected-id"
        with patch("isolated_postgres.command", return_value=b'[{"Id":"different-id"}]') as command:
            with self.assertRaises(RuntimeError):
                pg.verify()
            command.assert_called_once()

    def test_existing_archive_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "existing.dump"
            output.write_bytes(b"preservar")
            args = argparse.Namespace(service="isolated", confirm_source="expected", output=output)
            with patch("recovery.subprocess.run") as run:
                run.return_value.returncode = 0
                run.return_value.stdout = "expected\n"
                with self.assertRaises(ValueError):
                    backup(args)
                self.assertEqual(1, run.call_count)
                self.assertEqual(b"preservar", output.read_bytes())

    def test_archives_cannot_be_written_or_read_in_repository(self):
        for path in [ROOT / "db.dump", ROOT / "scripts" / ".." / "backup.dump"]:
            with self.assertRaises(argparse.ArgumentTypeError):
                outside_repo(str(path))

    def test_inherited_database_configuration_is_removed(self):
        with patch.dict("os.environ", {"PGHOST": "forbidden", "ConnectionStrings__DefaultConnection": "forbidden",
                                       "HELPDESK_TEST_CONNECTION_STRING": "forbidden", "Demo__Enabled": "true"}):
            env = clean_env()
            self.assertNotIn("PGHOST", env)
            self.assertNotIn("ConnectionStrings__DefaultConnection", env)
            self.assertNotIn("HELPDESK_TEST_CONNECTION_STRING", env)
            self.assertNotIn("Demo__Enabled", env)

    def test_backup_rejects_wrong_source_before_dump_or_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "no.dump"
            args = argparse.Namespace(service="isolated", confirm_source="expected", output=output)
            with patch("recovery.subprocess.run") as run:
                run.return_value.returncode = 0
                run.return_value.stdout = "different\n"
                with self.assertRaises(ValueError):
                    backup(args)
                self.assertEqual(1, run.call_count)
                self.assertFalse(output.exists())

    def test_backup_rejects_service_connection_injection(self):
        with patch("recovery.subprocess.run") as run:
            with self.assertRaises(ValueError):
                backup(argparse.Namespace(service="name host=forbidden"))
            run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
