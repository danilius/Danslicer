# Maintaining the user guide

`docs/wiki` is the versioned source for the GitHub wiki. Edit these pages with normal Markdown links to neighbouring `.md` files so the guide is readable in the main repository too.

Validate links, then stage into a separately cloned wiki:

```powershell
python build/wiki.py
git clone https://github.com/danilius/Danslicer.wiki.git artifacts/documentation-wiki
python build/wiki.py --stage artifacts/documentation-wiki
git -C artifacts/documentation-wiki diff --check
git -C artifacts/documentation-wiki diff --stat
```

Review and commit only the intended wiki files, then push the wiki repository. The staging command rewrites local page links to GitHub wiki URLs and image links to the wiki's raw image URLs. It does not delete unknown destination files, commit, or push. GitHub requires an initial wiki page before cloning an empty wiki.

## Update checklist

- Match current visible control names and command scope. Check `WindowKeymap.cs` and `ViewportControl.cs` when keys change.
- Check support/structure/raft AXAML tooltips and their core implementations when documenting settings; do not infer physical calibration from defaults.
- Keep current development features identified until included in a published build.
- Inspect every added image. Use actual UI or renderer output and state if a capture lacks the GPU viewport.
- Keep `_Sidebar.md` and Home in sync when adding pages.
- Validate local links with `build/wiki.py` and inspect a published wiki page after pushing.

## Image provenance

The guide uses unchanged existing repository captures:

| Guide image | Original capture |
| --- | --- |
| `viewport.png` | `docs/ui-refresh/evidence/task-05-gl/Deferred-supports-isolation.png` |
| `support-settings.png` | `docs/ui-refresh/evidence/task-10/workspace-support.png` |
| `rafts.png` | `docs/ui-refresh/evidence/task-05-ui-final/settings-raft.png` |
| `printers.png` | `docs/ui-refresh/evidence/task-09/settings-printers.png` |
| `slicing.png` | `docs/ui-refresh/evidence/task-08/workflow-sliced.png` |
