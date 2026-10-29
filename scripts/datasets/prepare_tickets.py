"""Prepare/validate the synthetic v1 corpus; Python standard library, no ML training."""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import random
import re
import sys
import unicodedata
import uuid
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_DATA = ROOT / "data" / "tickets" / "v1"
SEED = 20261023
NAMESPACE = uuid.UUID("386d4490-1ed7-51ef-80cd-e2d68b201d06")
SPLITS = ("training", "validation", "test")
CATEGORIES = ("Hardware", "Software", "Network", "Database", "Security", "Billing", "Account", "Application", "Other")
PRIORITIES = ("Low", "Medium", "High", "Critical")
TEAMS = ("Support", "Network", "Database", "Security", "Backend", "Infrastructure")
LABELS = {"Category": CATEGORIES, "Priority": PRIORITIES, "RecommendedTeam": TEAMS}
COLUMNS = ("TicketId", "ScenarioId", "Title", "Description", "Category", "Priority", "RecommendedTeam")
# Group quotas: 84/18/18, with every category in each split.
QUOTAS = {
    "Hardware": (10, 2, 2), "Software": (12, 2, 2), "Network": (12, 3, 3),
    "Database": (8, 2, 2), "Security": (8, 2, 2), "Billing": (8, 1, 1),
    "Account": (12, 3, 3), "Application": (10, 2, 2), "Other": (4, 1, 1),
}
SOURCE_KEYS = {"scenario_id", "category", "priority", "team", "rationale", "variants"}
LABEL_ECHO = re.compile(r"\b(?:priority|severity|category|assigned team|recommended team)\s*[:=]|\b(?:P[0-4]|SEV[0-4])\b", re.I)


