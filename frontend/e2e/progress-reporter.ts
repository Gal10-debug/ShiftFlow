import type { Reporter, TestCase, TestResult } from "@playwright/test/reporter";
export default class ProgressReporter implements Reporter {
  onTestEnd(test: TestCase, result: TestResult) {
    console.log(`[E2E] ${result.status}: ${test.title}`);
  }
}
