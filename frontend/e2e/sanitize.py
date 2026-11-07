"""Publish only sanitized Playwright diagnostics; raw captures stay private."""
import html
import json
import os
from pathlib import Path
import re
import shutil
import sys
import zipfile

SENSITIVE = re.compile(r"password|cookie|authorization|csrf|token|secret|connectionstring", re.I)
REDACTED = "[REDACTED]"


class Sanitizer:
    def __init__(self, secrets=()):
        self.secrets = {v for v in secrets if isinstance(v, str) and len(v) >= 8}

    def discover(self, value):
        if isinstance(value, dict):
            if SENSITIVE.search(str(value.get("name", ""))):
                self.remember(value.get("value"))
            for key, item in value.items():
                if SENSITIVE.search(key):
                    self.remember(item)
                if key.lower() == "cookies" and isinstance(item, list):
                    for cookie in item:
                        if isinstance(cookie, dict):
                            self.remember(cookie.get("value"))
                self.discover(item)
        elif isinstance(value, list):
            for item in value:
                self.discover(item)
        elif isinstance(value, str):
            try:
                nested = json.loads(value)
                if isinstance(nested, (dict, list)):
                    self.discover(nested)
            except ValueError:
                pass

    def remember(self, value):
        if isinstance(value, str) and len(value) >= 8:
            self.secrets.add(value)
            if "=" in value:
                for component in value.split(";"):
                    if "=" in component:
                        self.remember(component.split("=", 1)[1].strip())

    def text(self, value):
        for secret in sorted(self.secrets, key=len, reverse=True):
            value = value.replace(secret, REDACTED)
            value = value.replace(json.dumps(secret)[1:-1], REDACTED)
        value = re.sub(r"(?i)(password\s*=)[^;\s]+", r"\1[REDACTED]", value)
        return value

    def clean(self, value):
        if isinstance(value, dict):
            sensitive_header = SENSITIVE.search(str(value.get("name", "")))
            return {key: REDACTED if SENSITIVE.search(key) or sensitive_header and key == "value"
                    else self.clean(item) for key, item in value.items()}
        if isinstance(value, list):
            return [self.clean(item) for item in value]
        if isinstance(value, str):
            try:
                nested = json.loads(value)
                if isinstance(nested, (dict, list)):
                    return json.dumps(self.clean(nested), ensure_ascii=False)
            except ValueError:
                pass
            return self.text(value)
        return value

    @staticmethod
    def documents(data):
        try:
            return [json.loads(data)]
        except (ValueError, UnicodeError):
            result = []
            for line in data.splitlines():
                try:
                    result.append(json.loads(line))
                except (ValueError, UnicodeError):
                    pass
            return result

    def payload(self, data):
        try:
            text = data.decode("utf-8")
        except UnicodeError:
            # Only browser image resources may be copied as binary.
            if data.startswith((b"\x89PNG\r\n", b"\xff\xd8\xff")):
                return data
            raise ValueError("Unsupported binary artifact; refusing publication")
        try:
            return json.dumps(self.clean(json.loads(text)), ensure_ascii=False).encode()
        except ValueError:
            lines = []
            for line in text.splitlines(keepends=True):
                try:
                    lines.append(json.dumps(self.clean(json.loads(line)), ensure_ascii=False) + "\n")
                except ValueError:
                    lines.append(self.text(line))
            return "".join(lines).encode()

    def trace(self, source, target):
        with zipfile.ZipFile(source) as archive:
            entries = [(item.filename, archive.read(item)) for item in archive.infolist() if not item.is_dir()]
        for _, data in entries:
            for document in self.documents(data):
                self.discover(document)
        with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
            for name, data in entries:
                archive.writestr(name, self.payload(data))


def publish(source, destination):
    secrets = [v for k, v in os.environ.items() if SENSITIVE.search(k)]
    sanitizer = Sanitizer(secrets)
    # Build in a private staging directory; failure never exposes a partial report.
    staging = source / "sanitized"
    staging.mkdir(exist_ok=True, mode=0o700)
    report_path = source / "report.json"
    report = json.loads(report_path.read_text()) if report_path.exists() else {"errors": ["No Playwright report; inspect sanitized service logs."]}
    sanitizer.discover(report)
    for trace in source.glob("results/**/trace.zip"):
        with zipfile.ZipFile(trace) as archive:
            for item in archive.infolist():
                if not item.is_dir():
                    for document in sanitizer.documents(archive.read(item)):
                        sanitizer.discover(document)
    links = []
    for source_file in [*source.glob("*.log"), *source.glob("results/**/*")]:
        if not source_file.is_file() or source_file.suffix not in (".log", ".zip", ".png", ".md"):
            continue
        relative = source_file.relative_to(source)
        target = staging / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        if source_file.suffix == ".zip":
            sanitizer.trace(source_file, target)
        else:
            target.write_bytes(sanitizer.payload(source_file.read_bytes()))
        links.append(str(relative))
    clean_report = sanitizer.clean(report)
    # Attachment paths must refer to published files, without private absolute paths.
    serialized = json.dumps(clean_report, ensure_ascii=False, indent=2).replace(str(source), ".")
    (staging / "report.json").write_text(serialized)
    (staging / "index.html").write_text("<!doctype html><meta charset='utf-8'><title>ShiftFlow E2E report</title>"
        "<h1>ShiftFlow E2E</h1><p>Sanitized diagnostics. Open trace.zip with Playwright Trace Viewer.</p><ul>"
        + "".join(f'<li><a href="{html.escape(link)}">{html.escape(link)}</a></li>' for link in ["report.json", *links])
        + "</ul><pre>" + html.escape(serialized) + "</pre>")
    if destination.exists():
        shutil.rmtree(destination)
    shutil.copytree(staging, destination)
    stats = clean_report.get("stats", {})
    print("E2E results:", json.dumps(stats))
    for error in clean_report.get("errors", []):
        print(json.dumps(error, ensure_ascii=False))
    def failures(suites):
        for suite in suites:
            for spec in suite.get("specs", []):
                for test in spec.get("tests", []):
                    for result in test.get("results", []):
                        if result.get("status") not in ("passed", "skipped"):
                            print(spec["title"], result.get("status"))
                            for error in result.get("errors", []):
                                print(error.get("message", ""))
            failures(suite.get("suites", []))
    failures(clean_report.get("suites", []))
    print("Sanitized report: frontend/e2e-report/index.html")


if __name__ == "__main__":
    publish(Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve())
