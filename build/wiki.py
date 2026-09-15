"""Validate the user guide, or stage it for a separately cloned GitHub wiki."""
from pathlib import Path
import argparse
import re
import shutil

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs" / "wiki"
WIKI = "https://github.com/danilius/Danslicer/wiki/"
IMAGES = "https://raw.githubusercontent.com/wiki/danilius/Danslicer/"
LINK = re.compile(r"(!?\[[^\]]*\]\()([^\s)]+)(\))")


def validate():
    pages = [ROOT / "README.md", *SOURCE.glob("*.md")]
    count = 0
    for page in pages:
        text = page.read_text(encoding="utf-8")
        if re.search(r"<!-- (?:SETTINGS|STRUCTURE|RAFT|KEYMAP)_TABLE", text):
            raise ValueError(f"Unfinished table in {page}")
        for match in LINK.finditer(text):
            target = match[2].split("#")[0]
            if not target or re.match(r"https?://", target):
                continue
            if not (page.parent / target).exists():
                raise ValueError(f"Broken local link in {page.name}: {target}")
            count += 1
    print(f"Validated {len(pages)} Markdown files and {count} local links/images.")


def stage(destination):
    destination = Path(destination).resolve()
    if destination == SOURCE or SOURCE in destination.parents:
        raise ValueError("Stage outside the source guide directory")
    if not (destination / ".git").exists():
        raise ValueError("Destination must be an existing, separately cloned wiki repository")
    for page in SOURCE.glob("*.md"):
        def rewrite(match):
            target = match[2]
            if target.startswith("images/"):
                target = IMAGES + target
            elif ".md" in target and not re.match(r"https?://", target):
                name, _, anchor = target.partition("#")
                target = WIKI + Path(name).stem + ("#" + anchor if anchor else "")
            return match[1] + target + match[3]
        (destination / page.name).write_text(LINK.sub(rewrite, page.read_text(encoding="utf-8")), encoding="utf-8")
    shutil.copytree(SOURCE / "images", destination / "images", dirs_exist_ok=True)
    print(f"Staged wiki pages and images in {destination}; review, commit, and push separately.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", metavar="WIKI_CLONE")
    args = parser.parse_args()
    validate()
    if args.stage:
        stage(args.stage)
