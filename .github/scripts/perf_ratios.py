#!/usr/bin/env python3
"""Measure SlideRule's performance as ratios inside one job, and write them with the run's provenance.

Each bed is a solution and its spec. Every round times three arms in an order that rotates by round:

  build  a clean `dotnet build <solution> --no-incremental --no-restore`
  cold   `check --no-cache --timings`
  hit    an untimed `check` that fills the cache, then a timed `check --timings`

A ratio is computed per round from that round's arms and summarised as the median of the rounds with the
lowest and highest, because both arms of one round ran on the same machine minutes apart. The first check
after any build pays a once-per-build warm-up, so one discarded `check --no-cache` runs before the first
measured check of each bed and after every build arm. It has to be `--no-cache`: a cached check can skip the
workspace load and leave the warm-up inside the next measured cold arm.

Standard library only, so the same script runs on Windows too.
"""

from __future__ import annotations

import argparse
import datetime
import os
import platform
import re
import statistics
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass, field
from pathlib import Path

ARMS = ("build", "cold", "hit")
# Must agree with PhaseTimings.Format in src/SlideRule.Cli/Pipeline/PhaseTimings.cs, which writes these lines.
PHASE_ROW = re.compile(r"^  (?P<name>[a-z]+(?: [a-z]+)?) +(?P<seconds>\d+\.\d{2})$")
CACHE_LINE = re.compile(r"^cache: (?P<outcome>[a-z]+), (?P<extracted>\d+) of (?P<projects>\d+) projects extracted$")


class MeasurementError(Exception):
    """A run the measurement cannot use: an unexpected exit code, or a cache line that is not what the arm needs."""


@dataclass
class Timed:
    seconds: float
    phases: dict[str, float] = field(default_factory=dict)
    outcome: str | None = None
    projects: int | None = None


@dataclass
class Round:
    build: Timed
    cold: Timed
    hit: Timed


@dataclass
class Bed:
    name: str
    solution: Path
    spec: Path
    rounds: list[Round] = field(default_factory=list)


def log(message: str) -> None:
    print(message, flush=True)


def run(command: list[str], cwd: Path, env: dict[str, str], accepted: tuple[int, ...]) -> tuple[float, str]:
    started = time.monotonic()
    completed = subprocess.run(command, cwd=cwd, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                               text=True, encoding="utf-8", errors="replace")
    elapsed = time.monotonic() - started
    if completed.returncode not in accepted:
        tail = "\n".join(completed.stderr.splitlines()[-30:])
        raise MeasurementError(f"{' '.join(command)} exited {completed.returncode}\n{tail}")
    return elapsed, completed.stderr


def parse_timings(stderr: str, command: list[str]) -> Timed:
    lines = stderr.splitlines()
    try:
        start = lines.index("timings (wall clock, seconds):")
    except ValueError as error:
        raise MeasurementError(f"{' '.join(command)} printed no timings block") from error

    timed = Timed(seconds=0.0)
    for line in lines[start + 1:]:
        if match := PHASE_ROW.match(line):
            timed.phases[match["name"]] = float(match["seconds"])
        elif match := CACHE_LINE.match(line):
            timed.outcome = match["outcome"]
            timed.projects = int(match["projects"])
    if timed.outcome is None:
        raise MeasurementError(f"{' '.join(command)} printed no cache line")
    return timed


class Runner:
    def __init__(self, cli: list[str], cache_root: Path) -> None:
        self.cli = cli
        self.cache_root = cache_root

    def env_for(self, bed: Bed) -> dict[str, str]:
        env = dict(os.environ)
        env["SLIDERULE_CACHE_DIR"] = str(self.cache_root / bed.name)
        env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        env["DOTNET_NOLOGO"] = "1"
        return env

    def check(self, bed: Bed, *extra: str) -> tuple[float, str, list[str]]:
        command = [*self.cli, "check", str(bed.solution), "--spec", str(bed.spec), *extra]
        elapsed, stderr = run(command, bed.solution.parent, self.env_for(bed), accepted=(0, 1))
        return elapsed, stderr, command

    def timed_check(self, bed: Bed, label: str, expected: str, *extra: str) -> Timed:
        elapsed, stderr, command = self.check(bed, *extra, "--timings")
        timed = parse_timings(stderr, command)
        if timed.outcome != expected:
            raise MeasurementError(f"{label} reported cache: {timed.outcome}, not {expected}")
        timed.seconds = elapsed
        return timed

    def discard(self, bed: Bed) -> None:
        self.timed_check(bed, "the discarded warm-up check", "disabled", "--no-cache")
        log(f"  [{bed.name}] warm-up check discarded")

    def build(self, bed: Bed) -> Timed:
        command = ["dotnet", "build", str(bed.solution), "--no-incremental", "--no-restore", "-nologo", "-v", "q"]
        elapsed, _ = run(command, bed.solution.parent, self.env_for(bed), accepted=(0,))
        self.discard(bed)
        return Timed(seconds=elapsed)

    def cold(self, bed: Bed) -> Timed:
        return self.timed_check(bed, "the cold check", "disabled", "--no-cache")

    def hit(self, bed: Bed) -> Timed:
        self.check(bed)
        return self.timed_check(bed, "the hit arm's check", "hit")

    def measure(self, bed: Bed, rounds: int) -> None:
        for index in range(rounds):
            order = ARMS[index % len(ARMS):] + ARMS[:index % len(ARMS)]
            results: dict[str, Timed] = {}
            for arm in order:
                results[arm] = getattr(self, arm)(bed)
                log(f"  [{bed.name}] round {index + 1}/{rounds} {arm}: {results[arm].seconds:.1f} s")
            bed.rounds.append(Round(**results))


