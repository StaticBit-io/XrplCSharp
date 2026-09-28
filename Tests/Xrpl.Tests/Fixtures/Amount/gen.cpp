// Golden vectors for XrplCSharp's port of rippled's STAmount arithmetic, Quality and mulRatio.
//
// The functions below are copied from rippled 3.4.0 (STAmount.cpp, Quality.cpp, IOUAmount.cpp,
// XRPAmount.h, MPTAmount.h) with one change: STAmount is replaced by `Amt`, a struct holding the
// same four fields (asset kind, value_, offset_, isNegative_) and the same canonicalize(). The
// Number arithmetic is rippled's own Number.cpp, unmodified.
//
// Line format (space separated):
//   <scale> <mode> <mptv2> <op> <operands...> = <result>
// An amount is "<kind>:<value_>e<offset_>" with a leading '-' on the value when negative, the raw
// STAmount fields; kind is X (XRP), I (issued currency, or a unitless rate) or M (MPT). A quality
// is its 64-bit encoding in decimal. "!error" is any exception.

#include <xrpl/basics/Number.h>

#include <boost/multiprecision/cpp_int.hpp>

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <exception>
#include <limits>
#include <random>
#include <stdexcept>
#include <string>
#include <tuple>
#include <utility>
#include <vector>

using namespace xrpl;

enum class Kind { X, I, M };

static bool gMPTokensV2 = false;

static constexpr int kMinOffset = -96;
static constexpr int kMaxOffset = 80;
static constexpr std::uint64_t kMinValue = 1'000'000'000'000'000ull;
static constexpr std::uint64_t kMaxValue = (kMinValue * 10) - 1;
static constexpr std::uint64_t kMaxNativeN = 100'000'000'000'000'000ull;
static constexpr std::uint64_t kMaxMpTokenAmount = 0x7FFF'FFFF'FFFF'FFFFull;

// ---------------------------------------------------------------------------- IOUAmount.cpp

struct IOUAmount
{
    std::int64_t mantissa_ = 0;
    int exponent_ = -100;

    IOUAmount() = default;

    IOUAmount(std::int64_t mantissa, int exponent) : mantissa_(mantissa), exponent_(exponent)
    {
        normalize();
    }

    explicit IOUAmount(Number const& other)
    {
        std::tie(mantissa_, exponent_) =
            other.normalizeToRange<static_cast<std::int64_t>(kMinValue), static_cast<std::int64_t>(kMaxValue)>();
        if (exponent_ > kMaxOffset)
            throw std::overflow_error("value overflow");
        if (exponent_ < kMinOffset)
        {
            mantissa_ = 0;
            exponent_ = -100;
        }
    }

    void
    normalize()
    {
        if (mantissa_ == 0)
        {
            exponent_ = -100;
            return;
        }

        Number const v{mantissa_, exponent_};
        *this = IOUAmount(v);
    }

    operator Number() const
    {
        return Number{mantissa_, exponent_};
    }
};

// ---------------------------------------------------------------------------- STAmount

struct Amt
{
    Kind kind;
    std::uint64_t value_ = 0;
    int offset_ = 0;
    bool isNegative_ = false;

    explicit Amt(Kind k) : kind(k), offset_(k == Kind::I ? -100 : 0)
    {
    }

    Amt(Kind k, std::uint64_t mantissa, int exponent, bool negative)
        : kind(k), value_(mantissa), offset_(exponent), isNegative_(negative)
    {
        canonicalize();
    }

    bool native() const { return kind == Kind::X; }
    bool isMpt() const { return kind == Kind::M; }
    bool integral() const { return kind != Kind::I; }
    std::uint64_t mantissa() const { return value_; }
    int exponent() const { return offset_; }
    bool negative() const { return isNegative_; }
    bool isZero() const { return value_ == 0; }

    IOUAmount
    iou() const
    {
        auto mantissa = static_cast<std::int64_t>(value_);
        auto exponent = offset_;
        if (isNegative_)
            mantissa = -mantissa;
        return {mantissa, exponent};
    }

