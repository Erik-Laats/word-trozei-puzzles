# Trozei daily puzzles

The daily puzzles for the Trozei word game, served as static JSON by GitHub Pages:

- `docs/upcoming.json`: the last 7 days and the next 14 days. This is the file the app downloads.
- `docs/puzzles/YYYY-MM-DD.json`: one file per day.

```json
{ "date": "2026-10-28", "version": 1, "letters": "adeilmnortz",
  "targets": ["diner", "deter", "alarm"], "seed": 20261028, "note": null }
```

`letters` are the tiles that can be dealt that day (`q` is the Qu tile). `targets` are the three star words. They may only use those letters and must be in the game dictionary.

## How it runs

A GitHub Action (`.github/workflows/publish.yml`) runs every day at 05:23 UTC. It makes sure the next 14 days exist, validates every file and commits. It never changes a day that is already published.

Version 1 of every day is exactly what the app generates offline. The generator in `generator/` is compiled from the game's own code, so players without a connection still get the same puzzle unless a day was regenerated.

## Regenerating a day

**From GitHub:** go to **Actions → Publish puzzles → Run workflow**. Enter the date and, optionally, a reason.
- A future day is replaced by a new version (v2, v3, …).
- Today's or a past day also needs **force**. Then reset that day's leaderboard, because scores from two different puzzles can't be compared.

**By hand:** edit `docs/puzzles/<date>.json` in GitHub's editor, for example to pick different targets, and raise `version`. When you commit, the workflow validates the file and rebuilds `upcoming.json`. If it fails, the run shows why and nothing is published.

**Locally** (needs the .NET 8 SDK):

```
dotnet build generator -c Release
dotnet generator/bin/Release/net8.0/PuzzleGen.dll regenerate --site docs --words generator/words --date 2026-11-02 --reason "dull targets"
```

Phones pick up a change the next time the app starts or comes back to the foreground (at most hourly). Publish changes a day ahead whenever possible.

## Updating the generator

`generator/core` and `generator/words` are copied from the game project. Don't edit them here: change the game and run `Tools/PuzzleGen/sync-puzzles-repo.sh` there. Changing the generator never alters puzzles already published.

Word lists: SCOWL (see `generator/words/SCOWL-LICENSE.txt`).
