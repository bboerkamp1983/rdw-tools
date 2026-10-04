# Research: open questions verified against real RDW data

- Issue: #25
- Dataset: `m9d7-ebf2` ("Gekentekende voertuigen")
- Endpoint: `https://opendata.rdw.nl/resource/m9d7-ebf2.json`
- Queried: 2026-10-04 (UTC). Counts change as the RDW updates the dataset.

All queries below are SoQL, sent as the `$query` parameter (URL-encoded), for
example:

```sh
curl -G "https://opendata.rdw.nl/resource/m9d7-ebf2.json" \
  --data-urlencode '$query=SELECT count(*)'
```

## 1. Are there plates shorter than 6 characters?

**Finding: no. Every one of the 16,865,476 records has a plate of exactly 6
characters.**

| Query | Result |
| --- | --- |
| `SELECT count(*)` | 16,865,476 |
| `SELECT length(kenteken) AS len, count(*) AS n GROUP BY len ORDER BY len` | one group: `len = 6`, `n = 16,865,476` |
| `SELECT count(*) WHERE length(kenteken) < 6` | 0 |
| `SELECT count(*) WHERE length(kenteken) > 6` | 0 |
| `SELECT count(*) WHERE kenteken IS NULL` | 0 |
| `SELECT count(*) WHERE kenteken like "%-%" OR kenteken like "% %"` | 0 |
| `SELECT count(*) WHERE upper(kenteken) != kenteken` | 0 |

So the stored plate is always 6 upper-case letters/digits without separators,
which matches what `LicensePlateNormalizer.Normalize` produces.

Examples, including the oldest vehicles in the set (old sidecodes are still
6 characters):

| kenteken | voertuigsoort | datum_eerste_toelating |
| --- | --- | --- |
| `RM0480` | Personenauto | 1887-01-01 |
| `TLB86H` | Land- of bosbouwtrekker | 1889-06-30 |
| `DR332L` | Bromfiets | 1889-06-30 |
| `X998ZG` | Personenauto | (current vehicle) |

A short plate is answered like any unknown plate: `?kenteken=AB12` returns
HTTP 200 with an empty list `[]`.

**Limitation.** This proves that a plate shorter than 6 characters can never
be *found in this dataset*. It does not prove that no short plate exists
anywhere in the Netherlands (special plates, for example for the Royal House,
are not in this open dataset as far as we can see; the data cannot tell us).

## 2. How does `export_indicator` behave?

**Finding: it is always `"Ja"` or `"Nee"`, never missing, and exported
vehicles stay in the dataset.**

| Query | Result |
| --- | --- |
| `SELECT export_indicator, count(*) AS n GROUP BY export_indicator` | `Ja`: 922,075; `Nee`: 15,943,401 |
| `SELECT count(*) WHERE export_indicator IS NULL` | 0 |

Related fields for exported vehicles
(`GROUP BY export_indicator, tenaamstellen_mogelijk, wam_verzekerd`):

| export_indicator | tenaamstellen_mogelijk | wam_verzekerd | n |
| --- | --- | --- | --- |
| Ja | Nee | Nee | 780,130 |
| Ja | Nee | Ja | 94,124 |
| Ja | Nee | N.v.t. | 47,821 |
| Nee | Ja | Ja | 13,315,926 |
| Nee | Ja | Nee | 1,173,496 |
| Nee | Ja | N.v.t. | 1,382,588 |
| Nee | Nee | Ja | 34,391 |
| Nee | Nee | Nee | 31,847 |
| Nee | Nee | N.v.t. | 5,153 |

Observations:

- Every exported vehicle has `tenaamstellen_mogelijk = "Nee"` (it can no
  longer be registered to an owner). The reverse is not true: 71,391
  non-exported vehicles also have `tenaamstellen_mogelijk = "Nee"`.
- Exported vehicles usually have no `datum_tenaamstelling`: only 116,546 of
  922,075 have one (`SELECT export_indicator, count(datum_tenaamstelling),
  count(*) GROUP BY export_indicator`). In the JSON the field is then simply
  absent, which our parser already turns into null.
- Example exported vehicle: `TGNJ53` (Toyota Corolla, 1998),
  `export_indicator = "Ja"`, `tenaamstellen_mogelijk = "Nee"`,
  `wam_verzekerd = "Nee"`, no `datum_tenaamstelling`. A direct lookup
  (`?kenteken=TGNJ53`) returns the full record.

**Relation to not-found.** Export does not cause a not-found: an exported
vehicle is returned as a normal record with `export_indicator = "Ja"`. A
not-found plate (for example `?kenteken=ZZ999Z`) returns HTTP 200 with `[]`
and therefore has no `export_indicator` at all. So `NotFound` and "exported"
are separate situations; the reason a plate is not found cannot be read from
this dataset.

## 3. Proposals (not implemented)

These need the owner's decision. Item A changes behavior and needs an ADR
change first (CLAUDE.md: "Stop and ask first").

### A. `TryNormalize`: reject plates shorter than 6 characters (recommended)

ADR-002 rejects input "that can never be a license plate". The data shows
that every plate in `m9d7-ebf2` is exactly 6 characters, so a shorter input
can never be `Found`; today it costs an RDW call that always returns
`NotFound`.

Proposal: change the length check in `TryNormalize` from
`Length == 0 || Length > 6` to `Length != 6`, update ADR-002's decision text
("not exactly six characters after normalization"), and add tests for 1 to 5
characters returning `InvalidInput`. Record in the ADR that the rule is based
on this research and should be revisited if a dataset with other plate
formats is added.

Alternative: keep accepting short input. This is safe (the result is
`NotFound`) but wastes a call and gives the user a less precise message.

### B. ADR-002: replace the open point about the export indicator

Replace the last consequence ("How exported vehicles appear ... still to be
verified") with the finding: exported vehicles remain in the dataset with
`export_indicator = "Ja"`; export never causes `NotFound`.

### C. Expose the export status (later, separate issue)

`export_indicator` is not mapped yet. If we add it, parse `"Ja"`/`"Nee"` in
`RdwValueParser` to a `bool?` (anything else becomes null), so front-ends can
annotate "exported" without RDW logic of their own. This fits "annotate,
don't block": an exported vehicle is still `Found`.
