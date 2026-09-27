"""Install a real nupkg in an isolated hive, generate, build and test its applications."""
import argparse
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]
PROVIDERS = {
    "postgres": ("Postgres", "Npgsql.EntityFrameworkCore.PostgreSQL", ("oracle", "microsoft.data.sqlclient", "microsoft.entityframeworkcore.sqlserver")),
    "sqlserver": ("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer", ("oracle", "npgsql")),
    "oracle": ("Oracle", "Oracle.EntityFrameworkCore", ("npgsql", "microsoft.data.sqlclient", "microsoft.entityframeworkcore.sqlserver")),
}
FORBIDDEN_PARTS = {"bin", "obj", ".git", ".github", "testresults", "artifacts", ".venv", "__pycache__"}


def run(arguments, cwd, env, success=True):
    result = subprocess.run([str(a) for a in arguments], cwd=cwd, env=env, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding="utf-8")
    print(result.stdout, end="", flush=True)
    if (result.returncode == 0) != success:
        raise RuntimeError(f"Unexpected exit {result.returncode}: {arguments}")
    return result.stdout


def package_version(package):
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        assert "content/.template.config/template.json" in names
        assert "content/.template.config/dotnetcli.host.json" in names
        assert "content/.openbase.json" in names
        for name in names:
            path = PurePosixPath(name)
            assert not path.is_absolute() and ".." not in path.parts, name
            assert not FORBIDDEN_PARTS.intersection(part.lower() for part in path.parts), name
            assert path.suffix.lower() not in {".dll", ".pdb", ".trx", ".user", ".nupkg"}, name
        nuspec = ET.fromstring(archive.read(next(n for n in names if n.endswith(".nuspec"))))
        metadata = nuspec.find("{*}metadata")
        assert metadata.find("{*}id").text == "w3ti.OpenBaseNET.Template"
        assert metadata.find("{*}packageTypes/{*}packageType").get("name") == "Template"
        return metadata.find("{*}version").text


def validate_files(destination, database, name, version):
    folder, provider, forbidden = PROVIDERS[database]
    manifest = json.loads((destination / ".openbase.json").read_text())
    Draft202012Validator(json.loads((ROOT / "contracts/openbase.schema.json").read_text())).validate(manifest)
    assert manifest["database"] == database
    assert manifest["rootNamespace"] == name
    assert manifest["templateVersion"] == version
    solution = (destination / manifest["solution"]).read_text(encoding="utf-8-sig")
    guid_pattern = r' = "[^\"]+", "[^\"]+", "\{([A-F0-9-]{36})\}"'
    original_guids = set(re.findall(guid_pattern, (ROOT / "OpenBaseNET.sln").read_text(encoding="utf-8-sig")))
    generated_guids = set(re.findall(guid_pattern, solution.upper()))
    assert len(generated_guids) == len(original_guids) and not generated_guids.intersection(original_guids)
    projects = list((destination / "src").rglob("*.csproj"))
    assert len(projects) == 4 and len(set(manifest["projects"].values())) == 4
    for project in manifest["projects"].values():
        assert (destination / project).is_file() and project.replace("/", "\\") in solution
    assert manifest["persistence"]["dbContext"] == f"{name}.Infrastructure.Persistence.OpenBaseDbContext"
    migrations = destination / manifest["persistence"]["migrationsPath"]
    assert migrations.is_dir() and (migrations / f"{folder}ModelSnapshot.cs").is_file()
    assert len(list(migrations.glob("*InitialCustomers.cs"))) == 1
    assert len(list(migrations.glob("*InitialCustomers.Designer.cs"))) == 1
    for other, _, _ in PROVIDERS.values():
        if other != folder:
            assert not list(destination.glob(f"src/**/{other}"))
            assert not list(destination.glob(f"tests/**/{other}"))
    for path in destination.rglob("*"):
        if path.is_file():
            assert not FORBIDDEN_PARTS.intersection(part.lower() for part in path.relative_to(destination).parts), path
            content = path.read_text(encoding="utf-8-sig")
            assert "OpenBaseTemplate" not in content and "__OPENBASE_" not in content, path
            if path.suffix in {".cs", ".csproj", ".props", ".sln"}:
                assert "OpenBaseNET" not in content, path
                assert "OpenBaseDatabase" not in content, path
    packages = ET.parse(destination / "Directory.Packages.props").findall(".//PackageVersion")
    package_names = [item.get("Include") for item in packages]
    assert provider in package_names
    assert not any(p.lower().startswith(forbidden) for p in package_names), package_names
    infra = ET.parse(destination / manifest["projects"]["infrastructure"])
    refs = infra.findall(".//PackageReference")
    assert any(p.get("Include") == provider and p.get("Condition") is None for p in refs)
    assert not infra.findall(".//Compile[@Remove]")
    api = ET.parse(destination / manifest["projects"]["api"])
    secret = api.find(".//UserSecretsId").text
    assert re.fullmatch(r"[0-9a-fA-F-]{36}", secret), secret
    assert json.loads((destination / f"src/{name}.Api/appsettings.json").read_text())["ConnectionStrings"]["Default"] == ""
    core = {p.relative_to(destination / "src").as_posix(): p.read_bytes()
            for layer in ("Domain", "Application") for p in (destination / f"src/{name}.{layer}").rglob("*") if p.is_file()}
    # Compare with canonical source as well as with the other generated engines.
    for relative, content in core.items():
        source = ROOT / "src" / relative.replace(name, "OpenBaseNET")
        if source.suffix == ".cs":
            assert content.decode("utf-8-sig").replace("\r\n", "\n") == source.read_text(encoding="utf-8-sig").replace("OpenBaseNET", name)
    return core, secret


