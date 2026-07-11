#include "DisplayManagerNative.h"
#include "json.hpp"
#include <algorithm>
#include <map>
#include <set>
#include <string>
#include <vector>
#include <windows.h>

using json = nlohmann::json;

#define AISLOP_TU_FRAGMENT
// clang-format off
// Fragment order is load-bearing, not alphabetical: Edid must come first because
// DisplayQuery and ApplyConfiguration both call its helpers (ReadEdidFromRegistry,
// ParseEdidSerial, ...). Within the same translation unit all `namespace { }` blocks
// share one unnamed namespace, so unqualified lookup at a call site only sees names
// declared *earlier* in the TU; if Edid were included after its callers, calls would
// resolve to DisplayManagerNative.internal.h's external-linkage forward declarations
// instead and fail to link. Do not let a formatter's include-sorting reorder these.
#include "DisplayManagerNative.Edid.cpp"
#include "DisplayManagerNative.DisplayQuery.cpp"
#include "DisplayManagerNative.ApplyConfiguration.cpp"
// clang-format on
#undef AISLOP_TU_FRAGMENT
