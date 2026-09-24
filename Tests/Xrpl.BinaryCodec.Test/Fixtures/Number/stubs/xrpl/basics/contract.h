#pragma once
#include <stdexcept>
#include <string>
#include <utility>
namespace xrpl {
template <class E, class... Args>
[[noreturn]] inline void Throw(Args&&... args) { throw E(std::forward<Args>(args)...); }
[[noreturn]] inline void logicError(std::string const& s) { throw std::logic_error(s); }
}
