# Golden vectors for `XrplNumber` arithmetic

`vectors.txt.gz` holds 19,150 results computed by rippled's own `Number.cpp`. `TestUNumberVectors` replays every line through the C# port and requires the same result, bit for bit.

The vectors cover each of the four mantissa scales (`Small`, `LargeLegacy`, `Large320`, `Large330`) under each of the four rounding modes, with 100 random cases per combination. The operations are construction from a wire pair and from an internal mantissa, `+`, `-`, `*`, `/`, conversion to `int64`, `truncate`, `power(f, n)`, `root2`, `root(f, d)` and `power(f, n, d)`.

## Source

- rippled commit [`00606bec1a41454a7d7741c59c447bdd8b3cb3c2`](https://github.com/XRPLF/rippled/tree/00606bec1a41454a7d7741c59c447bdd8b3cb3c2) (22 September 2026)
- `include/xrpl/basics/Number.h` and `src/libxrpl/basics/Number.cpp`, unmodified
- `stubs/` replaces the two headers `Number.cpp` pulls in from the rest of rippled: `Throw` and `logicError` throw the named exception, and the assertion macros map to `assert`

## Line format

```text
<scale> <mode> <op> <operands...> = <result>
```

- A number is `<mantissa>e<exponent>` in the wire form of `Number::mantissa()` and `Number::exponent()`, or `Z` for zero.
- An integer operand (a power, a root) or an `int` result is a plain decimal number.
- `!overflow` means `std::overflow_error` was thrown. `!error` means any other exception was thrown, for example a division by zero or an even root of a negative value.

## Regenerate

Run these commands from this directory, with Docker available:

```bash
curl -sSfLo Number.cpp https://raw.githubusercontent.com/XRPLF/rippled/00606bec1a41454a7d7741c59c447bdd8b3cb3c2/src/libxrpl/basics/Number.cpp
curl -sSfLo stubs/xrpl/basics/Number.h https://raw.githubusercontent.com/XRPLF/rippled/00606bec1a41454a7d7741c59c447bdd8b3cb3c2/include/xrpl/basics/Number.h
docker build -t xrplcsharp-numgen .
docker run --rm -v "$PWD:/src" xrplcsharp-numgen sh -c 'g++ -std=c++23 -O2 -Istubs gen.cpp Number.cpp -o /tmp/gen && /tmp/gen 100' | gzip -9 > vectors.txt.gz
rm Number.cpp stubs/xrpl/basics/Number.h
```

On Git Bash for Windows, set `MSYS_NO_PATHCONV=1` so the `/src` mount is not rewritten.

`gen.cpp` seeds `std::mt19937_64` with a constant, so the same commit gives the same file. When the vectors are moved to a newer rippled commit, a changed line is a change in rippled's rounding, and the port has to follow it.