class DatasetError(ValueError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise DatasetError(message)


def normalized(text: str) -> str:
    return " ".join(re.findall(r"[a-z0-9]+", unicodedata.normalize("NFKC", text).casefold()))


def sha256(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def json_bytes(value: object) -> bytes:
    return (json.dumps(value, indent=2, ensure_ascii=False, sort_keys=True) + "\n").encode("utf-8")


def load_scenarios(path: Path) -> list[dict]:
    scenarios = []
    seen = set()
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        require(bool(line.strip()), f"Empty source line {line_number}")
        try:
            scenario = json.loads(line)
        except json.JSONDecodeError as error:
            raise DatasetError(f"Invalid source JSON on line {line_number}") from error
        require(isinstance(scenario, dict) and set(scenario) == SOURCE_KEYS, f"Invalid source schema on line {line_number}")
        sid = scenario["scenario_id"]
        require(isinstance(sid, str) and re.fullmatch(r"[a-z][a-z0-9-]{2,79}", sid) is not None, "Invalid scenario ID")
        require(sid not in seen, f"Duplicate scenario {sid}")
        seen.add(sid)
        for field, allowed in (("category", CATEGORIES), ("priority", PRIORITIES), ("team", TEAMS)):
            require(isinstance(scenario[field], str) and scenario[field] in allowed, f"Unknown {field} in {sid}")
        require(isinstance(scenario["rationale"], str) and len(scenario["rationale"].split()) >= 5, f"Missing annotation rationale: {sid}")
        variants = scenario["variants"]
        require(isinstance(variants, list) and len(variants) == 2, f"Expected two authored variants: {sid}")
        for variant in variants:
            require(isinstance(variant, dict) and set(variant) == {"title", "description"}, f"Invalid variant: {sid}")
            for field, minimum, maximum in (("title", 3, 160), ("description", 12, 8000)):
                value = variant[field]
                require(isinstance(value, str) and value == value.strip() and minimum <= len(value.split()) and len(value) <= maximum, f"Invalid {field}: {sid}")
                require(not any(ord(char) < 32 for char in value), f"Control character in {sid}")
                require(not value.startswith(("=", "+", "-", "@")), f"Spreadsheet formula prefix: {sid}")
                require(not LABEL_ECHO.search(value), f"Explicit label echo: {sid}")
        scenarios.append(scenario)
    counts = Counter(scenario["category"] for scenario in scenarios)
    require(counts == Counter({category: sum(quota) for category, quota in QUOTAS.items()}), "Source category counts do not match the frozen v1 design")
    return sorted(scenarios, key=lambda scenario: scenario["scenario_id"])


def split_scenarios(scenarios: list[dict]) -> dict[str, list[dict]]:
    """Fixed-seed category quotas, minimize label-distribution drift, never model scores."""
    scenarios = sorted(scenarios, key=lambda scenario: scenario["scenario_id"])
    rng = random.Random(SEED)
    totals = {field: Counter(scenario[field] for scenario in scenarios) for field in ("priority", "team")}
    best = None
    best_score = float("inf")
    for _ in range(4000):
        candidate = {split: [] for split in SPLITS}
        for category in CATEGORIES:
            group = [scenario for scenario in scenarios if scenario["category"] == category]
            rng.shuffle(group)
            offset = 0
            for split, count in zip(SPLITS, QUOTAS[category]):
                candidate[split].extend(group[offset:offset + count])
                offset += count
        score = 0.0
        for split, proportion in zip(SPLITS, (0.7, 0.15, 0.15)):
            for field, total in totals.items():
                counts = Counter(scenario[field] for scenario in candidate[split])
                for label, count in total.items():
                    expected = count * proportion
                    score += (counts[label] - expected) ** 2 / expected
                    if counts[label] < 2:
                        score += 10000
        if score < best_score:
            best_score = score
            best = candidate
    require(best is not None and best_score < 10000, "Cannot preserve priority/team coverage with these group quotas")
    return {split: sorted(group, key=lambda scenario: scenario["scenario_id"]) for split, group in best.items()}


def rows_for(scenarios: list[dict]) -> list[dict[str, str]]:
    rows = []
    for scenario in scenarios:
        scenario_uuid = str(uuid.uuid5(NAMESPACE, scenario["scenario_id"]))
        for index, variant in enumerate(scenario["variants"]):
            rows.append(dict(zip(COLUMNS, (
                str(uuid.uuid5(NAMESPACE, f"{scenario['scenario_id']}:{index}")), scenario_uuid,
                variant["title"], variant["description"], scenario["category"], scenario["priority"], scenario["team"],
            ))))
    # Neither category ordering nor paraphrase adjacency should convey a training hint.
    random.Random(SEED).shuffle(rows)
    return rows


def csv_bytes(rows: list[dict[str, str]]) -> bytes:
    stream = io.StringIO(newline="")
    writer = csv.DictWriter(stream, fieldnames=COLUMNS, lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
    return stream.getvalue().encode("utf-8")


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8", newline="") as stream:
        reader = csv.DictReader(stream, strict=True)
        require(reader.fieldnames == list(COLUMNS), f"Unexpected columns: {path.name}")
        rows = list(reader)
    require(bool(rows), f"Empty split: {path.name}")
    for row in rows:
        require(set(row) == set(COLUMNS) and all(isinstance(value, str) and value.strip() for value in row.values()), f"Missing/extra/empty CSV value: {path.name}")
        for column, allowed in LABELS.items():
            require(row[column] in allowed, f"Unknown {column}: {path.name}")
        for column in ("TicketId", "ScenarioId"):
            try:
                require(uuid.UUID(row[column]).version == 5, f"Invalid deterministic UUID: {path.name}")
            except ValueError as error:
                raise DatasetError(f"Invalid UUID: {path.name}") from error
    return rows


def validate_rows(splits: dict[str, list[dict[str, str]]]) -> dict:
    ids, texts, groups, titles = set(), {}, {}, {}
    all_rows = []
    distributions = {}
    group_labels = {}
    for split in SPLITS:
        rows = splits[split]
        distributions[split] = {"rows": len(rows), "scenario_groups": len({row["ScenarioId"] for row in rows})}
        for column, allowed in LABELS.items():
            counts = Counter(row[column] for row in rows)
            require(set(counts) == set(allowed), f"Missing {column} class in {split}")
            distributions[split][column] = {label: counts[label] for label in allowed}
        for row in rows:
            tid, sid = row["TicketId"], row["ScenarioId"]
            require(tid not in ids, f"Duplicate TicketId: {tid}")
            ids.add(tid)
            require(sid not in groups or groups[sid] == split, f"Scenario leakage across splits: {sid}")
            groups[sid] = split
            labels = tuple(row[column] for column in LABELS)
            require(sid not in group_labels or group_labels[sid] == labels, f"Contradictory annotations: {sid}")
            group_labels[sid] = labels
            text = normalized(row["Title"] + " " + row["Description"])
            title = normalized(row["Title"])
            require(text not in texts, f"Normalized duplicate text: {tid}")
            require(title not in titles or titles[title] == sid, f"Duplicate title across scenario groups: {tid}")
            texts[text], titles[title] = tid, sid
            tokens = text.split()
            bigrams = set(zip(tokens, tokens[1:]))
            all_rows.append((split, row, bigrams))
    # Descriptive overlap screen, not a semantic duplicate detector or learned feature transform.
    strongest = []
    for index, (left_split, left, left_terms) in enumerate(all_rows):
        for right_split, right, right_terms in all_rows[index + 1:]:
            if left_split == right_split:
                continue
            similarity = len(left_terms & right_terms) / len(left_terms | right_terms)
            require(similarity < 0.65, f"Cross-split near duplicate: {left['TicketId']} / {right['TicketId']}")
            strongest.append({"left": left["TicketId"], "right": right["TicketId"], "bigram_jaccard": round(similarity, 4)})
    strongest.sort(key=lambda pair: (-pair["bigram_jaccard"], pair["left"], pair["right"]))
    cross_tabs = {}
    for left, right in (("Category", "RecommendedTeam"), ("Category", "Priority"), ("Priority", "RecommendedTeam")):
        cross_tabs[f"{left}_by_{right}"] = {
            label: dict(sorted(Counter(row[right] for _, row, _ in all_rows if row[left] == label).items()))
            for label in LABELS[left]
        }
    return {
        "status": "passed", "rows": len(all_rows), "scenario_groups": len(groups),
        "checks": ["strict_csv_schema", "required_fields", "allowed_labels", "unique_ids", "consistent_group_labels",
                   "all_classes_in_all_splits", "disjoint_scenario_groups", "no_normalized_duplicate_text",
                   "no_cross_group_duplicate_titles", "cross_split_bigram_jaccard_below_0.65"],
        "distributions": distributions, "joint_distributions": cross_tabs,
        "strongest_cross_split_text_overlaps": strongest[:10],
        "limitations": ["Synthetic English corpus, not observed production traffic.",
                        "Annotation rationales are authored, not independently human-reviewed.",
                        "Lexical overlap checks cannot rule out semantic leakage or shared author style.",
                        "Two paraphrases per scenario are correlated, not independent incidents.",
                        "Held-out labels have small independent-group support; no reliable performance claim."]
    }


def expected_artifacts(data_dir: Path) -> dict[str, bytes]:
    source = data_dir / "source" / "scenarios.jsonl"
    scenarios = load_scenarios(source)
    assignments = split_scenarios(scenarios)
    splits = {split: rows_for(group) for split, group in assignments.items()}
    report = validate_rows(splits)
    artifacts = {f"{split}.csv": csv_bytes(rows) for split, rows in splits.items()}
    artifacts["validation-report.json"] = json_bytes(report)
    manifest = {
        "dataset_version": "1.0.0", "language": "en", "provenance": "synthetic_assistant_authored",
        "human_annotation_review": "pending", "seed": SEED,
        "split_method": "fixed category quotas; grouped multi-label distribution search (4000 fixed-seed candidates)",
        "split_ratios": {"training": 0.7, "validation": 0.15, "test": 0.15},
        "features": ["Title", "Description"], "labels": {column: list(values) for column, values in LABELS.items()},
        "excluded_from_features": ["TicketId", "ScenarioId", "Category", "Priority", "RecommendedTeam", "rationale"],
        "columns": list(COLUMNS), "rows": report["rows"], "scenario_groups": report["scenario_groups"],
        "source_sha256": sha256(source.read_bytes()),
        "generator_sha256": sha256(Path(__file__).read_bytes()),
        "artifacts_sha256": {name: sha256(content) for name, content in artifacts.items()},
        "test_policy": "Frozen group assignment. Do not tune features, thresholds or models on test results. New corpus changes require a new version.",
    }
    artifacts["manifest.json"] = json_bytes(manifest)
    return artifacts


def execute(data_dir: Path, build: bool = False) -> dict:
    artifacts = expected_artifacts(data_dir)
    # Check all existing files before writing any: a rebuild must not silently replace a frozen split.
    for name, content in artifacts.items():
        path = data_dir / name
        require(not path.exists() or path.read_bytes() == content, f"Frozen artifact differs: {name}. Review changes and create a new dataset version.")
    if build:
        for name, content in artifacts.items():
            (data_dir / name).write_bytes(content)
    for split in SPLITS:
        require((data_dir / f"{split}.csv").is_file(), f"Missing {split}.csv; run --build first")
    report = validate_rows({split: read_csv(data_dir / f"{split}.csv") for split in SPLITS})
    for name, content in artifacts.items():
        require((data_dir / name).is_file() and (data_dir / name).read_bytes() == content, f"Artifact/hash/split mismatch: {name}")
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", type=Path, default=DEFAULT_DATA)
    parser.add_argument("--build", action="store_true", help="Create missing deterministic artifacts; never replace differing frozen artifacts")
    args = parser.parse_args()
    try:
        report = execute(args.data_dir.resolve(), args.build)
    except (DatasetError, OSError, csv.Error) as error:
        print(f"Dataset validation FAILED: {error}", file=sys.stderr)
        return 1
    print(f"Dataset validation PASSED: {report['rows']} tickets / {report['scenario_groups']} scenario groups")
    for split in SPLITS:
        part = report["distributions"][split]
        print(f"  {split}: {part['rows']} tickets / {part['scenario_groups']} groups")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
