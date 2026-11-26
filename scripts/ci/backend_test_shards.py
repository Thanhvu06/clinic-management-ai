#!/usr/bin/env python3
"""Backend test sharding for ClinicCare CI.

Shards 1..N-1 select test classes by class-name prefix. The last shard runs
everything the other shards do not select, so a newly added test class is
never dropped from CI. Prefixes are balanced on measured CI durations.

Commands:
  filter <shard>                     print the dotnet test --filter for a shard (1-based)
  verify-partition <solution>        list tests per shard and fail unless the shards
                                     partition the full solution; writes total= to GITHUB_OUTPUT
  verify-results <expected> <dir>    fail unless the TRX files under <dir> ran exactly
                                     <expected> tests with no test in two shards
"""

import collections
import glob
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

NAMESPACE = "ClinicManagement.IntegrationTests."

SHARD_PREFIXES = [
    [
        "DemoSeed24",
        "DemoPolish",
        "DemoSundayClosed",
        "DemoConsistency",
    ],
    [
        "Ai",
        "B",
        "Development",
        "Prescription",
        "Pending",
        "Diagnostic",
        "H",
    ],
    [
        "ClinicOperatingHours",
        "OperatingHours",
        "DemoLogic",
        "DemoData",
        "DemoBlockers",
        "DemoRun",
        "DemoRound",
        "Daytime",
        "Realistic",
        "SeedData",
    ],
]

SHARD_COUNT = len(SHARD_PREFIXES) + 1


def term(prefix, operator):
    return f"FullyQualifiedName{operator}{NAMESPACE}{prefix}"


def shard_filter(shard):
    if not 1 <= shard <= SHARD_COUNT:
        raise SystemExit(f"shard must be between 1 and {SHARD_COUNT}, got {shard}")
    if shard < SHARD_COUNT:
        return "|".join(term(p, "~") for p in SHARD_PREFIXES[shard - 1])
    return "&".join(term(p, "!~") for prefixes in SHARD_PREFIXES for p in prefixes)


def list_tests(solution, test_filter=None):
    command = ["dotnet", "test", solution, "--no-build", "--configuration", "Release", "--list-tests"]
    if test_filter:
        command += ["--filter", test_filter]
    output = subprocess.run(command, check=True, capture_output=True, text=True, encoding="utf-8").stdout
    names = []
    listing = False
    for line in output.splitlines():
        if line.startswith("The following Tests are available:"):
            listing = True
        elif listing and line.startswith("    "):
            names.append(line.strip())
        else:
            listing = False
    return names


def write_output(key, value):
    path = os.environ.get("GITHUB_OUTPUT")
    if path:
        with open(path, "a", encoding="utf-8") as handle:
            handle.write(f"{key}={value}\n")


def verify_partition(solution):
    full = collections.Counter(list_tests(solution))
    total = sum(full.values())
    if total == 0:
        raise SystemExit("::error title=Backend shards::Solution lists no tests")
    combined = collections.Counter()
    owner = {}
    errors = []
    for shard in range(1, SHARD_COUNT + 1):
        names = collections.Counter(list_tests(solution, shard_filter(shard)))
        print(f"shard {shard}/{SHARD_COUNT}: {sum(names.values())} tests")
        if not names:
            errors.append(f"shard {shard} selects no tests")
        for name in names:
            if name in owner:
                errors.append(f"{name} is selected by shard {owner[name]} and shard {shard}")
            owner[name] = shard
        combined += names
    missing = full - combined
    extra = combined - full
    errors += [f"not selected by any shard: {name}" for name in missing]
    errors += [f"selected more often than it exists: {name}" for name in extra]
    print(f"solution: {total} tests; shards combined: {sum(combined.values())}")
    if errors:
        for error in errors[:50]:
            print(f"::error title=Backend shard partition::{error}")
        raise SystemExit(1)
    write_output("total", total)


def verify_results(expected, directory):
    namespace = {"trx": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    shard_dirs = sorted(d for d in glob.glob(os.path.join(directory, "*")) if os.path.isdir(d))
    if len(shard_dirs) != SHARD_COUNT:
        raise SystemExit(
            f"::error title=Backend test count::Expected TRX results from {SHARD_COUNT} shards, found {len(shard_dirs)}"
        )
    owner = {}
    duplicates = []
    executed = 0
    for shard_dir in shard_dirs:
        shard_executed = 0
        trx_files = glob.glob(os.path.join(shard_dir, "**", "*.trx"), recursive=True)
        if not trx_files:
            raise SystemExit(f"::error title=Backend test count::No TRX file in {shard_dir}")
        for path in trx_files:
            root = ET.parse(path).getroot()
            counters = root.find(".//trx:ResultSummary/trx:Counters", namespace)
            shard_executed += int(counters.attrib["executed"])
            for result in root.findall(".//trx:UnitTestResult", namespace):
                name = result.attrib["testName"]
                if name in owner and owner[name] != shard_dir:
                    duplicates.append(name)
                owner.setdefault(name, shard_dir)
        print(f"{os.path.basename(shard_dir)}: {shard_executed} tests executed")
        executed += shard_executed
    print(f"executed across shards: {executed}; expected from solution: {expected}")
    for name in duplicates[:50]:
        print(f"::error title=Backend test ran in two shards::{name}")
    if executed != expected:
        print(f"::error title=Backend test count::Shards executed {executed} tests, solution has {expected}")
    if duplicates or executed != expected:
        raise SystemExit(1)


def main(argv):
    if len(argv) >= 2 and argv[0] == "filter":
        print(shard_filter(int(argv[1])))
    elif len(argv) >= 2 and argv[0] == "verify-partition":
        verify_partition(argv[1])
    elif len(argv) >= 3 and argv[0] == "verify-results":
        verify_results(int(argv[1]), argv[2])
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
