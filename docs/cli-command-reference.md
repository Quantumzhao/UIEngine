# CLI Command Reference

The UIEngine CLI is the proving frontend for the live object runtime. It navigates the
deterministic `CyclicDomain` example, but every command operates through the same semantic
descriptors and structured results available to other frontends.

## Start the CLI

From the repository root:

```sh
dotnet run --project Frontend/Cli/Cli.csproj
```

The executable creates one root named `world`. The prompt displays the current logical path:

```text
/> cd /world
/world>
```

When standard input or output is redirected, the CLI reads one command per line without the
interactive prompt. This is useful for deterministic scripts and tests, but the output is a
human-readable format rather than a stable machine protocol.

## Syntax rules

- Command verbs are case-insensitive: `get`, `GET`, and `GeT` are equivalent.
- Root names, member identifiers, action argument names, collection keys, and domain identities
  are case-sensitive. For example, `get Population` works but `get population` does not.
- Whitespace separates tokens. Put a token in double quotes when its value contains whitespace:

  ```text
  set Motto "New Horizon"
  call ReplaceCapital name="New Capital"
  ```

- Inside double quotes, `\` escapes the following character. Use `\"` for a literal quote and
  `\\` for a literal backslash. Single quotes have no special meaning.
- Values are converted with invariant culture. Reflected values support strings, Boolean values,
  characters, numeric types, nullable forms, and enum names. Enum names are matched
  case-insensitively; identifiers remain case-sensitive.
- An empty line does nothing. There is currently no `help` command.
- Interactive completion proposes commands and context-appropriate paths, values, writable
  values, collections, actions, action parameters, and observable members.

## Command summary

| Command | Purpose |
|---|---|
| `ls` | List roots, or list the members of the current object. |
| `ls <collection> [offset=<n>] [limit=<n>]` | Read a bounded portion of a collection. |
| `cd <absolute-path>` | Navigate to an exposed object. |
| `cd ..` | Navigate to the parent object location. |
| `inspect` | Show current-object identity, metadata, and operations. |
| `get <member>` | Read an exposed scalar value. |
| `set <member> <value>` | Convert, validate, and write an exposed scalar value. |
| `call <action> [name=value ...]` | Invoke a synchronous or asynchronous action. |
| `watch <value-or-reference-or-collection>` | Stream normalized changes until cancellation. |
| `exit` | Dispose the session and host, then exit. |

## Navigation and logical paths

Logical paths are absolute, case-sensitive paths over exposed semantics, not arbitrary CLR
property chains. `/` is the root list and the first segment is a registered root name.

```text
cd /
cd /world
cd /world/Economy
cd /world/Nations[index=0]/Capital
cd /world/Nations[index=0]/Capital/OwnerNation
```

A collection segment may select one reference element in one of three forms:

| Selector | Meaning | Example |
|---|---|---|
| `[index=<n>]` | Zero-based collection position | `/world/Nations[index=0]` |
| `[key=<value>]` | Provider-defined stable key | `/catalog/Items[key=SKU-42]` |
| `[identity=<value>]` | Element domain identity | `/world/Nations[identity=nation%2FN1]` |

The legacy Phase 1 index form `/world/Nations/0` is also accepted. Successful navigation prints
and stores the canonical form `/world/Nations[index=0]`.

Identifiers and selector values use UTF-8 percent escaping when they contain path delimiters or
other reserved characters. For example, the domain identity `nation/N1` is written as
`nation%2FN1` in a selector. Arbitrary predicates and query expressions are not supported.

`cd` must finish at an object. A path ending at a scalar, action, or unselected collection is not
a navigable location. `cd ..` moves to the preceding object location captured during resolution;
at `/` it reports `INVALID_INPUT`.

The current location retains its canonical path, expected type, and optional domain identity.
Commands re-resolve that location, allowing a compatible replacement to be found without
silently binding to an object with a different identity or type.

## `ls`

At `/`, `ls` lists registered roots:

```text
/> ls
root world
```

At an object, it groups exposed members by semantic role:

```text
/world/Nations[index=0]> ls
value Code
value Motto
value OptionalNote
value Population
value Turn
reference Capital
collection Cities
action AdvanceTurn
action ReplaceCapital
action SimulateGrowthAsync
```

Member output is sorted by identifier within each role.

### Collection listing

