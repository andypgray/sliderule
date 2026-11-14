"""The guards that keep perf_ratios.py from publishing a ratio over the wrong kind of run.

Run with `python3 -m unittest discover -s .github/scripts -p "test_*.py"`; the performance workflow does before it
measures.
"""

import unittest
from pathlib import Path

import perf_ratios


def timings(outcome: str) -> str:
    return "\n".join([
        "timings (wall clock, seconds):",
        "  workspace load   1.00",
        "  extraction       2.00",
        "  total            3.00",
        f"cache: {outcome}, 3 of 3 projects extracted",
    ])


class ScriptedRunner(perf_ratios.Runner):
    """A runner whose checks print the cache outcomes it is handed, in order, and run nothing."""

    def __init__(self, *outcomes: str) -> None:
        super().__init__(["sliderule"], Path("cache"))
        self.outcomes = list(outcomes)

    def check(self, bed, *extra):
        return 1.0, timings(self.outcomes.pop(0)), ["check", *extra]


BED = perf_ratios.Bed("bed", Path("bed.slnx"), Path("spec.csproj"))


class GuardTests(unittest.TestCase):
    def test_a_hit_arm_whose_timed_check_missed_is_refused(self):
        for outcome in ("miss", "partial", "disabled"):
            with self.subTest(outcome=outcome), self.assertRaisesRegex(perf_ratios.MeasurementError, "not hit"):
                ScriptedRunner("hit", outcome).hit(BED)

    def test_a_cold_arm_that_read_the_cache_is_refused(self):
        with self.assertRaisesRegex(perf_ratios.MeasurementError, "not disabled"):
            ScriptedRunner("hit").cold(BED)

    def test_a_warm_up_that_read_the_cache_is_refused(self):
        with self.assertRaisesRegex(perf_ratios.MeasurementError, "not disabled"):
            ScriptedRunner("hit").discard(BED)

    def test_a_run_with_no_timings_block_is_refused(self):
        with self.assertRaisesRegex(perf_ratios.MeasurementError, "no timings block"):
            perf_ratios.parse_timings("Checked 3 rules", ["check"])

    def test_the_arms_that_ran_as_asked_are_measured(self):
        hit = ScriptedRunner("miss", "hit").hit(BED)
        cold = ScriptedRunner("disabled").cold(BED)
        self.assertEqual((hit.outcome, cold.outcome, cold.projects), ("hit", "disabled", 3))
        self.assertEqual(cold.phases, {"workspace load": 1.0, "extraction": 2.0, "total": 3.0})


if __name__ == "__main__":
    unittest.main()
