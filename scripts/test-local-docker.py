#!/usr/bin/env python3
"""Safety regressions for the local Docker entry point; no Docker required."""
import copy
import importlib.util
import json
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("local_docker", __file__.replace("test-local-docker.py", "local-docker.py"))
local = importlib.util.module_from_spec(spec)
spec.loader.exec_module(local)


class LocalDockerTests(unittest.TestCase):
    def setUp(self):
        self.env = {"POSTGRES_DB": "shiftmanager", "POSTGRES_USER": "shiftmanager", "POSTGRES_PASSWORD": "synthetic-test-only"}
        self.container = {
            "Config": {"Labels": {"com.docker.compose.project": "shiftflow", "com.docker.compose.service": "postgres",
                                   "com.docker.compose.project.working_dir": str(local.ROOT)},
                       "Env": [f"{key}={value}" for key, value in self.env.items()]},
            "Mounts": [{"Type": "volume", "Name": local.VOLUME, "Destination": local.TARGET, "RW": True}],
            "NetworkSettings": {"Networks": {local.NETWORK: {}}},
            "HostConfig": {"PortBindings": {"5432/tcp": [{"HostIp": "127.0.0.1", "HostPort": "5432"}]}},
            "State": {"Status": "exited", "Running": False}, "Image": "sha256:" + "a" * 64}
        self.volume = {"Name": local.VOLUME, "Driver": "local", "Options": None,
                       "Labels": {"com.docker.compose.project": "shiftflow", "com.docker.compose.volume": "postgres-data"}}
        self.network = {"Labels": {"com.docker.compose.project": "shiftflow", "com.docker.compose.network": "default"}}
        self.catalog = {"database": "shiftmanager", "owner": "shiftmanager", "databases": ["postgres", "shiftmanager"],
                        "users_table": True, "migrations": ["001_initial"]}

    def verify(self):
        local.verify_metadata(self.container, self.volume, [local.CONTAINER], self.network, self.env)

    def test_regular_database_metadata_accepted(self):
        self.verify()

    def test_demo_ownership_and_mounts_rejected(self):
        for section, key, value in [("container", "project", "shiftflow-demo"), ("volume", "project", "shiftflow-demo")]:
            with self.subTest(section=section):
                original = copy.deepcopy(getattr(self, section))
                obj = getattr(self, section)
                labels = obj["Config"]["Labels"] if section == "container" else obj["Labels"]
                labels["com.docker.compose." + key] = value
                with self.assertRaises(local.GuardError): self.verify()
                setattr(self, section, original)
        self.container["Mounts"][0]["Name"] = "demo-data"
        with self.assertRaises(local.GuardError): self.verify()

    def test_other_volume_consumers_rejected(self):
        with self.assertRaises(local.GuardError):
            local.verify_metadata(self.container, self.volume, [local.CONTAINER, "other"], self.network, self.env)

    def test_public_database_port_rejected(self):
        self.container["HostConfig"]["PortBindings"]["5432/tcp"][0]["HostIp"] = "0.0.0.0"
        with self.assertRaises(local.GuardError): self.verify()

    def test_password_mismatch_rejected_without_echoing_secret(self):
        self.env["POSTGRES_PASSWORD"] = "do-not-print-this"
        with self.assertRaises(local.GuardError) as error: self.verify()
        self.assertNotIn(self.env["POSTGRES_PASSWORD"], str(error.exception))

    def test_wrong_checkout_network_and_pgdata_rejected(self):
        for mutate in [lambda: self.container["Config"]["Labels"].update({"com.docker.compose.project.working_dir": "/other"}),
                       lambda: self.container["NetworkSettings"]["Networks"].update({"demo_default": {}}),
                       lambda: self.container["Config"]["Env"].append("PGDATA=/other")]:
            original = copy.deepcopy(self.container)
            mutate()
            with self.assertRaises(local.GuardError): self.verify()
            self.container = original

    def test_pending_migrations_reported_without_writes(self):
        self.assertEqual(["002_next"], local.verify_catalog(self.catalog, {"001_initial", "002_next"}))
        self.assertEqual([], local.verify_catalog(self.catalog, {"001_initial"}))

    def test_wrong_database_owner_demo_or_future_schema_rejected(self):
        for key, value in [("database", "shiftflow_demo"), ("owner", "demo"), ("users_table", False),
                           ("databases", ["postgres", "shiftmanager", "shiftflow_demo"]), ("migrations", ["999_future"])]:
            with self.subTest(key=key), self.assertRaises(local.GuardError):
                local.verify_catalog({**self.catalog, key: value}, {"001_initial"})

    def test_snapshot_uses_stdin_readonly_source_and_no_network(self):
        calls = []
        def run(args, **kwargs):
            calls.append((args, kwargs))
            return "70" if args[-2:] == ["-u", "postgres"] else json.dumps(self.catalog)
        with patch.object(local, "run", side_effect=run):
            self.assertEqual(self.catalog, local.catalog(self.container))
        args, kwargs = calls[-1]
        self.assertIn("-i", args)
        self.assertIn(f"type=volume,src={local.VOLUME},dst=/source,readonly", args)
        self.assertEqual("none", args[args.index("--network") + 1])
        self.assertEqual(local.SQL, kwargs["data"])
        self.assertIn("BEGIN READ ONLY", kwargs["data"])

    def test_up_pending_migrations_cannot_start_build_or_recreate_database(self):
        calls = []
        def run(args, **kwargs):
            calls.append(args)
            if args[-2:] == ["--format", "json"]:
                return json.dumps({"services": {"postgres": {"environment": self.env}}})
            if args[:3] == ["docker", "ps", "-a"]: return local.CONTAINER + "\n"
            return ""
        objects = {"container": self.container, "volume": self.volume, "network": self.network}
        with patch.object(local, "run", side_effect=run), patch.object(local, "inspect", side_effect=lambda kind, name: objects[kind]), \
             patch.object(local, "catalog", return_value={**self.catalog, "migrations": []}), patch("sys.argv", ["local-docker.py", "up"]):
            with self.assertRaisesRegex(local.GuardError, "Pending migrations"):
                local.main()
        self.assertFalse(any(word in args for args in calls for word in ["start", "up", "build", "down", "rm"]))

    def test_password_is_npgsql_quoted_and_docker_dns_used(self):
        result = local.connection_string({**self.env, "POSTGRES_PASSWORD": 'a;"b$c'})
        self.assertIn('Password="a;""b$c";', result)
        self.assertIn("Host=postgres;", result)


if __name__ == "__main__":
    unittest.main()
