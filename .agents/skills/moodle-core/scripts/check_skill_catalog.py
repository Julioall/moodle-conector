#!/usr/bin/env python3
"""Check that local Moodle skills have metadata and reference real connector names."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


TOOL_NAME_RE = re.compile(r'Name\s*=\s*"([a-z][a-z0-9_]*)"')
IDENTIFIER_RE = re.compile(r'"([a-z][a-z0-9]+(?:_[a-z0-9]+)+)"')
SKILL_REF_RE = re.compile(r"`([a-z][a-z0-9]*(?:_[a-z0-9]+)+)`")
YAML_FIELD_RE = re.compile(r'^\s*(display_name|short_description|default_prompt):\s*"([^"]*)"\s*$', re.MULTILINE)
NON_CONNECTOR_REFS = {
    "dado_indisponivel",
    "falha_parcial",
    "funcao_indisponivel",
    "sem_permissao",
    "zero_observado",
}


def find_repo_root(script_path: Path) -> Path:
    # .../<repo>/.agents/skills/moodle-core/scripts/check_skill_catalog.py
    return script_path.resolve().parents[4]


def collect_names(source_root: Path) -> set[str]:
    names: set[str] = set()
    for path in source_root.rglob("*.cs"):
        try:
            content = path.read_text(encoding="utf-8")
            names.update(TOOL_NAME_RE.findall(content))
            # Also include registered Moodle operations and business-flow names,
            # which are ordinary string literals rather than MCP Name attributes.
            names.update(IDENTIFIER_RE.findall(content))
        except UnicodeDecodeError:
            continue
    return names


def collect_skill_references(skills_root: Path) -> set[str]:
    refs: set[str] = set()
    for path in skills_root.rglob("SKILL.md"):
        refs.update(SKILL_REF_RE.findall(path.read_text(encoding="utf-8")))
    return refs


def validate_metadata(skill_dir: Path) -> list[str]:
    errors: list[str] = []
    metadata_path = skill_dir / "agents" / "openai.yaml"
    if not metadata_path.is_file():
        return [f"{skill_dir.name}: missing agents/openai.yaml"]

    values = dict(YAML_FIELD_RE.findall(metadata_path.read_text(encoding="utf-8")))
    for field in ("display_name", "short_description", "default_prompt"):
        if not values.get(field):
            errors.append(f"{skill_dir.name}: missing interface.{field}")
    if values.get("short_description") and not 25 <= len(values["short_description"]) <= 64:
        errors.append(f"{skill_dir.name}: short_description must be 25-64 characters")
    if values.get("default_prompt") and f"${skill_dir.name}" not in values["default_prompt"]:
        errors.append(f"{skill_dir.name}: default_prompt must mention ${skill_dir.name}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=None)
    args = parser.parse_args()

    repo_root = (args.repo_root or find_repo_root(Path(__file__))).resolve()
    skills_root = repo_root / ".agents" / "skills"
    source_root = repo_root / "src"
    if not skills_root.is_dir() or not source_root.is_dir():
        print(f"Repository layout not found below {repo_root}", file=sys.stderr)
        return 2

    errors: list[str] = []
    skills = sorted(path for path in skills_root.iterdir() if path.is_dir())
    for skill_dir in skills:
        if not (skill_dir / "SKILL.md").is_file():
            errors.append(f"{skill_dir.name}: missing SKILL.md")
        errors.extend(validate_metadata(skill_dir))

    known_names = collect_names(source_root)
    references = collect_skill_references(skills_root)
    unknown_references = sorted(
        reference
        for reference in references
        if reference not in known_names and reference not in NON_CONNECTOR_REFS
    )
    if unknown_references:
        errors.extend(f"unknown connector name referenced by skills: {name}" for name in unknown_references)

    print(f"Skills checked: {len(skills)}")
    print(f"Connector names discovered: {len(known_names)}")
    print(f"Names referenced by skills: {len(references)}")
    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        return 1

    print("Skill catalog is consistent.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