    void
    assign(IOUAmount const& iou)
    {
        offset_ = iou.exponent_;
        isNegative_ = iou.mantissa_ < 0;
        value_ = static_cast<std::uint64_t>(isNegative_ ? -iou.mantissa_ : iou.mantissa_);
    }

    void
    canonicalize()
    {
        if (integral())
        {
            if (value_ == 0 || offset_ <= -20)
            {
                value_ = 0;
                offset_ = 0;
                isNegative_ = false;
                return;
            }

            if (native() && offset_ > 17)
                throw std::runtime_error("Native currency amount out of range");
            if (isMpt() && offset_ > 18)
                throw std::runtime_error("MPT amount out of range");

            Number const num(isNegative_, value_, offset_, Number::Unchecked{});
            // XRPAmount{num} and MPTAmount{num} are both static_cast<value_type>(num).
            auto const value = static_cast<std::int64_t>(num);
            isNegative_ = value < 0;
            value_ = isNegative_ ? -value : value;
            offset_ = 0;

            if (native() && value_ > kMaxNativeN)
                throw std::runtime_error("Native currency amount out of range");
            else if (!native() && value_ > kMaxMpTokenAmount)
                throw std::runtime_error("MPT amount out of range");

            return;
        }

        assign(iou());
    }

    operator Number() const
    {
        if (integral())
        {
            auto const v = static_cast<std::int64_t>(value_);
            return Number{isNegative_ ? -v : v};
        }
        return Number(iou());
    }
};

static std::int64_t
getSNValue(Amt const& amount)
{
    auto const v = static_cast<std::int64_t>(amount.value_);
    return amount.isNegative_ ? -v : v;
}

static bool
operator<(Amt const& lhs, Amt const& rhs)
{
    return Number(lhs) < Number(rhs);
}

static bool
operator>(Amt const& lhs, Amt const& rhs)
{
    return rhs < lhs;
}

static Amt
fromNumber(Kind asset, Number const& number)
{
    bool const negative = number.mantissa() < 0;
    Number const working{negative ? -number : number};
    if (asset != Kind::I)
    {
        std::uint64_t const intValue = static_cast<std::int64_t>(working);
        return Amt{asset, intValue, 0, negative};
    }

    auto const [mantissa, exponent] = working.normalizeToRange<kMinValue, kMaxValue>();
    return Amt{asset, mantissa, exponent, negative};
}

static Amt
add(Amt const& v1, Amt const& v2)
{
    if (v2.isZero())
        return v1;
    if (v1.isZero())
        return Amt{v1.kind, v2.mantissa(), v2.exponent(), v2.negative()};

    if (v1.integral())
    {
        std::int64_t const sum = getSNValue(v1) + getSNValue(v2);
        return Amt{v1.kind, static_cast<std::uint64_t>(sum < 0 ? -sum : sum), 0, sum < 0};
    }

    Amt x = v1;
    x.assign(IOUAmount{Number{v1.iou()} + Number{v2.iou()}});
    return x;
}

static std::uint64_t const kTenTO14 = 100000000000000ull;
static std::uint64_t const kTenTO14M1 = kTenTO14 - 1;
static std::uint64_t const kTenTO17 = kTenTO14 * 1000;

static std::uint64_t
muldiv(std::uint64_t multiplier, std::uint64_t multiplicand, std::uint64_t divisor)
{
    boost::multiprecision::uint128_t ret;
    boost::multiprecision::multiply(ret, multiplier, multiplicand);
    ret /= divisor;
    if (ret > std::numeric_limits<std::uint64_t>::max())
        throw std::overflow_error("overflow");
    return static_cast<uint64_t>(ret);
}

static std::uint64_t
muldivRound(std::uint64_t multiplier, std::uint64_t multiplicand, std::uint64_t divisor, std::uint64_t rounding)
{
    boost::multiprecision::uint128_t ret;
    boost::multiprecision::multiply(ret, multiplier, multiplicand);
    ret += rounding;
    ret /= divisor;
    if (ret > std::numeric_limits<std::uint64_t>::max())
        throw std::overflow_error("overflow");
    return static_cast<uint64_t>(ret);
}

