#!/usr/bin/env python3
"""Regressions for the real-record migration comparison; no database access."""
import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("rehearsal", Path(__file__).with_name("validate-local-migrations.py"))
rehearsal = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rehearsal)


class MigrationComparisonTests(unittest.TestCase):
    def setUp(self):
        self.before = {"AspNetUsers": {"count": 8, "sha256": "original-users"},
                       "SchedulePublication": {"count": 3, "sha256": "immutable-snapshots"},
                       "ShiftTypeDefinition": {"count": 51, "sha256": "old-definitions"},
                       "__EFMigrationsHistory": {"count": 12, "sha256": "old-history"}}
        self.expected = {"count": 51, "sha256": "expected-two-updated-definitions"}
        self.after = {**copy.deepcopy(self.before), "ShiftTypeDefinition": self.expected.copy(),
                      "__EFMigrationsHistory": {"count": 17, "sha256": "new-history"},
                      **{table: {"count": 0, "sha256": "empty"} for table in rehearsal.NEW_TABLES}}

    def test_exact_expected_changes_pass(self):
        rehearsal.compare(self.before, self.after, self.expected)

    def test_same_count_changed_authentication_data_fails(self):
        self.after["AspNetUsers"]["sha256"] = "changed-password-hash"
        with self.assertRaisesRegex(RuntimeError, "AspNetUsers"):
            rehearsal.compare(self.before, self.after, self.expected)

    def test_published_snapshot_changes_fail(self):
        self.after["SchedulePublication"]["sha256"] = "mutated"
        with self.assertRaisesRegex(RuntimeError, "SchedulePublication"):
            rehearsal.compare(self.before, self.after, self.expected)

    def test_deleted_rows_fail(self):
        self.after["AspNetUsers"]["count"] -= 1
        with self.assertRaises(RuntimeError): rehearsal.compare(self.before, self.after, self.expected)

    def test_seeded_new_tables_fail(self):
        self.after["AbsenceRequest"]["count"] = 1
        with self.assertRaisesRegex(RuntimeError, "empty"):
            rehearsal.compare(self.before, self.after, self.expected)

    def test_missing_existing_table_fails(self):
        del self.after["SchedulePublication"]
        with self.assertRaisesRegex(RuntimeError, "disappeared"):
            rehearsal.compare(self.before, self.after, self.expected)

    def test_unexpected_new_table_fails(self):
        self.after["UnexpectedTable"] = {"count": 0, "sha256": "empty"}
        with self.assertRaisesRegex(RuntimeError, "new tables"):
            rehearsal.compare(self.before, self.after, self.expected)

    def test_wrong_history_count_fails(self):
        self.after["__EFMigrationsHistory"]["count"] = 16
        with self.assertRaisesRegex(RuntimeError, "history"):
            rehearsal.compare(self.before, self.after, self.expected)

    def test_unexpected_configuration_changes_fail(self):
        self.after["ShiftTypeDefinition"]["sha256"] = "renamed-or-reordered"
        with self.assertRaisesRegex(RuntimeError, "ShiftTypeDefinition"):
            rehearsal.compare(self.before, self.after, self.expected)


if __name__ == "__main__":
    unittest.main()
