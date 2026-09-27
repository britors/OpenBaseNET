"""Stage the tested sources and pack one template; never copy build or CI artifacts."""
import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE_NAME = "OpenBaseTemplate"
PROVIDER_PACKAGES = {
    "Npgsql.EntityFrameworkCore.PostgreSQL": "postgres",
    "Microsoft.EntityFrameworkCore.SqlServer": "sqlserver",
    "Oracle.EntityFrameworkCore": "oracle",
}


def project_content(path):
    tree = ET.fromstring(path.read_text(encoding="utf-8-sig"))
    for group in tree:
        for node in list(group):
            if node.tag == "Compile" and "Remove" in node.attrib:
                group.remove(node)  # Unselected provider source folders are physically excluded.
            elif node.tag in {"OpenBaseDatabase", "BaseIntermediateOutputPath", "BaseOutputPath", "DefaultItemExcludes"}:
                group.remove(node)
            elif node.tag == "UserSecretsId":
                node.text = "__OPENBASE_USER_SECRETS__"
            elif node.tag in {"PackageReference", "PackageVersion"} and node.get("Include") in PROVIDER_PACKAGES:
                provider = PROVIDER_PACKAGES[node.get("Include")]
                node.attrib.pop("Condition", None)
                index = list(group).index(node)
                group.insert(index, ET.Comment(f"#if ({provider})"))
                group.insert(index + 2, ET.Comment("#endif"))
    ET.indent(tree, space="  ")
    return ET.tostring(tree, encoding="unicode") + "\n"


def stage(content, version):
    # An allowlist keeps .git, credentials, build outputs and repository CI out of the package.
    files = [ROOT / p for p in ("OpenBaseNET.sln", "Directory.Build.props", "Directory.Packages.props",
                               "global.json", ".config/dotnet-tools.json", ".gitignore", "LICENSE.txt",
                               "src/OpenBaseNET.Api/appsettings.json")]
    for folder in ("src", "tests/OpenBaseNET.Tests.Unit", "tests/OpenBaseNET.Tests.Integration"):
        files.extend(p for p in (ROOT / folder).rglob("*") if p.is_file()
                     and not any(part.lower() in {"bin", "obj", "testresults", "artifacts"} for part in p.relative_to(ROOT).parts)
                     and p.suffix in {".cs", ".csproj"})
    for source in sorted(files):
        relative = source.relative_to(ROOT).as_posix().replace("OpenBaseNET", SOURCE_NAME)
        target = content / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        text = project_content(source) if source.suffix in {".csproj", ".props"} else source.read_text(encoding="utf-8-sig")
        target.write_text(text.replace("OpenBaseNET", SOURCE_NAME), encoding="utf-8")

    config = json.loads((ROOT / "packaging/template.json").read_text())
    solution = (ROOT / "OpenBaseNET.sln").read_text(encoding="utf-8-sig")
    # Replace project/folder instance GUIDs, preserving the well-known project type GUIDs.
    config["guids"] = sorted(set(re.findall(r' = "[^"]+", "[^"]+", "\{([A-F0-9-]{36})\}"', solution)))
    config_dir = content / ".template.config"
    config_dir.mkdir()
    (config_dir / "template.json").write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
    (config_dir / "dotnetcli.host.json").write_text(json.dumps({
        "$schema": "http://json.schemastore.org/dotnetcli.host",
        "symbolInfo": {"database": {"longName": "database", "shortName": ""}},
    }, indent=2) + "\n", encoding="utf-8")

    manifest = json.loads((ROOT / "contracts/examples/postgres.openbase.json").read_text())
    manifest["templateVersion"] = version
    manifest["database"] = "__OPENBASE_DATABASE__"
    manifest["persistence"]["migrationsPath"] = "src/MinhaApi.Infrastructure/Persistence/__OPENBASE_PROVIDER_FOLDER__/Migrations"
    # The distinct sourceName preserves the canonical $schema URL containing OpenBaseNET.
    (content / ".openbase.json").write_text(json.dumps(manifest, indent=2).replace("MinhaApi", SOURCE_NAME) + "\n", encoding="utf-8")
    shutil.copyfile(ROOT / "packaging/generated-README.md", content / "README.md")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True, help="Version embedded in both package and generated manifest")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/packages")
    args = parser.parse_args()
    version_pattern = json.loads((ROOT / "contracts/openbase.schema.json").read_text())["$defs"]["version"]["pattern"]
    if "+" in args.version or not re.fullmatch(version_pattern, args.version):
        parser.error("Use a NuGet SemVer version without build metadata (e.g. 11.0.0-preview.1)")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="openbase-pack-") as temporary:
        work = Path(temporary)
        stage(work / "content", args.version)
        shutil.copyfile(ROOT / "packaging/OpenBaseNET.Template.csproj", work / "OpenBaseNET.Template.csproj")
        shutil.copyfile(ROOT / "packaging/README.md", work / "README.md")
        shutil.copyfile(ROOT / "global.json", work / "global.json")
        subprocess.run(["dotnet", "pack", "OpenBaseNET.Template.csproj", "--configuration", "Release",
                        f"-p:PackageVersion={args.version}", "--output", str(output)], cwd=work, check=True)
    print(output / f"w3ti.OpenBaseNET.Template.{args.version}.nupkg")


if __name__ == "__main__":
    main()