static Amt
divide(Amt const& num, Amt const& den, Kind asset)
{
    if (den.isZero())
        throw std::runtime_error("division by zero");
    if (num.isZero())
        return Amt{asset};

    std::uint64_t numVal = num.mantissa();
    std::uint64_t denVal = den.mantissa();
    int numOffset = num.exponent();
    int denOffset = den.exponent();

    if (num.integral())
    {
        while (numVal < kMinValue)
        {
            numVal *= 10;
            --numOffset;
        }
    }

    if (den.integral())
    {
        while (denVal < kMinValue)
        {
            denVal *= 10;
            --denOffset;
        }
    }

    return Amt(asset, muldiv(numVal, kTenTO17, denVal) + 5, numOffset - denOffset - 17, num.negative() != den.negative());
}

static Amt
multiply(Amt const& v1, Amt const& v2, Kind asset)
{
    if (v1.isZero() || v2.isZero())
        return Amt(asset);

    if (v1.native() && v2.native() && asset == Kind::X)
    {
        std::uint64_t const minV = std::min(getSNValue(v1), getSNValue(v2));
        std::uint64_t const maxV = std::max(getSNValue(v1), getSNValue(v2));
        if (minV > 3000000000ull)
            throw std::runtime_error("Native value overflow");
        if (((maxV >> 32) * minV) > 2095475792ull)
            throw std::runtime_error("Native value overflow");
        return Amt(Kind::X, minV * maxV, 0, false);
    }
    if (v1.isMpt() && v2.isMpt() && asset == Kind::M)
    {
        std::uint64_t const minV = std::min(getSNValue(v1), getSNValue(v2));
        std::uint64_t const maxV = std::max(getSNValue(v1), getSNValue(v2));
        if (minV > 3037000499ull)
            throw std::runtime_error("MPT value overflow");
        if (((maxV >> 32) * minV) > 2147483648ull)
            throw std::runtime_error("MPT value overflow");
        return Amt(asset, minV * maxV, 0, false);
    }

    auto const r = Number{v1} * Number{v2};
    return fromNumber(asset, r);
}

static void
canonicalizeRound(bool integral, std::uint64_t& value, int& offset, bool)
{
    if (integral)
    {
        if (offset < 0)
        {
            int loops = 0;
            while (offset < -1)
            {
                value /= 10;
                ++offset;
                ++loops;
            }
            value += (loops >= 2) ? 9 : 10;
            value /= 10;
            ++offset;
        }
    }
    else if (value > kMaxValue)
    {
        while (value > (10 * kMaxValue))
        {
            value /= 10;
            ++offset;
        }
        value += 9;
        value /= 10;
        ++offset;
    }
}

static void
canonicalizeRoundStrict(bool integral, std::uint64_t& value, int& offset, bool roundUp)
{
    if (integral)
    {
        if (offset < 0)
        {
            bool hadRemainder = false;
            while (offset < -1)
            {
                std::uint64_t const newValue = value / 10;
                hadRemainder |= (value != (newValue * 10));
                value = newValue;
                ++offset;
            }
            value += (hadRemainder && roundUp) ? 10 : 9;
            value /= 10;
            ++offset;
        }
    }
    else if (value > kMaxValue)
    {
        while (value > (10 * kMaxValue))
        {
            value /= 10;
            ++offset;
        }
        value += 9;
        value /= 10;
        ++offset;
    }
}

namespace {

class DontAffectNumberRoundMode
{
public:
    explicit DontAffectNumberRoundMode(Number::RoundingMode) noexcept
    {
    }
};

Number::RoundingMode
roundMode(bool const resultNegative, bool const roundUp)
{
    using enum Number::RoundingMode;
    return roundUp ^ resultNegative ? Upward : Downward;
}

Amt
roundNumberResult(Kind asset, bool const resultNegative, bool const roundUp, Number const& number)
{
    NumberRoundModeGuard const finalRound(roundMode(resultNegative, roundUp));
    auto result = fromNumber(asset, number);
    if (roundUp && !resultNegative && result.isZero())
    {
        if (asset != Kind::I)
            return Amt{asset, 1, 0, false};
        return Amt{asset, kMinValue, kMinOffset, false};
    }
    return result;
}

}  // namespace

