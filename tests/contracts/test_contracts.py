"""Validate published contracts; these tests do not implement the future CLI."""

import copy
import json
from pathlib import Path
import unittest

from jsonschema import Draft202012Validator


CONTRACTS = Path(__file__).resolve().parents[2] / "contracts"


def read_json(relative_path):
    return json.loads((CONTRACTS / relative_path).read_text(encoding="utf-8"))


class ManifestContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.schema = read_json("openbase.schema.json")
        cls.validator = Draft202012Validator(cls.schema)
        cls.example = read_json("examples/postgres.openbase.json")

    def assert_invalid_change(self, path, values):
        for value in values:
            with self.subTest(path=path, value=value):
                instance = copy.deepcopy(self.example)
                target = instance
                for segment in path[:-1]:
                    target = target[segment]
                target[path[-1]] = value
                self.assertFalse(self.validator.is_valid(instance))

    def test_schema_is_valid(self):
        Draft202012Validator.check_schema(self.schema)

    def test_three_provider_examples(self):
        for database in ("postgres", "sqlserver", "oracle"):
            with self.subTest(database=database):
                instance = read_json(f"examples/{database}.openbase.json")
                self.validator.validate(instance)
                self.assertEqual(instance["database"], database)
                self.assertEqual(len(set(instance["projects"].values())), 4)

    def test_required_fields(self):
        for field in self.schema["required"]:
            with self.subTest(field=field):
                instance = copy.deepcopy(self.example)
                del instance[field]
                self.assertFalse(self.validator.is_valid(instance))

    def test_unknown_versions_and_layouts(self):
        self.assert_invalid_change(["schemaVersion"], [1, 3, "2", None, True])
        self.assert_invalid_change(["template"], ["openbasepgsql", "", None])
        self.assert_invalid_change(["architecture"], ["legacy", "", None])

    def test_database_is_canonical(self):
        self.assert_invalid_change(
            ["database"], ["pgsql", "postgresql", "POSTGRES", "sqlite", "", None, 0]
        )

    def test_versions_are_separate_and_optional_cli_is_allowed(self):
        instance = copy.deepcopy(self.example)
        instance["templateVersion"] = "11.0.1"
        instance["cliVersion"] = "12.1.0-rc.2+build.45"
        self.validator.validate(instance)

    def test_invalid_versions(self):
        invalid = ["", "latest", "1.2", "v1.2.3", "01.2.3", "1.2.3-01", "1.2.3\n", None]
        self.assert_invalid_change(["templateVersion"], invalid)
        self.assert_invalid_change(["cliVersion"], invalid)

    def test_namespace_and_context_lexical_form(self):
        invalid = ["", "My App", "1App", "A..B", "@namespace", "A/B", "MinhaApi\n", None]
        self.assert_invalid_change(["rootNamespace"], invalid)
        self.assert_invalid_change(["persistence", "dbContext"], invalid)
        instance = copy.deepcopy(self.example)
        instance["rootNamespace"] = "Empresa._MinhaApi2"
        self.validator.validate(instance)

    def test_unsafe_paths_rejected_in_every_path_field(self):
        prefixes = ["/", "../", "./", "src/../", "src/./", "src//", "C:/", "C:", "\\\\server\\", "src\\", "src/\x00", "src/\x1f", "src/\x7f"]
        fields = [(["solution"], "MinhaApi.sln")]
        fields.extend((["projects", key], "Example.csproj") for key in self.example["projects"])
        fields.append((["persistence", "migrationsPath"], "Migrations"))
        for path, filename in fields:
            self.assert_invalid_change(path, [prefix + filename for prefix in prefixes] + [filename + "\n"])

    def test_empty_and_parent_migration_paths_rejected(self):
        self.assert_invalid_change(["persistence", "migrationsPath"], ["", ".", "..", "src/..", "src/.", None])

    def test_spaces_in_paths_and_both_solution_formats(self):
        for extension in ("sln", "slnx"):
            with self.subTest(extension=extension):
                instance = copy.deepcopy(self.example)
                instance["solution"] = f"My Project/MinhaApi.{extension}"
                instance["projects"]["api"] = "My Project/Api/Api.csproj"
                instance["persistence"]["migrationsPath"] = "My Project/Infra/Migrations"
                self.validator.validate(instance)

    def test_file_extensions(self):
        self.assert_invalid_change(["solution"], ["App.csproj", "App.sln.old", "App.sln/", "App"])
        for key in self.example["projects"]:
            self.assert_invalid_change(["projects", key], ["App.sln", "App.csproj.old", "App.csproj/", "App"])

    def test_unknown_properties_and_credentials_rejected(self):
        for path, key in (([], "password"), ([], "connectionString"), (["persistence"], "connectionString"), (["projects"], "extra")):
            with self.subTest(path=path, key=key):
                instance = copy.deepcopy(self.example)
                target = instance
                for segment in path:
                    target = target[segment]
                target[key] = "unexpected"
                self.assertFalse(self.validator.is_valid(instance))

    def test_connection_string_name_is_a_key(self):
        self.assert_invalid_change(["persistence", "connectionStringName"], ["Host=localhost;Password=example", "", "Default\n", None])

    def test_legacy_fixtures_are_not_silently_accepted_as_v2(self):
        for path in sorted((CONTRACTS / "fixtures/legacy").glob("*.json")):
            with self.subTest(path=path.name):
                instance = json.loads(path.read_text(encoding="utf-8"))
                self.assertFalse(self.validator.is_valid(instance))
                self.assertEqual(set(instance), {"createdBy", "template", "version", "createdAt"})


class ResponseContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.schema = read_json("cli-response.schema.json")
        cls.validator = Draft202012Validator(cls.schema)

    def test_schema_is_valid(self):
        Draft202012Validator.check_schema(self.schema)

    def test_all_response_examples(self):
        paths = sorted((CONTRACTS / "examples").glob("*.json"))
        for path in paths:
            if path.name.endswith(".openbase.json"):
                continue
            with self.subTest(path=path.name):
                self.validator.validate(json.loads(path.read_text(encoding="utf-8")))

    def test_success_and_failure_cannot_contradict_payload(self):
        for filename, changes in (
            ("new-success.json", {"ok": False}),
            ("new-success.json", {"data": None}),
            ("new-success.json", {"error": {"code": "ERROR", "message": "Failure"}}),
            ("new-conflict.json", {"ok": True}),
            ("new-conflict.json", {"error": None}),
            ("new-conflict.json", {"data": {}}),
        ):
            with self.subTest(filename=filename, changes=changes):
                instance = read_json(f"examples/{filename}")
                instance.update(changes)
                self.assertFalse(self.validator.is_valid(instance))

    def test_required_fields(self):
        for field in self.schema["required"]:
            with self.subTest(field=field):
                instance = read_json("examples/new-success.json")
                del instance[field]
                self.assertFalse(self.validator.is_valid(instance))

    def test_invalid_types_version_and_unknown_properties(self):
        for changes in ({"protocolVersion": 2}, {"protocolVersion": True}, {"ok": "true"}, {"command": ""}, {"data": []}, {"warnings": {}}, {"extra": 1}):
            with self.subTest(changes=changes):
                instance = read_json("examples/new-success.json")
                instance.update(changes)
                self.assertFalse(self.validator.is_valid(instance))

    def test_error_and_warning_codes_are_stable_identifiers(self):
        for code in ("argument conflict", "ArgumentConflict", "", "ERROR\n"):
            for field in ("error", "warnings"):
                with self.subTest(code=code, field=field):
                    instance = read_json("examples/new-conflict.json")
                    message = {"code": code, "message": "Example"}
                    instance[field] = message if field == "error" else [message]
                    self.assertFalse(self.validator.is_valid(instance))

    def test_legacy_discovery_does_not_invent_template_version(self):
        instance = read_json("examples/project-info-legacy.json")
        self.validator.validate(instance)
        self.assertEqual(instance["data"]["source"], "legacy")
        self.assertIsNone(instance["data"]["templateVersion"])


if __name__ == "__main__":
    unittest.main()
