"""Dataset quality regression checks; run with Python's built-in unittest."""

import copy
import json
import tempfile
import unittest
from pathlib import Path

import prepare_tickets as dataset


class DatasetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = dataset.DEFAULT_DATA / "source" / "scenarios.jsonl"
        cls.scenarios = dataset.load_scenarios(cls.source)
        cls.assignment = dataset.split_scenarios(cls.scenarios)
        cls.rows = {split: dataset.rows_for(group) for split, group in cls.assignment.items()}

    def changed_source(self, mutate, message):
        scenarios = copy.deepcopy(self.scenarios)
        mutate(scenarios)
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "scenarios.jsonl"
            path.write_text("\n".join(json.dumps(scenario) for scenario in scenarios) + "\n", encoding="utf-8")
            with self.assertRaisesRegex(dataset.DatasetError, message):
                dataset.load_scenarios(path)

    def test_source_labels_and_counts(self):
        self.assertEqual(120, len(self.scenarios))
        self.assertEqual(set(dataset.CATEGORIES), {scenario["category"] for scenario in self.scenarios})
        self.assertEqual(set(dataset.PRIORITIES), {scenario["priority"] for scenario in self.scenarios})
        self.assertEqual(set(dataset.TEAMS), {scenario["team"] for scenario in self.scenarios})

    def test_split_is_reproducible(self):
        self.assertEqual(self.assignment, dataset.split_scenarios(list(reversed(self.scenarios))))

    def test_exact_group_and_row_quotas(self):
        self.assertEqual([84, 18, 18], [len(self.assignment[split]) for split in dataset.SPLITS])
        self.assertEqual([168, 36, 36], [len(self.rows[split]) for split in dataset.SPLITS])
        for split in dataset.SPLITS:
            self.assertEqual({2}, set(dataset.Counter(row["ScenarioId"] for row in self.rows[split]).values()))

    def test_priority_and_team_have_independent_group_support(self):
        for split in dataset.SPLITS:
            for field, allowed in (("priority", dataset.PRIORITIES), ("team", dataset.TEAMS)):
                counts = dataset.Counter(scenario[field] for scenario in self.assignment[split])
                for label in allowed:
                    self.assertGreaterEqual(counts[label], 2, f"{split}/{label}")

    def test_text_features_exclude_all_labels_and_metadata(self):
        manifest = json.loads((dataset.DEFAULT_DATA / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(["Title", "Description"], manifest["features"])
        self.assertTrue(set(dataset.LABELS).isdisjoint(manifest["features"]))
        self.assertTrue({"TicketId", "ScenarioId"}.isdisjoint(manifest["features"]))

    def test_csv_round_trip_preserves_commas_quotes_and_unicode(self):
        row = dict(self.rows["training"][0], Title='PDF export says "cannot open", again', Description='The user’s file contains commas, quotes "and" Unicode; the alternate view still works.')
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "roundtrip.csv"
            path.write_bytes(dataset.csv_bytes([row]))
            self.assertEqual([row], dataset.read_csv(path))

    def test_dataset_passes_overlap_and_distribution_checks(self):
        report = dataset.validate_rows(self.rows)
        self.assertEqual("passed", report["status"])
        self.assertEqual(240, report["rows"])
        self.assertLess(report["strongest_cross_split_text_overlaps"][0]["bigram_jaccard"], 0.65)

    def test_duplicate_ticket_is_rejected(self):
        rows = copy.deepcopy(self.rows)
        rows["training"].append(dict(rows["training"][0]))
        with self.assertRaisesRegex(dataset.DatasetError, "Duplicate TicketId"):
            dataset.validate_rows(rows)

    def test_group_cannot_cross_split_boundary(self):
        rows = copy.deepcopy(self.rows)
        rows["validation"][0]["ScenarioId"] = rows["training"][0]["ScenarioId"]
        with self.assertRaisesRegex(dataset.DatasetError, "Scenario leakage"):
            dataset.validate_rows(rows)

    def test_conflicting_group_annotations_are_rejected(self):
        rows = copy.deepcopy(self.rows)
        first = rows["training"][0]
        other = next(row for row in rows["training"][1:] if row["ScenarioId"] == first["ScenarioId"])
        other["Priority"] = "High" if first["Priority"] != "High" else "Medium"
        with self.assertRaisesRegex(dataset.DatasetError, "Contradictory annotations"):
            dataset.validate_rows(rows)

    def test_normalized_duplicate_text_is_rejected(self):
        rows = copy.deepcopy(self.rows)
        rows["validation"][0].update(Title=rows["training"][0]["Title"].upper(), Description=rows["training"][0]["Description"])
        with self.assertRaisesRegex(dataset.DatasetError, "Normalized duplicate text"):
            dataset.validate_rows(rows)

    def test_lexically_similar_cross_split_copy_is_rejected(self):
        rows = copy.deepcopy(self.rows)
        rows["validation"][0].update(Title=rows["training"][0]["Title"] + " today", Description=rows["training"][0]["Description"] + " Please investigate.")
        with self.assertRaisesRegex(dataset.DatasetError, "Cross-split near duplicate"):
            dataset.validate_rows(rows)

    def test_missing_class_is_rejected(self):
        rows = copy.deepcopy(self.rows)
        rows["test"] = [row for row in rows["test"] if row["Category"] != "Other"]
        with self.assertRaisesRegex(dataset.DatasetError, "Missing Category class"):
            dataset.validate_rows(rows)

    def test_unknown_source_category_is_rejected(self):
        self.changed_source(lambda scenarios: scenarios[0].update(category="Unknown"), "Unknown category")

    def test_unknown_source_priority_is_rejected(self):
        self.changed_source(lambda scenarios: scenarios[0].update(priority="Urgent"), "Unknown priority")

    def test_unknown_source_team_is_rejected(self):
        self.changed_source(lambda scenarios: scenarios[0].update(team="Backend Team"), "Unknown team")

    def test_duplicate_scenario_is_rejected(self):
        self.changed_source(lambda scenarios: scenarios.append(copy.deepcopy(scenarios[0])), "Duplicate scenario")

    def test_incomplete_source_schema_is_rejected(self):
        self.changed_source(lambda scenarios: scenarios[0].pop("rationale"), "Invalid source schema")

    def test_empty_description_is_rejected(self):
        self.changed_source(lambda scenarios: scenarios[0]["variants"][0].update(description=" "), "Invalid description")

    def test_explicit_label_in_text_is_rejected(self):
        self.changed_source(lambda scenarios: scenarios[0]["variants"][0].update(title="Priority: High incident"), "Explicit label echo")

    def test_formula_prefix_is_rejected(self):
        self.changed_source(lambda scenarios: scenarios[0]["variants"][0].update(title="=Please inspect this issue"), "Spreadsheet formula prefix")

    def test_csv_rejects_unexpected_columns_empty_values_and_invalid_labels(self):
        good = dataset.csv_bytes([self.rows["training"][0]])
        cases = [good.replace(b"TicketId,", b"UnexpectedId,", 1), dataset.csv_bytes([dict(self.rows["training"][0], Title="")]),
                 dataset.csv_bytes([dict(self.rows["training"][0], Priority="Urgent")]), dataset.csv_bytes([dict(self.rows["training"][0], TicketId="not-a-uuid")])]
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "invalid.csv"
            for content in cases:
                with self.subTest(content=content[:50]):
                    path.write_bytes(content)
                    with self.assertRaises(dataset.DatasetError):
                        dataset.read_csv(path)

    def test_committed_artifacts_match_source_generator_and_hashes(self):
        self.assertEqual("passed", dataset.execute(dataset.DEFAULT_DATA)["status"])

    def test_build_refuses_to_overwrite_a_frozen_artifact(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            (folder / "source").mkdir()
            (folder / "source" / "scenarios.jsonl").write_bytes(self.source.read_bytes())
            (folder / "training.csv").write_bytes(b"tampered frozen corpus\n")
            with self.assertRaisesRegex(dataset.DatasetError, "Frozen artifact differs"):
                dataset.execute(folder, build=True)
            self.assertEqual(b"tampered frozen corpus\n", (folder / "training.csv").read_bytes())
            self.assertFalse((folder / "test.csv").exists())


if __name__ == "__main__":
    unittest.main()