template <void (*CanonicalizeFunc)(bool, std::uint64_t&, int&, bool), typename MightSaveRound>
static Amt
mulRoundImpl(Amt const& v1, Amt const& v2, Kind asset, bool roundUp)
{
    if (v1.isZero() || v2.isZero())
        return Amt{asset};

    if (v1.native() && v2.native() && asset == Kind::X)
    {
        std::uint64_t const minV = std::min(getSNValue(v1), getSNValue(v2));
        std::uint64_t const maxV = std::max(getSNValue(v1), getSNValue(v2));
        if (minV > 3000000000ull)
            throw std::runtime_error("Native value overflow");
        if (((maxV >> 32) * minV) > 2095475792ull)
            throw std::runtime_error("Native value overflow");
        return Amt(Kind::X, minV * maxV, 0, false);
    }

    if (v1.isMpt() && v2.isMpt() && asset == Kind::M)
    {
        std::uint64_t const minV = std::min(getSNValue(v1), getSNValue(v2));
        std::uint64_t const maxV = std::max(getSNValue(v1), getSNValue(v2));
        if (minV > 3037000499ull)
            throw std::runtime_error("MPT value overflow");
        if (((maxV >> 32) * minV) > 2147483648ull)
            throw std::runtime_error("MPT value overflow");
        return Amt(asset, minV * maxV, 0, false);
    }

    bool const resultNegative = v1.negative() != v2.negative();

    if (asset == Kind::M && gMPTokensV2)
    {
        Number result;
        {
            NumberRoundModeGuard const operationRound(roundMode(resultNegative, roundUp));
            result = Number{v1} * Number{v2};
        }
        return roundNumberResult(asset, resultNegative, roundUp, result);
    }

    std::uint64_t value1 = v1.mantissa(), value2 = v2.mantissa();
    int offset1 = v1.exponent(), offset2 = v2.exponent();

    if (v1.integral())
    {
        while (value1 < kMinValue)
        {
            value1 *= 10;
            --offset1;
        }
    }

    if (v2.integral())
    {
        while (value2 < kMinValue)
        {
            value2 *= 10;
            --offset2;
        }
    }

    std::uint64_t amount = muldivRound(value1, value2, kTenTO14, (resultNegative != roundUp) ? kTenTO14M1 : 0);
    int offset = offset1 + offset2 + 14;
    if (resultNegative != roundUp)
        CanonicalizeFunc(asset != Kind::I, amount, offset, roundUp);

    Amt result = [&]() {
        MightSaveRound const savedRound(Number::RoundingMode::TowardsZero);
        return Amt(asset, amount, offset, resultNegative);
    }();

    if (roundUp && !resultNegative && result.isZero())
    {
        if (asset != Kind::I)
        {
            amount = 1;
            offset = 0;
        }
        else
        {
            amount = kMinValue;
            offset = kMinOffset;
        }
        return Amt(asset, amount, offset, resultNegative);
    }
    return result;
}

static Amt
mulRound(Amt const& v1, Amt const& v2, Kind asset, bool roundUp)
{
    return mulRoundImpl<canonicalizeRound, DontAffectNumberRoundMode>(v1, v2, asset, roundUp);
}

static Amt
mulRoundStrict(Amt const& v1, Amt const& v2, Kind asset, bool roundUp)
{
    return mulRoundImpl<canonicalizeRoundStrict, NumberRoundModeGuard>(v1, v2, asset, roundUp);
}