def cell(values: list[float], spec: str, unit: str = "") -> str:
    median, low, high = statistics.median(values), min(values), max(values)
    return f"{median:{spec}}{unit} [{low:{spec}}–{high:{spec}}]"


def projects_of(bed: Bed) -> int:
    counts = {r.cold.projects for r in bed.rounds}
    if len(counts) != 1 or None in counts:
        raise MeasurementError(f"[{bed.name}] the cold checks disagree on the project count: {sorted(map(str, counts))}")
    return counts.pop()  # type: ignore[return-value]


def cpu_model() -> str:
    cpuinfo = Path("/proc/cpuinfo")
    if cpuinfo.exists():
        for line in cpuinfo.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.startswith("model name"):
                return line.split(":", 1)[1].strip()
    return platform.processor() or "not reported"


def provenance() -> list[str]:
    server = os.environ.get("GITHUB_SERVER_URL")
    repository = os.environ.get("GITHUB_REPOSITORY")
    run_id = os.environ.get("GITHUB_RUN_ID")
    link = f"{server}/{repository}/actions/runs/{run_id}" if server and repository and run_id else "not a GitHub run"
    image = os.environ.get("ImageOS")
    image_version = os.environ.get("ImageVersion")
    return [
        "| | |",
        "|---|---|",
        f"| Runner image | {f'{image} {image_version}' if image else 'not a GitHub runner'} |",
        f"| CPU model | {cpu_model()} |",
        f"| Logical cores | {os.cpu_count()} |",
        f"| Date (UTC) | {datetime.datetime.now(datetime.timezone.utc):%Y-%m-%d %H:%M} |",
        f"| Commit | {os.environ.get('GITHUB_SHA', 'not a GitHub run')} |",
        f"| Run | {link} |",
    ]


def report(beds: list[Bed], rounds: int) -> str:
    lines = [
        "## SlideRule performance as same-job ratios",
        "",
        f"Each ratio is computed per round from that round's arms, then summarised as the median of {rounds} "
        "rounds with the lowest and highest in brackets. Every term of a ratio was measured on this runner "
        "in this job; the seconds at the end are this run's alone.",
        "",
    ]
    for bed in beds:
        cold_over_hit = [r.cold.seconds / r.hit.seconds for r in bed.rounds]
        build_over_cold = [r.build.seconds / r.cold.seconds for r in bed.rounds]
        lines += [
            f"### {bed.name} ({projects_of(bed)} projects)",
            "",
            "| Ratio | Median [range] |",
            "|---|---|",
            f"| cold check ÷ cache hit | {cell(cold_over_hit, ".1f", "×")} |",
            f"| clean build ÷ cold check | {cell(build_over_cold, ".1f", "×")} |",
            "",
            "Share of the cold check's total by phase:",
            "",
            "| Phase | Median [range] |",
            "|---|---|",
        ]
        names = [name for name in bed.rounds[0].cold.phases if name != "total"]
        for name in names:
            shares = [r.cold.phases.get(name, 0.0) / r.cold.phases["total"] for r in bed.rounds]
            lines.append(f"| {name} | {cell(shares, ".0%")} |")
        lines.append("")

    lines += [
        "### This run's figures",
        "",
        "Seconds on this runner, median [range] over the rounds. They describe this run, not the tool.",
        "",
        "| Bed | Projects | Clean build | Cold check | Cache hit | Cold check per project |",
        "|---|---|---|---|---|---|",
    ]
    for bed in beds:
        projects = projects_of(bed)
        build = [r.build.seconds for r in bed.rounds]
        cold = [r.cold.seconds for r in bed.rounds]
        hit = [r.hit.seconds for r in bed.rounds]
        per_project = [seconds / projects for seconds in cold]
        lines.append(f"| {bed.name} | {projects} | {cell(build, ".1f")} | {cell(cold, ".1f")} | "
                     f"{cell(hit, ".1f")} | {cell(per_project, ".1f")} |")

    lines += ["", "### Provenance", "", *provenance(), ""]
    return "\n".join(lines)


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--cli", required=True, type=Path, help="the built sliderule-cli.dll, run with dotnet")
    parser.add_argument("--bed", action="append", nargs=3, metavar=("NAME", "SOLUTION", "SPEC"), required=True,
                        help="a bed: its name, its solution file and its built spec DLL or spec csproj")
    parser.add_argument("--rounds", type=int, default=3)
    parser.add_argument("--cache-root", type=Path,
                        help="where each bed's extraction cache goes (default: a new temporary directory)")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    # The report's ÷, × and – would reach a Windows console in its code page.
    sys.stdout.reconfigure(encoding="utf-8")
    args = parse_args(argv)
    cache_root = args.cache_root or Path(tempfile.mkdtemp(prefix="sliderule-perf-"))
    runner = Runner(["dotnet", str(args.cli.resolve())], cache_root)
    beds = [Bed(name, Path(solution).resolve(), Path(spec).resolve()) for name, solution, spec in args.bed]

    try:
        for bed in beds:
            log(f"[{bed.name}] {args.rounds} rounds over {bed.solution}")
            runner.measure(bed, args.rounds)
        text = report(beds, args.rounds)
    except MeasurementError as error:
        print(f"::error::{error}", file=sys.stderr, flush=True)
        return 1

    print(text, flush=True)
    if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(text + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
