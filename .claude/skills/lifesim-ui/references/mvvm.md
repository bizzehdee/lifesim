# MVVM & Data Flow (LifeSim)

View-model patterns and how UI state flows from the engine. General Avalonia MVVM/styling practices live in the user-level **`avalonia-ui`** skill (`practices.md`); this file covers only the LifeSim-specific data flow. See [`lifesim.md`](../../../../lifesim.md) §12, §16, §18.

## Binding rules
<!-- TODO -->
- Compiled bindings (`x:CompileBindings`, `x:DataType`) required.
- Collection/virtualization patterns for large organism sets.

## Data flow: live state vs. snapshot
<!-- TODO -->
- Both feed the same view-models; the view-model is agnostic to source.
- Desktop/small-world browser mode = live in-process state; streaming browser mode = deserialized snapshots.
- Update cadence / throttling from the engine thread to the UI thread.

## The edit flow (interventions)
<!-- TODO -->
- Editing appends explicit `edit_log` entries (lifesim.md §16); never mutate state silently.
- Where edits are validated before they reach the Core / snapshot.