template <typename MightSaveRound>
static Amt
divRoundImpl(Amt const& num, Amt const& den, Kind asset, bool roundUp)
{
    if (den.isZero())
        throw std::runtime_error("division by zero");

    if (num.isZero())
        return Amt{asset};

    bool const resultNegative = (num.negative() != den.negative());

    if (asset == Kind::M && gMPTokensV2)
    {
        Number result;
        {
            NumberRoundModeGuard const operationRound(roundMode(resultNegative, roundUp));
            result = Number{num} / Number{den};
        }
        return roundNumberResult(asset, resultNegative, roundUp, result);
    }

    std::uint64_t numVal = num.mantissa(), denVal = den.mantissa();
    int numOffset = num.exponent(), denOffset = den.exponent();

    if (num.integral())
    {
        while (numVal < kMinValue)
        {
            numVal *= 10;
            --numOffset;
        }
    }

    if (den.integral())
    {
        while (denVal < kMinValue)
        {
            denVal *= 10;
            --denOffset;
        }
    }

    std::uint64_t amount = muldivRound(numVal, kTenTO17, denVal, (resultNegative != roundUp) ? denVal - 1 : 0);
    int offset = numOffset - denOffset - 17;

    if (resultNegative != roundUp)
        canonicalizeRound(asset != Kind::I, amount, offset, roundUp);

    Amt result = [&]() {
        using enum Number::RoundingMode;
        MightSaveRound const savedRound(roundUp ^ resultNegative ? Upward : Downward);
        return Amt(asset, amount, offset, resultNegative);
    }();

    if (roundUp && !resultNegative && result.isZero())
    {
        if (asset != Kind::I)
        {
            amount = 1;
            offset = 0;
        }
        else
        {
            amount = kMinValue;
            offset = kMinOffset;
        }
        return Amt(asset, amount, offset, resultNegative);
    }
    return result;
}

static Amt
divRound(Amt const& num, Amt const& den, Kind asset, bool roundUp)
{
    return divRoundImpl<DontAffectNumberRoundMode>(num, den, asset, roundUp);
}

static Amt
divRoundStrict(Amt const& num, Amt const& den, Kind asset, bool roundUp)
{
    return divRoundImpl<NumberRoundModeGuard>(num, den, asset, roundUp);
}

// ---------------------------------------------------------------------------- Quality

static std::uint64_t
getRate(Amt const& offerOut, Amt const& offerIn)
{
    if (offerOut.isZero())
        return 0;
    try
    {
        Amt const r = divide(offerIn, offerOut, Kind::I);
        if (r.isZero())
            return 0;
        std::uint64_t const ret = r.exponent() + 100;
        return (ret << (64 - 8)) | r.mantissa();
    }
    catch (...)
    {
        return 0;
    }
}

static Amt
amountFromQuality(std::uint64_t rate)
{
    if (rate == 0)
        return Amt(Kind::I);
    std::uint64_t const mantissa = rate & ~(255ull << (64 - 8));
    int const exponent = static_cast<int>(rate >> (64 - 8)) - 100;
    return Amt(Kind::I, mantissa, exponent, false);
}

using Amounts = std::pair<Amt, Amt>;

template <Amt (*DivRoundFunc)(Amt const&, Amt const&, Kind, bool)>
static Amounts
ceilInImpl(Amounts const& amount, Amt const& limit, bool roundUp, std::uint64_t quality)
{
    if (amount.first > limit)
    {
        Amounts result(limit, DivRoundFunc(limit, amountFromQuality(quality), amount.second.kind, roundUp));
        if (result.second > amount.second)
            result.second = amount.second;
        return result;
    }
    return amount;
}

template <Amt (*MulRoundFunc)(Amt const&, Amt const&, Kind, bool)>
static Amounts
ceilOutImpl(Amounts const& amount, Amt const& limit, bool roundUp, std::uint64_t quality)
{
    if (amount.second > limit)
    {
        Amounts result(MulRoundFunc(limit, amountFromQuality(quality), amount.first.kind, roundUp), limit);
        if (result.first > amount.first)
            result.first = amount.first;
        return result;
    }
    return amount;
}

// ---------------------------------------------------------------------------- mulRatio

