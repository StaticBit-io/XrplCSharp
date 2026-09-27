# Golden vectors for `XrplAmount` arithmetic

`vectors.txt.gz` holds 8,808 results computed by rippled's own code. `TestUAmountVectors` replays every line through the C# port and requires the same result: kind, sign, mantissa and exponent, and which operations throw.

The vectors cover each of the four `Number` mantissa scales (`Small`, `LargeLegacy`, `Large320`, `Large330`) under each of the four rounding modes as the ambient mode, with 40 random cases per combination. Operands mix XRP, issued currencies and MPTs; `MPTokensV2` is on in about half of the lines. The operations are:

- `mulRound`, `mulRoundStrict`, `divRound`, `divRoundStrict`;
- `divide`, `multiply`, `add`, `fromNumber`;
- `getRate`, and `Quality::ceilIn`, `ceilOut`, `ceilInStrict`, `ceilOutStrict`;
- `mulRatio` of `IOUAmount`, `XRPAmount` and `MPTAmount`.

## Source

- rippled tag [`3.4.0`](https://github.com/XRPLF/rippled/tree/3.4.0)
- `src/libxrpl/basics/Number.cpp` and `include/xrpl/basics/Number.h`, unmodified.
- `gen.cpp` carries the functions under test copied from `STAmount.cpp`, `Quality.cpp`, `IOUAmount.cpp`, `XRPAmount.h` and `MPTAmount.h`. The one change is the type: `STAmount` is replaced by `Amt`, a struct with the same four fields and the same `canonicalize()`. Everything else, `STBase` included, is left out.
- `stubs/` replaces the two headers `Number.cpp` pulls in from the rest of rippled: `Throw` and `logicError` throw the named exception, and the assertion macros map to `assert`.

## Line format

```text
<scale> <mode> <mptv2> <op> <operands...> = <result>
```

- An amount is `<kind>:<value_>e<offset_>`, the raw `STAmount` fields, with `-` before the value when negative. The kind is `X` (XRP), `I` (an issued currency, or the unitless rate of `noIssue()`) or `M` (MPT).
- A quality is its 64-bit encoding in decimal.
- `!error` means any exception. The port throws `OverflowException` or `DivideByZeroException`, and the test reads both as `!error`.

## Regenerate

Run these commands from this directory, with Docker available:

```bash
curl -sSfLo Number.cpp https://raw.githubusercontent.com/XRPLF/rippled/3.4.0/src/libxrpl/basics/Number.cpp
curl -sSfLo stubs/xrpl/basics/Number.h https://raw.githubusercontent.com/XRPLF/rippled/3.4.0/include/xrpl/basics/Number.h
docker build -t xrplcsharp-amountgen .
docker run --rm -v "$PWD:/src" xrplcsharp-amountgen sh -c 'g++ -std=c++23 -O2 -Istubs gen.cpp Number.cpp -o /tmp/gen && /tmp/gen 40' | gzip -9 > vectors.txt.gz
rm Number.cpp stubs/xrpl/basics/Number.h
```

On Git Bash for Windows, set `MSYS_NO_PATHCONV=1` so the `/src` mount is not rewritten.

`gen.cpp` seeds `std::mt19937_64` with a constant, so the same tag gives the same lines. When the vectors move to a newer rippled release, any changed line is a change in rippled's rounding, and the port has to follow it. When that release changes a copied function, copy it again from the new tag.
