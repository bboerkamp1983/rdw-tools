# ADR-002: Annotate, don't block

- Status: Accepted
- Date: 2026-10-01
- Amended by: ADR-003

## Context

The core library is used by several front-ends (CLI, REST API, possibly a
web interface). Callers differ in what they want to do with an unusual or
unknown license plate: a shop, an insurer and a hobbyist each decide for
themselves. A plate that is not found at the RDW can mean a typing error,
a vehicle too new for the open data, or a vehicle that is not in the dataset
for another reason.

The RDW answers an unknown plate with a normal successful response that
contains an empty list, not with an error.

## Decision

- Input that can never be a license plate (empty, only separators, longer
  than six characters after normalization) is rejected immediately, without
  calling the RDW (fail fast). The length rule is amended by ADR-003: input
  must be exactly six characters after normalization.
- Everything else is passed on to the RDW. The core never decides whether a
  plate is "valid"; the RDW data is the source of truth.
- The result of a lookup is a result object with an explicit status
  (Found, NotFound, InvalidInput, ServiceUnavailable), not a bare null.
- Later, plates are annotated with a neutral hint about their shape
  (recognized or unrecognized pattern). Names stay neutral: no "suspicious".
  A hint is an indication, not a verdict.

## Consequences

- Callers can guide their own users based on the status and the hint.
- Slightly more code than returning null or throwing exceptions.
- The list of plate patterns has to be maintained and tested separately
  (to be designed after the first real RDW call).
- Exported vehicles stay in the dataset: `export_indicator` is always `Ja`
  or `Nee`, and an exported vehicle is returned as `Found` with
  `export_indicator = "Ja"`. Export never causes `NotFound`; a not-found
  plate has no record and therefore no export indicator. Verified on
  2026-10-04, see `docs/research/rdw-open-questions.md`.
