// Golden vectors for XrplCSharp's port of rippled's Number, produced by rippled's own Number.cpp.
//
// Line format (space separated):
//   <scale> <mode> <op> <operands...> = <result>
// An operand or result is "<mantissa>e<exponent>" in the external (wire) form of
// Number::mantissa()/exponent(), "Z" for zero, or "!<what>" for an exception.
// Integer operands (powers, roots, int64 results) are plain decimal numbers.

#include <xrpl/basics/Number.h>

#include <cstdint>
#include <cstdio>
#include <exception>
#include <functional>
#include <random>
#include <stdexcept>
#include <string>

using namespace xrpl;

static std::mt19937_64 rng(20260924);

static std::string fmt(Number const& n)
{
    if (n == Number{})
        return "Z";
    return std::to_string(n.mantissa()) + "e" + std::to_string(n.exponent());
}

template <class F>
static std::string guarded(F f)
{
    try
    {
        return f();
    }
    catch (std::overflow_error const&)
    {
        return "!overflow";
    }
    catch (std::exception const&)
    {
        return "!error";
    }
}

static std::uint64_t pow10u(int n)
{
    std::uint64_t r = 1;
    while (n-- > 0)
        r *= 10;
    return r;
}

// A signed mantissa of 1..19 digits, biased toward the interesting edges.
static std::int64_t randomMantissa()
{
    std::int64_t const kMax = std::numeric_limits<std::int64_t>::max();
    std::uint64_t m = 0;
    switch (rng() % 10)
    {
        case 0:  // near 2^63 - 1
            m = static_cast<std::uint64_t>(kMax) - (rng() % 1000);
            break;
        case 1:  // near a power of ten
        {
            int const d = 1 + static_cast<int>(rng() % 18);
            std::int64_t const delta = static_cast<std::int64_t>(rng() % 21) - 10;
            m = static_cast<std::uint64_t>(static_cast<std::int64_t>(pow10u(d)) + delta);
            break;
        }
        case 2:  // few significant digits followed by zeros
            m = (1 + rng() % 999) * pow10u(static_cast<int>(rng() % 16));
            break;
        default:
        {
            int const digits = 1 + static_cast<int>(rng() % 19);
            m = rng() % pow10u(digits > 18 ? 18 : digits);
            if (digits == 19)
                m = m * 9 + rng() % 9;  // up to about 9e18
            if (m == 0)
                m = 1;
        }
    }
    if (m > static_cast<std::uint64_t>(kMax))
        m = static_cast<std::uint64_t>(kMax);
    std::int64_t const signedM = static_cast<std::int64_t>(m);
    return (rng() % 3 == 0) ? -signedM : signedM;
}

static int randomExponent()
{
    switch (rng() % 12)
    {
        case 0:
            return -32768 + static_cast<int>(rng() % 60);
        case 1:
            return 32768 - 60 + static_cast<int>(rng() % 60);
        default:
            return static_cast<int>(rng() % 81) - 40;
    }
}

static Number randomNumber()
{
    if (rng() % 25 == 0)
        return Number{};
    for (;;)
    {
        try
        {
            return Number{randomMantissa(), randomExponent()};
        }
        catch (std::exception const&)
        {
        }
    }
}

// Moderate magnitudes for power and root, so the results stay in range.
static Number randomModerate(bool nonNegative)
{
    for (;;)
    {
        try
        {
            std::int64_t m = randomMantissa();
            if (nonNegative && m < 0)
                m = -m;
            int const digits = static_cast<int>(std::to_string(m < 0 ? -m : m).size());
            int const e = static_cast<int>(rng() % 9) - 4 - digits + 1;
            return Number{m, e};
        }
        catch (std::exception const&)
        {
        }
    }
}

