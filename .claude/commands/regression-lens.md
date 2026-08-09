---
description: Audit recent merges for defects the fixes themselves introduced
argument-hint: "[commit-ish or PR range, e.g. 4bf0230 or 'last 20 hours']"
---

Run a **regression lens** over this repository.

This is not a bug hunt. It audits **the fixes themselves**, asking only what they broke and which
siblings they missed. It exists because roughly one in five defects found in this codebase's audit
sweeps was caused by an earlier fix in the same campaign, and nothing else looks for those.

## Scope

Audit: **$ARGUMENTS**

If that is empty, audit everything merged to `develop` in the last 24 hours (`git log --oneline
--since="24 hours ago"`). Read each diff in full with `git show <sha>`, then read the **current** state
of every file touched — a later commit may already have changed it.

Keep the scope tight. One pass over one batch of fixes finds more than one pass over everything.

## The only two questions

1. **What did this fix break?** Did it tighten or loosen a rule — validation, guard, allowlist, filter,
   cache, default, lifetime, error path, order of operations — without accounting for a legitimate case
   that depended on the previous behaviour?
2. **Which siblings did it miss?** This codebase has sets of ten (alarm types, list components, edit
   dialogs, services, `*Create`/`*Update` DTO pairs) and eleven (locale files). A fix applied to one
   member is suspect until the others are checked.

## Calibration — the shapes this keeps finding

Give the auditor these, so it knows what it is looking for:

- **A constraint added, the bad case verified refused, the legitimate cases never enumerated.** A live
  resolution locked out users configured elsewhere (#601 → #626). Save-time validation broke seeding,
  because two presets carry empty filters on purpose (#604 → #637). An allowlist would have refused
  `blanche` and `npc 0`, both live in production.
- **A claim wider than its evidence.** `isAdmin` made live without distinguishing "not an admin" from
  "could not ask", so an outage de-admined live sessions (#624 → #656). A comment asserting one key was
  the only type-agnostic one, when the repo's own whitelist listed four (#671 → #674).
- **Validation in the wrong place.** Checks added *after* the thing they guard is created, so a refusal
  answers 400 and leaves an orphan behind (#647 → #665).
- **One member of a set of ten.** `bulkDelete` hardened, `bulkUpdateDistance` left (#603 → #641). Create
  DTO bounded, Update DTO not (#612 → #660).

## Ground rules for the auditor

- Verify against the code. `git show` the diff, then read the current file. Never reason from a commit
  message.
- Check any PoracleNG claim against `E:/PGAN/pogogit/PoracleNG`, pinned to the commit production runs —
  see the "Keep the PoracleNG Checkout Pinned To What Prod Runs" section of `CLAUDE.md`.
- Read "Fixing Defects Without Causing Them" in `CLAUDE.md` first.
- Before claiming a value should be refused, query production for what currently satisfies the loose
  rule. Connection details are in `.env`.
- Do **not** report defects in code the audited commits did not touch. That is a different lens's job.
- Do **not** re-report findings from earlier passes.
- **A clean result is the expected and desired outcome.** Say so plainly and stop. Manufacturing a
  marginal finding to appear thorough costs real work, because every finding gets acted on.

Report each finding with the commit that introduced it, the legitimate case now broken (or the sibling
now missed), a demonstrable failure case, and what should happen instead. Then list what was checked
and cleared.

## Run it until it comes back empty

One pass is not enough — its own fixes can introduce the next round. Re-run, scoping each pass to the
previous pass's fixes, until a pass reports nothing.

Observed convergence when this was first run: **8 → 5 → 2 → 1 → 0**. Pass one held a severe defect (an
outage de-admining a live session); pass two another (an orphan profile left behind a 400); by pass four
the only finding was an overclaiming comment. Expect roughly that shape. Stopping at pass one would have
left five defects live.

## Fixing what it finds

Follow `CLAUDE.md`. In particular: give every guard a **legitimate-case-still-passes** test beside the
refusal test, and **revert the fix and confirm the new test goes red** before trusting it. A test written
alongside a fix encodes that fix's own assumptions and passes either way — one spec in this repo was
asserting a broken request shape, so the suite was defending the bug.
