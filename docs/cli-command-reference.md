# CLI Command Reference

The CLI is UIEngine's proving frontend. It starts with a deterministic `world` root and operates
only through the public Core runtime.

## Start

```sh
dotnet run --project Frontend/Cli/Cli.csproj
```

Interactive mode provides editing and completion. Redirected input reads one command per line.
Output is human-readable and is not a machine protocol.

## Syntax

- Command verbs are case-insensitive; names and argument names are case-sensitive.
- Double quotes preserve whitespace: `set Motto "New Horizon"`.
- Inside quotes, `\` escapes the following character.
- Values use invariant-culture conversion.
- Paths are absolute and percent-escaped.

## Commands

| Command | Purpose |
|---|---|
| `ls` | List roots or members at the current object. |
| `ls <collection> [offset=<n>] [limit=<n>]` | Read one bounded collection slice. |
| `cd <absolute-path>` | Navigate to an object. |
| `cd ..` | Return to the previous object location. |
| `inspect` | Show current object metadata. |
| `get <member>` | Read a scalar value. |
| `set <member> <value>` | Convert, validate, and write a scalar value. |
| `call <action> [name=value ...]` | Invoke an action and report its result. |
| `exit` | Dispose the session and host. |

## Paths

The first path segment is a root. References are ordinary segments. Collections require a
canonical selector to navigate to an entry:

```text
cd /world
cd /world/Economy
cd /world/Nations[index=0]/Capital
cd /world/Nations[index=0]/Capital/OwnerNation
```

Selectors are `[index=<non-negative integer>]` and `[key=<value>]`. The old `/Collection/0` form
is deliberately unsupported. `cd` must end at an object, not a scalar, action, or unselected
collection.

The session re-resolves its canonical path before each operation and follows the object currently
located there.

## Collection Output

```text
ls PopulationForecast offset=9998 limit=2
collection PopulationForecast offset=9998 count=2 total=10000 hasMore=False
[9998] value=10098
[9999] value=10099
```

The default limit is 20. The host maximum remains authoritative. Entries show their position,
optional key, and exactly one of null, scalar value, or reference handle.

## Inspection, Values, and Actions

`inspect` prints the canonical path, CLR type, runtime handle, summary, and role-specific member
metadata. It includes nullability, enum options, ranges, collection element/key types, and action
parameter/default/status information.

Examples:

```text
get Population
set Population 120
set Motto "New Horizon"
call AdvanceTurn populationDelta=5
call SimulateGrowthAsync years=3 populationPerYear=2
```

Arguments are named and may appear in any order. The CLI retains the resolved method-node occurrence
while it awaits that node's result task and then prints the returned structured result.

## Errors

Expected failures do not end the session:

```text
error NOT_FOUND: Member 'population' was not found.
error CONVERSION_FAILED: 'invalid' cannot be converted to 'Int32'.
error VALIDATION_FAILED: Value 'Population' failed validation.
issue OUT_OF_RANGE name=Population: 'Population' must be between 0 and 1000000.
```

## End-to-End Example

```text
ls
cd /world
inspect
ls PopulationForecast offset=9998 limit=2
cd /world/Nations[index=0]
get Population
set Population 120
call AdvanceTurn populationDelta=5
cd /world/Nations[index=0]/Capital/OwnerNation
get Code
exit
```

The final navigation closes the example cycle and resolves to the original nation handle.
