# ADR-003: Plates are exactly six characters

- Status: Accepted
- Date: 2026-10-04
- Amends: ADR-002

## Context

ADR-002 rejects input "that can never be a license plate" without calling
the RDW. It defined that as empty, only separators, or longer than six
characters after normalization. Shorter input was passed on to the RDW
because we did not know whether shorter plates exist.

Research against the real dataset `m9d7-ebf2` on 2026-10-04 (issue #25,
`docs/research/rdw-open-questions.md`) found that all 16,865,476 plates are
exactly six characters: none shorter, none longer, none missing. Input
shorter than six characters can therefore never be `Found`; it only costs an
RDW call that always returns `NotFound`.

## Decision

- After normalization (separators removed, upper case), input that is not
  exactly six characters is rejected as `InvalidInput`, without calling the
  RDW.
- This replaces the length rule in ADR-002. The rest of ADR-002 stays: we
  still never decide ourselves whether a six-character plate is "valid".

## Consequences

- Fewer useless RDW calls, and a clearer message for the user than
  "not found".
- The rule depends on this dataset. If we add a dataset with other plate
  formats, or the RDW data changes, revisit this ADR and repeat the length
  query from the research document.

## When the RDW changes its plate format

The plate length is defined in one place: the constant
`LicensePlateNormalizer.PlateLength` in `src/Rdw.Core`. The `InvalidInput`
message and the tests tied to the length are built from it. When the RDW
changes its plate format (for example longer plates):

1. Repeat the length query from `docs/research/rdw-open-questions.md`
   (`SELECT length(kenteken) AS len, count(*) AS n GROUP BY len`) and record
   the new result there.
2. If the data shows one new fixed length, change `PlateLength`. If several
   lengths occur, a single constant no longer fits: write a new ADR first.
3. Update the tests with concrete example plates in
   `LicensePlateNormalizerTests` and `RdwClientTests`; they are expected to
   fail after the change, as a reminder.
4. Update this ADR, the example in ADR-004 and CLAUDE.md.

The length is deliberately a constant in code, not a runtime setting
(`appsettings.json`): a change in the RDW format needs the steps above
(research, tests, documentation), and a setting would let a running
installation disagree with the tested rule.
