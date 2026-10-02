# ADR-0075: Numeric comparison operators for attribute filters

Status: Accepted

Date: 2026-10-02

## Context

Attributes are stored as `Map(LowCardinality(String), String)`, so every
value is a string. `AttributeFilterOperator` and `SpanAttributeFilterOperator`
had no range operators. Filtering `http.response.status_code >= 500` or
`retry.count > 3` needed a regex or an `In` list. LogQL did accept
`< <= > >=` on `attr(...)` and `json(...)`, but compiled them to string
comparisons, so `'10' > '9'` was false.

Prior art: [signoz#9154](https://github.com/SigNoz/signoz/commit/8c29debb529738d2e90b50125c91200e36b114d8).

## Decision

### Four operators, compared as Float64

`GreaterThan`, `GreaterThanOrEqual`, `LessThan` and `LessThanOrEqual` are
appended to `AttributeFilterOperator`, `SpanAttributeFilterOperator` and the
pipeline-rule `AttributeConditionOperator`. They are appended, not inserted,
because MemoryPack encodes the enum as its ordinal (ADR-0016). The dashboard
ordinal arrays in `memorypack/enums.ts` grow the same way.

The SQL form, shared through `NumericAttributeComparison`, is:

```sql
ifNull(toFloat64OrNull(LogAttributes[{k:String}]) > {v:Float64}, 0)
```

- A missing key reads back as `''`, which is NULL, so no `mapContains` guard
  is needed. `ifNull(..., 0)` keeps the result a plain boolean in any
  composition.
- A non-numeric stored value is NULL and never matches.
- An operand that isn't a number (blank, `abc`, NaN) compiles to a constant
  false and binds no parameter, instead of failing the query.
- Operands are parsed with the invariant culture.

### Promoted columns

Promoted attribute columns (ADR-0062) are still `String`, so the same
`toFloat64OrNull(column)` form applies, with the `mapContains` guard dropped
for the same reason as above. This is exact for these operators, unlike
`Exists`/`Absent`/regex, because `''` can never satisfy a comparison.

### Live tail and pipeline rules

`LogFilterMatcher` and `PipelineRuleConditionMatcher` require both the
attribute value and the operand to parse as numbers, otherwise no match. This
mirrors the SQL form.

### LogQL

For `attr(...)` and `json(...)` comparisons with a range operator, a numeric
literal now compiles to the Float64 form above. A non-numeric literal keeps the
string comparison, so ISO timestamps and similar still sort lexicographically.
This changes the result of existing queries that used a numeric literal with a
range operator, which were previously lexicographic and almost certainly not
what the author meant.

## Alternatives considered

- **A typed numeric attribute column or map.** Would be faster, but needs a
  schema change and a backfill. `toFloat64OrNull` on the string map needs
  neither, and promotion already covers the hot keys.
- **Passing the operand as a string and letting ClickHouse cast.** A bad
  operand would fail the whole query instead of matching nothing.
- **Leaving LogQL lexicographic.** Would leave two different meanings for `>`
  on the same attribute depending on which query surface was used.

## Consequences

- `toFloat64OrNull` runs per row on the unpromoted map path, with no skip
  index to prune. Combine with a service or time filter on large windows, or
  promote the key.
- Values with thousands separators or units (`1,000`, `5ms`) are non-numeric
  and never match.
- Float64 loses precision above 2^53, so integer ids that large shouldn't be
  compared this way.
- Pipeline-rule conditions and live tail follow the same semantics, so a rule,
  an alert filter and a search agree.
