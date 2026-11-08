"""Ensure profiling reports preserve failures and do not conflate wall and summed time."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import subprocess
import sys

spec = importlib.util.spec_from_file_location("profiling", Path(__file__).with_name("performance.py"))
profiling = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profiling)


class TimingTests(unittest.TestCase):
    def test_summary_rejects_a_successful_command_that_executed_no_tests(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "empty.trx"
            path.write_text('''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                <ResultSummary><Counters total="0" executed="0" /></ResultSummary></TestRun>''')
            result = subprocess.run([sys.executable, str(Path(__file__).with_name("summarize-performance.py")),
                                     "--trx", str(path), "--output", str(Path(directory) / "summary.json")], capture_output=True)
            self.assertNotEqual(0, result.returncode)
            self.assertFalse((Path(directory) / "summary.json").exists())

    def test_parallel_durations_failure_outcomes_and_assembly_totals_are_preserved(self):
        xml = '''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
        <TestDefinitions><UnitTest id="a"><TestMethod className="Example.Case" codeBase="/private/tests.dll" /></UnitTest>
        <UnitTest id="b"><TestMethod className="Example.Case" codeBase="/private/tests.dll" /></UnitTest></TestDefinitions>
        <Results><UnitTestResult testId="a" testName="one" duration="00:01:02.500" outcome="Passed" />
        <UnitTestResult testId="b" testName="two" duration="00:00:03.000" outcome="Failed" /></Results>
        <ResultSummary><Counters total="2" passed="1" failed="1" /></ResultSummary></TestRun>'''
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "timing.trx"
            path.write_text(xml)
            result = profiling.summarize_trx(path)
        self.assertEqual("1", result["counts"]["failed"])
        self.assertEqual(65.5, result["summedTestSeconds"])
        self.assertEqual(65.5, result["classesSummedSeconds"]["Example.Case"])
        self.assertEqual("Failed", result["tests"][1]["outcome"])
        self.assertEqual({"tests.dll": 65.5}, result["assembliesSummedSeconds"])


if __name__ == "__main__":
    unittest.main()