static Amt
mulRatioIou(Amt const& a, std::uint32_t num, std::uint32_t den, bool roundUp)
{
    using namespace boost::multiprecision;

    if (den == 0u)
        throw std::runtime_error("division by zero");

    static auto const kPowerTable = [] {
        std::vector<uint128_t> result;
        result.reserve(30);
        uint128_t cur(1);
        for (int i = 0; i < 30; ++i)
        {
            result.push_back(cur);
            cur *= 10;
        };
        return result;
    }();

    static auto kLoG10Floor = [](uint128_t const& v) {
        auto const l = std::ranges::lower_bound(kPowerTable, v);
        int index = std::distance(kPowerTable.begin(), l);
        if (*l != v)
            --index;
        return index;
    };

    static auto kLoG10Ceil = [](uint128_t const& v) {
        auto const l = std::ranges::lower_bound(kPowerTable, v);
        return int(std::distance(kPowerTable.begin(), l));
    };

    static auto const kFl64 = kLoG10Floor(std::numeric_limits<std::int64_t>::max());

    IOUAmount const amt = a.iou();
    bool const neg = amt.mantissa_ < 0;
    uint128_t const den128(den);
    uint128_t const mul = uint128_t(neg ? -amt.mantissa_ : amt.mantissa_) * uint128_t(num);

    auto low = mul / den128;
    uint128_t rem(mul - low * den128);

    int exponent = amt.exponent_;

    if (rem)
    {
        auto const roomToGrow = kFl64 - kLoG10Ceil(low);
        if (roomToGrow > 0)
        {
            exponent -= roomToGrow;
            low *= kPowerTable[roomToGrow];
            rem *= kPowerTable[roomToGrow];
        }
        auto const addRem = rem / den128;
        low += addRem;
        rem = rem - addRem * den128;
    }

    bool hasRem = bool(rem);
    auto const mustShrink = kLoG10Ceil(low) - kFl64;
    if (mustShrink > 0)
    {
        uint128_t const sav(low);
        exponent += mustShrink;
        low /= kPowerTable[mustShrink];
        if (!hasRem)
            hasRem = bool(sav - low * kPowerTable[mustShrink]);
    }

    auto mantissa = low.convert_to<std::int64_t>();
    if (neg)
        mantissa *= -1;

    IOUAmount result(mantissa, exponent);

    if (hasRem)
    {
        if (roundUp && !neg)
        {
            if (result.mantissa_ == 0)
                result = IOUAmount(static_cast<std::int64_t>(kMinValue), kMinOffset);
            else
                result = IOUAmount(result.mantissa_ + 1, result.exponent_);
        }
        else if (!roundUp && neg)
        {
            if (result.mantissa_ == 0)
                result = IOUAmount(-static_cast<std::int64_t>(kMinValue), kMinOffset);
            else
                result = IOUAmount(result.mantissa_ - 1, result.exponent_);
        }
    }

    Amt out(Kind::I);
    out.assign(result);
    return out;
}

static Amt
mulRatioIntegral(Amt const& a, std::uint32_t num, std::uint32_t den, bool roundUp)
{
    using namespace boost::multiprecision;

    if (den == 0u)
        throw std::runtime_error("division by zero");

    int128_t const amt128(getSNValue(a));
    auto const neg = getSNValue(a) < 0;
    auto const m = amt128 * num;
    auto r = m / den;
    if (m % den)
    {
        if (!neg && roundUp)
            r += 1;
        if (neg && !roundUp)
            r -= 1;
    }
    if (r > std::numeric_limits<std::int64_t>::max())
        throw std::overflow_error("mulRatio overflow");
    std::int64_t const v = r.convert_to<std::int64_t>();
    return Amt(a.kind, static_cast<std::uint64_t>(v < 0 ? -v : v), 0, v < 0);
}

// ---------------------------------------------------------------------------- generator

static std::mt19937_64 rng(20260927);

static std::string
fmt(Amt const& a)
{
    char k = a.kind == Kind::X ? 'X' : a.kind == Kind::I ? 'I' : 'M';
    std::string s(1, k);
    s += ':';
    if (a.isNegative_ && a.value_ != 0)
        s += '-';
    s += std::to_string(a.value_) + "e" + std::to_string(a.offset_);
    return s;
}

