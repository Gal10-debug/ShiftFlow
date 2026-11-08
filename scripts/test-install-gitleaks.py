"""Offline installer regressions: no network, scanner, Docker or credentials required."""
import hashlib
import importlib.util
import io
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("install_gitleaks", Path(__file__).with_name("install-gitleaks.py"))
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)
real_run = subprocess.run


def archive(version="8.30.1", symlink=False):
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w:gz") as package:
        member = tarfile.TarInfo("gitleaks")
        if symlink:
            member.type = tarfile.SYMTYPE
            member.linkname = "/bin/sh"
            package.addfile(member)
        else:
            data = f"#!/bin/sh\nprintf '%s\\n' '{version}'\n".encode()
            member.size = len(data)
            package.addfile(member, io.BytesIO(data))
    return buffer.getvalue()


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="shiftflow-installer-test-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.cache = self.root / "cache"
        self.destination = self.root / "bin"
        self.data = archive()
        self.downloads = []
        self.addCleanup(patch.stopall)
        patch("sys.stdout", new_callable=io.StringIO).start()
        patch.object(installer, "SHA256", hashlib.sha256(self.data).hexdigest()).start()

    def downloader(self, outcomes):
        outcomes = iter(outcomes)

        def run(command, **kwargs):
            if command[0] != "curl":
                return real_run(command, **kwargs)
            self.downloads.append(command)
            self.assertLessEqual(kwargs["timeout"], 100)
            outcome = next(outcomes)
            if outcome == "timeout":
                raise subprocess.TimeoutExpired(command, 100)
            target = Path(command[command.index("--output") + 1])
            target.write_bytes(self.data if outcome == 0 else b"partial failed response")
            return subprocess.CompletedProcess(command, outcome)

        return patch.object(installer.subprocess, "run", side_effect=run)

    def test_primary_download_installs_verified_version(self):
        with self.downloader([0]):
            binary = installer.install(self.cache, self.destination)
        self.assertEqual(binary.name, "gitleaks")
        self.assertTrue(binary.is_file())
        self.assertEqual(len(self.downloads), 1)
        self.assertEqual((self.cache / installer.ASSET).read_bytes(), self.data)

    def test_http_504_after_curl_retries_uses_official_asset_api(self):
        # curl exits 22 when its HTTP-error retries (including 504) are exhausted.
        with self.downloader([22, 0]):
            installer.install(self.cache, self.destination)
        self.assertEqual(len(self.downloads), 2)
        self.assertIn("api.github.com/repos/gitleaks/gitleaks/releases/assets/378332058", self.downloads[1][-1])
        self.assertIn("Accept: application/octet-stream", self.downloads[1])
        self.assertEqual((self.cache / installer.ASSET).read_bytes(), self.data)

    def test_timeout_is_bounded_and_uses_the_alternate_endpoint(self):
        with self.downloader(["timeout", 0]):
            installer.install(self.cache, self.destination)
        self.assertEqual(len(self.downloads), 2)

    def test_verified_cache_needs_no_network_but_still_checks_version(self):
        self.cache.mkdir()
        (self.cache / installer.ASSET).write_bytes(self.data)
        with self.downloader([]):
            installer.install(self.cache, self.destination)
        self.assertEqual(self.downloads, [])

    def test_corrupted_cache_is_replaced_with_verified_download(self):
        self.cache.mkdir()
        (self.cache / installer.ASSET).write_bytes(b"tampered cache")
        with self.downloader([0]):
            installer.install(self.cache, self.destination)
        self.assertEqual((self.cache / installer.ASSET).read_bytes(), self.data)

    def test_downloaded_checksum_mismatch_never_installs_or_uses_fallback(self):
        trusted = self.data
        self.data = b"successful HTTP response with wrong bytes"
        with patch.object(installer, "SHA256", hashlib.sha256(trusted).hexdigest()), self.downloader([0]):
            with self.assertRaisesRegex(ValueError, "checksum mismatch"):
                installer.install(self.cache, self.destination)
        self.assertEqual(len(self.downloads), 1)
        self.assertFalse((self.destination / "gitleaks").exists())
        self.assertFalse((self.cache / installer.ASSET).exists())

    def test_both_endpoints_failing_cannot_report_a_clean_install(self):
        with self.downloader([22, 22]):
            with self.assertRaisesRegex(RuntimeError, "Both official"):
                installer.install(self.cache, self.destination)
        self.assertFalse((self.destination / "gitleaks").exists())
        self.assertFalse((self.cache / installer.ASSET).exists())

    def test_wrong_version_is_not_published_as_the_installed_scanner(self):
        self.data = archive("0.0.0")
        with patch.object(installer, "SHA256", hashlib.sha256(self.data).hexdigest()), self.downloader([0]):
            with self.assertRaisesRegex(RuntimeError, "version"):
                installer.install(self.cache, self.destination)
        self.assertFalse((self.destination / "gitleaks").exists())
        self.assertEqual(list(self.destination.iterdir()), [])

    def test_archive_link_is_not_extracted_or_executed(self):
        self.data = archive(symlink=True)
        with patch.object(installer, "SHA256", hashlib.sha256(self.data).hexdigest()), self.downloader([0]):
            with self.assertRaisesRegex(ValueError, "regular scanner"):
                installer.install(self.cache, self.destination)
        self.assertEqual(list(self.destination.iterdir()), [])


if __name__ == "__main__":
    unittest.main()
