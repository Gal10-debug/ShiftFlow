import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from sanitize import Sanitizer


class ArtifactSecurityTests(unittest.TestCase):
    def test_trace_removes_credentials_headers_cookie_arrays_and_csrf_resources(self):
        with tempfile.TemporaryDirectory() as folder:
            source, target = Path(folder) / "raw.zip", Path(folder) / "safe.zip"
            with zipfile.ZipFile(source, "w") as archive:
                archive.writestr("test.trace", json.dumps({"params": {"value": "KnownPassword!123"}, "type": "before"}) + "\n")
                archive.writestr("test.network", json.dumps({"headers": [
                    {"name": "Cookie", "value": "ShiftFlow.Auth=dynamic-cookie"},
                    {"name": "X-CSRF-TOKEN", "value": "dynamic-csrf-token"}], "cookies": [{"name": "auth", "value": "cookie-value"}]}))
                archive.writestr("resources/login", '{"password":"KnownPassword!123","token":"dynamic-csrf-token"}')
                archive.writestr("post.trace", json.dumps({"params": {"value": "IncorrectPassword!123"}}))
                archive.writestr("post.network", json.dumps({"postData": {"text": json.dumps({"password": "IncorrectPassword!123"})}}))
                archive.writestr("resources/snapshot", '<input value="KnownPassword!123"> dynamic-csrf-token ShiftFlow.Auth=dynamic-cookie')
                archive.writestr("resources/screenshot", b"\x89PNG\r\nimage")
            sanitizer = Sanitizer(["KnownPassword!123"])
            sanitizer.trace(source, target)
            with zipfile.ZipFile(target) as archive:
                text = b"\n".join(archive.read(name) for name in archive.namelist()).decode(errors="replace")
                for secret in ("KnownPassword!123", "IncorrectPassword!123", "dynamic-cookie", "dynamic-csrf-token", "cookie-value"):
                    self.assertNotIn(secret, text)
                self.assertIn("[REDACTED]", text)
                self.assertIn("test.trace", archive.namelist())

    def test_unknown_binary_artifacts_fail_closed(self):
        with self.assertRaises(ValueError):
            Sanitizer().payload(b"\x80\x00unsupported")

    def test_connection_strings_are_scrubbed(self):
        self.assertEqual("Host=127.0.0.1;Password=[REDACTED];Database=e2e", Sanitizer().text("Host=127.0.0.1;Password=hidden-value;Database=e2e"))

    def test_cached_replacements_invalidate_when_new_escaped_secrets_are_discovered(self):
        sanitizer = Sanitizer(["initial-secret"])
        self.assertEqual("[REDACTED]", sanitizer.text("initial-secret"))
        secret = 'new-\\secret-"quoted"'
        sanitizer.discover({"password": secret})
        self.assertEqual("[REDACTED] [REDACTED]", sanitizer.text(secret + " " + json.dumps(secret)[1:-1]))
        self.assertEqual("[REDACTED]", sanitizer.text("initial-secret"))


if __name__ == "__main__":
    unittest.main()