```text
ls <collection> [offset=<non-negative-integer>] [limit=<positive-integer>]
```

Examples:

```text
ls Nations
ls PopulationForecast offset=9998 limit=2
```

The default limit is 20, further bounded by the host configuration. The CLI chooses a supported
mode in this order: page, virtualized range, then finite snapshot. Snapshot-only collections
require an offset of zero. The result header reports the selected mode, offset, returned count,
optional total count, `hasMore`, and any continuation token. Each entry reports its position,
optional key, and one of `null`, a scalar value, or a reference with runtime and optional domain
identity.

Although page results can report a continuation token, the current CLI accepts only `offset` and
`limit`; it does not accept a continuation token as input.

## `inspect`

```text
inspect
```

`inspect` requires a selected object and prints:

- canonical path, availability, CLR type, runtime identity, optional domain identity, and summary;
- value type, readability, writability, nullability, range, finite options, validation rules,
  unit, and tags;
- reference type;
- collection element/key types and capabilities; and
- action parameters, result type, sync/async status, cancellation and progress support, risk, and
  confirmation metadata.

Runtime identities are process-local GUIDs. They identify object references within one host and
must not be persisted. Domain identities and logical paths are the durable concepts.

## `get` and `set`

```text
get <member>
set <member> <value>
```

Examples:

```text
get Population
set Population 120
set Motto "New Horizon"
```

`get` accepts readable scalar values only. `set` accepts writable scalar values only, converts the
text to the descriptor's type, applies nullability, range, finite-selection, data-annotation, and
programmatic validation, then writes the live domain member.

A successful read whose value is null prints `member = null`. That is distinct from an unavailable
or missing target, which prints a structured error. The current command grammar has no dedicated
null literal for writes; the token `null` is ordinary text presented to the target converter.

## `call`

```text
call <action> [name=value ...]
```

Arguments are named, case-sensitive, and may appear in any order. Do not put whitespace around
`=`. Quote a value that contains whitespace. Each parameter may be supplied only once.

```text
call AdvanceTurn populationDelta=5
call ReplaceCapital name="Replacement Capital"
call SimulateGrowthAsync years=3 populationPerYear=2
```

Required, optional, defaulted, nullable, range, and selection rules come from the action
descriptor. Framework-injected progress and cancellation parameters are not command arguments.

The command uses the same lifecycle for synchronous and asynchronous actions. It prints every
normalized progress record, waits for the single terminal result, and then prints either the
result or a structured failure:

```text
progress SimulateGrowthAsync token=1 value=SimulationProgress { Year = 1, Population = 122 }
SimulateGrowthAsync => 126
```

Cancelling the current command is forwarded to the invocation only when the action advertises
cancellation support.

## `watch`

```text
watch <value-or-reference-or-collection>
```

Examples:

```text
watch Population
watch Cities
```

`watch` opens one bounded observation stream and occupies the command loop until it is cancelled.
The interactive prompt's command-cancellation gesture stops it; redirected callers cancel the
session token. Output includes the ordering token, change kind, member, old/new values, old/new
indices, and the number of dropped records if the bounded buffer overflowed.

Values, references, and collections are observable; actions are not. Automatic observation uses
host-configured adapters or .NET property/collection notifications. The CLI does not expose the
Core API's explicit polling-mode options.

## Errors and session behavior

Expected failures have a stable code followed by a human-readable message:

```text
error TARGET_MISSING: Member 'population' was not found at '/world/Nations[index=0]'.
error CONVERSION_FAILED: 'invalid' cannot be converted to 'Int32'.
error VALIDATION_FAILED: Value 'Population' failed validation.
issue OUT_OF_RANGE target=VALUE id=Population: ...
```

Syntax, navigation, binding, conversion, validation, collection, observation, and invocation
failures do not end the session. `exit` is the normal terminating command and prints `bye`.

## End-to-end example

```text
ls
cd /world/Nations/0
inspect
get Population
set Population 120
call AdvanceTurn populationDelta=5
get Population
cd /world/Nations[index=0]/Capital/OwnerNation
get Code
cd /world
ls PopulationForecast offset=9998 limit=2
exit
```

The path through `Capital/OwnerNation` closes the sample's object cycle. It resolves to the same
runtime identity as the original nation instead of creating a recursive wrapper tree.