static char
kindChar(Kind k)
{
    return k == Kind::X ? 'X' : k == Kind::I ? 'I' : 'M';
}

static std::uint64_t
logUniform(std::uint64_t max)
{
    int digits = 1 + static_cast<int>(rng() % 19);
    std::uint64_t cap = 1;
    for (int i = 0; i < digits && cap <= max / 10; ++i)
        cap *= 10;
    std::uint64_t v = 1 + rng() % cap;
    return std::min(v, max);
}

static Amt
randomAmount(Kind kind, bool allowNegative)
{
    bool const negative = allowNegative && rng() % 4 == 0;
    if (rng() % 40 == 0)
        return Amt(kind);

    switch (kind)
    {
        case Kind::X:
            return Amt(Kind::X, logUniform(kMaxNativeN), 0, negative);
        case Kind::M:
            return Amt(Kind::M, logUniform(kMaxMpTokenAmount), 0, negative);
        default: {
            std::uint64_t mantissa;
            switch (rng() % 6)
            {
                case 0: mantissa = kMinValue; break;
                case 1: mantissa = kMaxValue; break;
                default: mantissa = kMinValue + rng() % (kMaxValue - kMinValue + 1); break;
            }
            int exponent = rng() % 3 == 0 ? kMinOffset + static_cast<int>(rng() % (kMaxOffset - kMinOffset + 1))
                                          : -30 + static_cast<int>(rng() % 30);
            return Amt(Kind::I, mantissa, exponent, negative);
        }
    }
}

static Kind
randomKind()
{
    switch (rng() % 3)
    {
        case 0: return Kind::X;
        case 1: return Kind::I;
        default: return Kind::M;
    }
}

template <class F>
static std::string
guarded(F f)
{
    try
    {
        return f();
    }
    catch (std::exception const&)
    {
        return "!error";
    }
}

static char const* kScaleNames[] = {"Small", "LargeLegacy", "Large320", "Large330"};
static MantissaRange::MantissaScale const kScales[] = {
    MantissaRange::MantissaScale::Small,
    MantissaRange::MantissaScale::LargeLegacy,
    MantissaRange::MantissaScale::Large320,
    MantissaRange::MantissaScale::Large330};
static char const* kModeNames[] = {"ToNearest", "TowardsZero", "Downward", "Upward"};
static Number::RoundingMode const kModes[] = {
    Number::RoundingMode::ToNearest,
    Number::RoundingMode::TowardsZero,
    Number::RoundingMode::Downward,
    Number::RoundingMode::Upward};