int main(int argc, char** argv)
{
    int const perCombination = argc > 1 ? std::stoi(argv[1]) : 100;

    MantissaRange::MantissaScale const scales[] = {
        MantissaRange::MantissaScale::Small,
        MantissaRange::MantissaScale::LargeLegacy,
        MantissaRange::MantissaScale::Large320,
        MantissaRange::MantissaScale::Large330,
    };
    char const* const scaleNames[] = {"Small", "LargeLegacy", "Large320", "Large330"};
    Number::RoundingMode const modes[] = {
        Number::RoundingMode::ToNearest,
        Number::RoundingMode::TowardsZero,
        Number::RoundingMode::Downward,
        Number::RoundingMode::Upward,
    };
    char const* const modeNames[] = {"ToNearest", "TowardsZero", "Downward", "Upward"};

    for (int s = 0; s < 4; ++s)
    {
        Number::setMantissaScale(scales[s]);
        for (int r = 0; r < 4; ++r)
        {
            Number::setround(modes[r]);
            auto line = [&](std::string const& body) {
                std::printf("%s %s %s\n", scaleNames[s], modeNames[r], body.c_str());
            };

            for (int i = 0; i < perCombination; ++i)
            {
                // Construction from a wire pair: normalization with the current scale and mode.
                std::int64_t const m = randomMantissa();
                int const e = randomExponent();
                line("new " + std::to_string(m) + "e" + std::to_string(e) + " = " +
                     guarded([&] { return fmt(Number{m, e}); }));

                // Construction from an internal mantissa up to 10^19 - 1.
                std::uint64_t const um = rng() % 10'000'000'000'000'000'000ULL;
                bool const neg = rng() % 2 == 0;
                line("newi " + std::string(neg ? "-" : "") + std::to_string(um) + "e" + std::to_string(e) + " = " +
                     guarded([&] { return fmt(Number{neg, um, e, Number::Normalized{}}); }));

                Number const x = randomNumber();
                Number y = randomNumber();
                if (rng() % 4 == 0 && x != Number{})
                {
                    // A neighbour of x, where addition and subtraction cancel.
                    try
                    {
                        y = Number{x.mantissa() + static_cast<std::int64_t>(rng() % 5) - 2, x.exponent() - static_cast<int>(rng() % 3)};
                    }
                    catch (std::exception const&)
                    {
                    }
                }

                line("add " + fmt(x) + " " + fmt(y) + " = " + guarded([&] { return fmt(x + y); }));
                line("sub " + fmt(x) + " " + fmt(y) + " = " + guarded([&] { return fmt(x - y); }));
                line("mul " + fmt(x) + " " + fmt(y) + " = " + guarded([&] { return fmt(x * y); }));
                if (y != Number{})
                    line("div " + fmt(x) + " " + fmt(y) + " = " + guarded([&] { return fmt(x / y); }));

                Number const z = rng() % 2 == 0 ? randomModerate(false) : x;
                line("int " + fmt(z) + " = " + guarded([&] {
                         return std::to_string(static_cast<std::int64_t>(z));
                     }));
                line("trunc " + fmt(z) + " = " + guarded([&] { return fmt(z.truncate()); }));

                Number const b = randomModerate(false);
                unsigned const n = static_cast<unsigned>(rng() % 25);
                line("pow " + fmt(b) + " " + std::to_string(n) + " = " + guarded([&] { return fmt(power(b, n)); }));

                Number const q = randomModerate(true);
                line("root2 " + fmt(q) + " = " + guarded([&] { return fmt(root2(q)); }));
                unsigned const d = 1 + static_cast<unsigned>(rng() % 5);
                Number const rootBase = (d % 2 == 1 && rng() % 2 == 0) ? -q : q;
                line("root " + fmt(rootBase) + " " + std::to_string(d) + " = " +
                     guarded([&] { return fmt(root(rootBase, d)); }));
                unsigned const pn = static_cast<unsigned>(rng() % 7);
                unsigned const pd = 1 + static_cast<unsigned>(rng() % 4);
                line("powf " + fmt(q) + " " + std::to_string(pn) + " " + std::to_string(pd) + " = " +
                     guarded([&] { return fmt(power(q, pn, pd)); }));
            }
        }
    }
    return 0;
}
