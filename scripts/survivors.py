"""Prints the mutation score and each surviving mutant from a Stryker JSON report: python3 survivors.py mutation-report.json"""
import json
import sys

counts = {}
survivors = []
for path, file in json.load(open(sys.argv[1]))["files"].items():
    name = path[path.find("src/"):]
    for mutant in file["mutants"]:
        status = mutant["status"]
        counts[status] = counts.get(status, 0) + 1
        if status in ("Survived", "NoCoverage"):
            replacement = " ".join(mutant.get("replacement", "").split())[:100]
            survivors.append((name, mutant["location"]["start"]["line"], status, mutant["mutatorName"], replacement))

killed = counts.get("Killed", 0) + counts.get("Timeout", 0)
survived = counts.get("Survived", 0)
uncovered = counts.get("NoCoverage", 0)
tested = killed + survived + uncovered
score = 100 * killed / tested if tested else 100
print(f"Mutation score {score:.1f}%: {killed} killed, {survived} survived, {uncovered} not covered")
for name, line, status, mutator, replacement in sorted(survivors):
    print(f"{name}:{line} {status} {mutator}: {replacement}")