int
main(int argc, char** argv)
{
    int const perCase = argc > 1 ? std::atoi(argv[1]) : 40;

    for (int s = 0; s < 4; ++s)
    {
        for (int m = 0; m < 4; ++m)
        {
            Number::setMantissaScale(kScales[s]);
            Number::setround(kModes[m]);

            auto line = [&](std::string const& body) {
                std::printf("%s %s %d %s\n", kScaleNames[s], kModeNames[m], gMPTokensV2 ? 1 : 0, body.c_str());
            };

            for (int i = 0; i < perCase; ++i)
            {
                gMPTokensV2 = rng() % 2 == 0;

                // mulRound / divRound and their strict forms, over every combination of kinds.
                Amt const a = randomAmount(randomKind(), true);
                Amt const b = randomAmount(randomKind(), true);
                Kind const target = randomKind();
                bool const up = rng() % 2 == 0;
                std::string const args = fmt(a) + " " + fmt(b) + " " + kindChar(target) + " " + (up ? "1" : "0");
                line("mulRound " + args + " = " + guarded([&] { return fmt(mulRound(a, b, target, up)); }));
                line("mulRoundStrict " + args + " = " + guarded([&] { return fmt(mulRoundStrict(a, b, target, up)); }));
                line("divRound " + args + " = " + guarded([&] { return fmt(divRound(a, b, target, up)); }));
                line("divRoundStrict " + args + " = " + guarded([&] { return fmt(divRoundStrict(a, b, target, up)); }));

                std::string const pair = fmt(a) + " " + fmt(b) + " " + kindChar(target);
                line("divide " + pair + " = " + guarded([&] { return fmt(divide(a, b, target)); }));
                line("multiply " + pair + " = " + guarded([&] { return fmt(multiply(a, b, target)); }));

                // Same-kind addition.
                Amt const c = randomAmount(a.kind, true);
                line("add " + fmt(a) + " " + fmt(c) + " = " + guarded([&] { return fmt(add(a, c)); }));

                // fromNumber of a Number with up to 19 digits.
                Kind const nk = randomKind();
                std::int64_t const nm = static_cast<std::int64_t>(logUniform(std::numeric_limits<std::int64_t>::max())) *
                    (rng() % 3 == 0 ? -1 : 1);
                int const ne = -30 + static_cast<int>(rng() % 40);
                line(
                    std::string("fromNumber ") + kindChar(nk) + " " + std::to_string(nm) + " " + std::to_string(ne) + " = " +
                    guarded([&] { return fmt(fromNumber(nk, Number{nm, ne})); }));

                // Quality: an offer's rate, and the four ceil functions against a limit.
                Kind const inKind = randomKind();
                Kind const outKind = randomKind();
                Amt const in = randomAmount(inKind, false);
                Amt const out = randomAmount(outKind, false);
                std::uint64_t const q = getRate(out, in);
                line("getRate " + fmt(out) + " " + fmt(in) + " = " + std::to_string(q));

                if (q != 0 && !in.isZero() && !out.isZero())
                {
                    Amt const limitIn = randomAmount(inKind, false);
                    Amt const limitOut = randomAmount(outKind, false);
                    bool const ru = rng() % 2 == 0;
                    std::string const qa = std::to_string(q) + " " + fmt(in) + " " + fmt(out);
                    auto pairText = [](Amounts const& r) { return fmt(r.first) + " " + fmt(r.second); };
                    line("ceilIn " + qa + " " + fmt(limitIn) + " 1 = " +
                         guarded([&] { return pairText(ceilInImpl<divRound>({in, out}, limitIn, true, q)); }));
                    line("ceilInStrict " + qa + " " + fmt(limitIn) + " " + (ru ? "1" : "0") + " = " +
                         guarded([&] { return pairText(ceilInImpl<divRoundStrict>({in, out}, limitIn, ru, q)); }));
                    line("ceilOut " + qa + " " + fmt(limitOut) + " 1 = " +
                         guarded([&] { return pairText(ceilOutImpl<mulRound>({in, out}, limitOut, true, q)); }));
                    line("ceilOutStrict " + qa + " " + fmt(limitOut) + " " + (ru ? "1" : "0") + " = " +
                         guarded([&] { return pairText(ceilOutImpl<mulRoundStrict>({in, out}, limitOut, ru, q)); }));
                }

                // mulRatio at a transfer rate, or its inverse, or an arbitrary ratio.
                Amt const r = randomAmount(randomKind(), true);
                std::uint32_t const rate = 1'000'000'000u + static_cast<std::uint32_t>(rng() % 1'000'000'001u);
                std::uint32_t num = 0, den = 0;
                switch (rng() % 3)
                {
                    case 0: num = rate; den = 1'000'000'000u; break;
                    case 1: num = 1'000'000'000u; den = rate; break;
                    default: num = static_cast<std::uint32_t>(rng()); den = 1 + static_cast<std::uint32_t>(rng() % 4'000'000'000u); break;
                }
                bool const ratioUp = rng() % 2 == 0;
                line(
                    "mulRatio " + fmt(r) + " " + std::to_string(num) + " " + std::to_string(den) + " " + (ratioUp ? "1" : "0") +
                    " = " + guarded([&] {
                        return fmt(r.integral() ? mulRatioIntegral(r, num, den, ratioUp) : mulRatioIou(r, num, den, ratioUp));
                    }));
            }
        }
    }

    return 0;
}
