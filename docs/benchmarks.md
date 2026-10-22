# Action-count benchmark (synthetic)

`dotnet run --project tools/Aac.Bench` drives the real `CommunicationSession` with a simulated user
who never makes mistakes. The simulated user moves the focus with arrow actions, rows first and then
columns, and selects. Each move and each selection counts as one physical action. The counts include
pressing Speak, but not reaching the starting page.

Results with the keyboard-first QWERTY layout (Speak in the Enter position, no Approve step), the 59,000-word dictionary and no learning yet:

| Task | Letters only | Letters + word prediction | Quick / phrase page |
|---|---:|---:|---:|
| Answer yes/no | 23 | 20 | 1 |
| Ask for help | 59 | 25 | 5 |
| Ask for water | 63 | 26 | 8 |
| Simple sentence ("I want coffee") | 75 | 37 | – |
| Opinion about a documentary | 317 | 78 | – |
| Talk about a game | 165 | 73 | – |
| Negated feeling ("I'm not tired") | 62 | 34 | – |

## How to read this

- **Quick phrases and the phrase pages are the fastest paths by far** (1–10 actions). Putting the
  phrases the user actually uses most into them is the cheapest big win.
- **Navigation dominates.** Letter-by-letter typing costs about 5 to 6 actions per character, mostly
  cursor moves. This is why Phase 3 tests input methods that do not use a grid cursor.
- **These are first-day numbers.** The large dictionary covers far more words, but it ranks them by
  general English use, so task-specific words take a few more letters than with the old tiny list.
  Learning closes that gap: words and word pairs the user actually says move to the front.
- **None of this measures time, fatigue, reading effort or errors.** No usability claim can be made
  until we measure with the intended user.
