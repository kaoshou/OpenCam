// SPDX-License-Identifier: AGPL-3.0-or-later
#include "../../native/obs-linux-probe/portal-grant.hpp"
#include <cassert>
#include <functional>
#include <iostream>
using opencam::PortalGrant;
static void rejects(const char *code, std::function<void()> fn) {
    try { fn(); assert(false); } catch (const std::runtime_error &e) { assert(std::string(e.what()) == code); }
}
int main() {
    PortalGrant grant;
    assert(!grant.selected());
    rejects("portal_identity_unavailable", [&] { grant.accept(3, 1, "opaque-A", 42, 1, "token-A"); });
    rejects("portal_identity_unavailable", [&] { grant.accept(4, 2, "opaque-A", 42, 1, "token-A"); });
    rejects("portal_identity_unavailable", [&] { grant.accept(4, 1, "", 42, 1, "token-A"); });
    rejects("portal_identity_unavailable", [&] { grant.accept(4, 1, "opaque-A", 0, 1, "token-A"); });
    rejects("portal_identity_unavailable", [&] { grant.accept(4, 1, "opaque-A", 42, 2, "token-A"); });
    rejects("portal_restore_unavailable", [&] { grant.accept(4, 1, "opaque-A", 42, 1, ""); });
    assert(!grant.selected());
    grant.accept(4, 1, "opaque-A", 42, 1, "token-A");
    assert(grant.selected());
    grant.requireIdentity("opaque-A");
    rejects("portal_source_changed", [&] { grant.requireIdentity("opaque-B"); });
    rejects("portal_identity_unavailable", [&] { grant.accept(4, 1, "opaque-A", 42, 1, "token-B"); });
    assert(grant.restoreToken() == "token-A");
    rejects("portal_identity_unavailable", [&] { PortalGrant bad; bad.accept(4, 1, "opaque\nA", 42, 1, "secret"); });
    rejects("portal_restore_unavailable", [&] { PortalGrant bad; bad.accept(4, 1, "opaque-A", 42, 1, std::string(8192, 'x')); });
    std::cout << "PASS: portal identity, one-monitor scope, bounded private grant, mismatch and duplicate rejection\n";
}
