#pragma once
#include <cassert>
#define XRPL_ASSERT(cond, ...) assert(cond)
#define XRPL_ASSERT_PARTS(cond, ...) assert(cond)
#define UNREACHABLE(...) assert(false)
