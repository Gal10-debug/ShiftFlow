#!/usr/bin/env python3
"""Install the pinned Linux x64 scanner, verifying downloaded or cached bytes."""
import argparse
import hashlib
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tarfile
import tempfile

VERSION = "8.30.1"
ASSET = "gitleaks_8.30.1_linux_x64.tar.gz"
SHA256 = "551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb"
DOWNLOADS = [
    ("release URL", f"https://github.com/gitleaks/gitleaks/releases/download/v{VERSION}/{ASSET}", []),
    ("release asset API", "https://api.github.com/repos/gitleaks/gitleaks/releases/assets/378332058",
     ["--header", "Accept: application/octet-stream", "--header", "X-GitHub-Api-Version: 2022-11-28"]),
]


def verify(path):
    if hashlib.sha256(path.read_bytes()).hexdigest() != SHA256:
        raise ValueError("Gitleaks archive checksum mismatch; refusing installation.")


def archive_for(cache):
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / ASSET
    if archive.exists():
        try:
            verify(archive)
            print("Using checksum-verified Gitleaks archive cache.")
            return archive
        except ValueError:
            print("Cached archive failed verification; downloading a fresh copy.")
            archive.unlink()
    with tempfile.TemporaryDirectory(prefix=".gitleaks-download-", dir=cache) as directory:
        partial = Path(directory) / ASSET
        for name, url, headers in DOWNLOADS:
            print(f"Downloading pinned Gitleaks via {name}.")
            try:
                result = subprocess.run([
                    "curl", "--fail", "--silent", "--show-error", "--location",
                    "--proto", "=https", "--proto-redir", "=https", "--retry", "2", "--retry-connrefused",
                    "--connect-timeout", "10", "--max-time", "30", "--retry-max-time", "70",
                    *headers, "--output", str(partial), url,
                ], capture_output=True, timeout=100)
            except subprocess.TimeoutExpired:
                print(f"Gitleaks {name} timed out.")
                continue
            if result.returncode:
                print(f"Gitleaks {name} unavailable (curl exit {result.returncode}).")
                continue
            # A successful HTTP response with wrong bytes is an integrity failure,
            # not permission to change the checksum or use an unchecked fallback.
            verify(partial)
            partial.replace(archive)
            return archive
    raise RuntimeError("Both official Gitleaks download paths failed; the scan cannot run.")


def install(cache, destination):
    archive = archive_for(cache)
    destination.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(prefix=".gitleaks-", dir=destination, delete=False) as output:
        temporary = Path(output.name)
        try:
            with tarfile.open(archive, "r:gz") as package:
                member = package.getmember("gitleaks")
                if not member.isfile():
                    raise ValueError("Gitleaks archive must contain a regular scanner executable.")
                with package.extractfile(member) as source:
                    shutil.copyfileobj(source, output)
            output.flush()
        except BaseException:
            temporary.unlink(missing_ok=True)
            raise
    try:
        temporary.chmod(0o755)
        version = subprocess.run([str(temporary), "version"], capture_output=True, text=True, timeout=10)
        if version.returncode or version.stdout.strip() != VERSION:
            raise RuntimeError("Installed Gitleaks version did not match the pinned release.")
        binary = destination / "gitleaks"
        os.replace(temporary, binary)
        print(f"Installed checksum-verified Gitleaks {VERSION}.")
        return binary
    finally:
        temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache-dir", type=Path, required=True)
    parser.add_argument("--install-dir", type=Path, required=True)
    args = parser.parse_args()
    if platform.system() != "Linux" or platform.machine().lower() not in ("x86_64", "amd64"):
        parser.error("This CI installer requires Linux x64; local audits may use a separately verified native scanner.")
    try:
        install(args.cache_dir, args.install_dir)
        return 0
    except (OSError, RuntimeError, ValueError, KeyError, tarfile.TarError, subprocess.TimeoutExpired) as error:
        print(f"Scanner installation failed ({type(error).__name__}); no clean security result is claimed.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
