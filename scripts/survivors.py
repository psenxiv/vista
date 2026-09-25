"""Prints the mutation score and each surviving mutant from a Stryker JSON report.

Given a commit, lists only survivors on lines changed since it: python3 survivors.py mutation-report.json [commit]
"""
import json
import re
import subprocess
import sys


def changed_lines(commit, path):
    """The line numbers in the working tree's copy of path that changed since commit."""
    diff = subprocess.run(["git", "diff", "-U0", commit, "--", path], capture_output=True, text=True, check=True).stdout
    lines = set()
    for match in re.finditer(r"^@@ -\S+ \+(\d+)(?:,(\d+))? @@", diff, re.M):
        start, count = int(match.group(1)), int(match.group(2) or 1)
        lines.update(range(start, start + count))
    return lines


commit = sys.argv[2] if len(sys.argv) > 2 else None
root = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True).stdout.strip()
counts = {}
survivors = []
changed = {}
for path, file in json.load(open(sys.argv[1]))["files"].items():
    name = path[path.find("src/"):]
    for mutant in file["mutants"]:
        status = mutant["status"]
        counts[status] = counts.get(status, 0) + 1
        if status not in ("Survived", "NoCoverage"):
            continue
        line = mutant["location"]["start"]["line"]
        if commit:
            if name not in changed:
                changed[name] = changed_lines(commit, f"{root}/{name}")
            if line not in changed[name]:
                continue
        replacement = " ".join(mutant.get("replacement", "").split())[:100]
        survivors.append((name, line, status, mutant["mutatorName"], replacement))

killed = counts.get("Killed", 0) + counts.get("Timeout", 0)
survived = counts.get("Survived", 0)
uncovered = counts.get("NoCoverage", 0)
tested = killed + survived + uncovered
if not tested:
    sys.exit("Stryker tested no mutants, so there's no score")
score = 100 * killed / tested
print(f"Mutation score {score:.1f}%: {killed} killed, {survived} survived, {uncovered} not covered")
if commit:
    print(f"{len(survivors)} survivors on lines changed since {commit[:7]}:")
for name, line, status, mutator, replacement in sorted(survivors):
    print(f"{name}:{line} {status} {mutator}: {replacement}")