def validate_dependencies(destination, database, name):
    _, provider, forbidden = PROVIDERS[database]
    assets = list((destination / "src").glob("*/obj/project.assets.json"))
    assert len(assets) == 4, assets
    for path in assets:
        packages = {k for k, v in json.loads(path.read_text())["libraries"].items() if v["type"] == "package"}
        if path.parent.parent.name.endswith((".Domain", ".Application")):
            assert not packages, (path, packages)
        else:
            assert any(p.startswith(provider + "/") for p in packages), path
        assert not any(p.lower().startswith(forbidden) for p in packages), (path, packages)
    runtime = json.loads((destination / f"src/{name}.Api/bin/Release/net10.0/{name}.Api.deps.json").read_text())["libraries"]
    assert any(p.startswith(provider + "/") for p in runtime)
    assert not any(p.lower().startswith(forbidden) for p in runtime)


def check(args, work):
    version = package_version(args.package)
    env = dict(os.environ)
    env.pop("OpenBaseDatabase", None)
    offline = {k: v for k, v in env.items() if not k.startswith("OPENBASE_TEST_") and k != "ConnectionStrings__Default"}
    hive = work / "hive"
    new = ["dotnet", "new", "--debug:custom-hive", str(hive)]
    run(new + ["install", str(args.package)], ROOT, offline)
    # Invalid/missing provider must fail before writing any application files.
    for index, options in enumerate(([], ["--database", "pgsql"], ["--database", "unknown"])):
        rejected = work / f"rejected-{index}"
        run(new + ["openbasenet", "--name", "Rejected", "--output", str(rejected)] + options, ROOT, offline, success=False)
        assert not rejected.exists() or not list(rejected.rglob("*"))

    cores, secrets = {}, set()
    for database in args.database or PROVIDERS:
        for name in ("MinhaApi", "Acme.Customers"):
            destination = work / database / (name + " output with spaces")
            run(new + ["openbasenet", "--name", name, "--output", str(destination), "--database", database], ROOT, offline)
            core, secret = validate_files(destination, database, name, version)
            assert core == cores.setdefault(name, core), f"Core differs for {database}"
            assert secret not in secrets, "User Secrets identifier reused across generated applications"
            secrets.add(secret)
            run(["dotnet", "restore", name + ".sln"], destination, offline)
            run(["dotnet", "build", name + ".sln", "--no-restore", "--configuration", "Release"], destination, offline)
            validate_dependencies(destination, database, name)
            run(["dotnet", "test", f"tests/{name}.Tests.Unit", "--no-build", "--configuration", "Release",
                 "--logger", "trx;LogFileName=unit.trx"], destination, offline)
            if args.integration and name == "Acme.Customers":
                run(["dotnet", "test", f"tests/{name}.Tests.Integration", "--no-build", "--configuration", "Release",
                     "--logger", "trx;LogFileName=integration.trx"], destination, env)
                run(["dotnet", "tool", "restore"], destination, env)
                migrations = destination / f"src/{name}.Infrastructure/Persistence/{PROVIDERS[database][0]}/Migrations"
                before = set(migrations.glob("*.cs"))
                run(["dotnet", "ef", "migrations", "add", "GeneratedProbe", "--project", f"src/{name}.Infrastructure",
                     "--output-dir", f"Persistence/{PROVIDERS[database][0]}/Migrations", "--configuration", "Release", "--no-build"], destination, env)
                assert len(set(migrations.glob("*.cs")) - before) == 2
                assert not (migrations / "OpenBaseDbContextModelSnapshot.cs").exists()
            print(f"VALIDATED {database}: {name}, manifest {version}, exclusive driver, shared core", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--database", choices=PROVIDERS, action="append")
    parser.add_argument("--integration", action="store_true", help="Also run tests on real configured databases and scaffold a migration")
    parser.add_argument("--output", type=Path, help="Keep generated applications and TRX reports in a new directory")
    args = parser.parse_args()
    args.package = args.package.resolve(strict=True)
    if args.output:
        work = args.output.resolve()
        work.mkdir(parents=True, exist_ok=False)
        check(args, work)
    else:
        with tempfile.TemporaryDirectory(prefix="openbase-template-check-") as temporary:
            check(args, Path(temporary))


if __name__ == "__main__":
    main()
