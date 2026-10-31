// SPDX-License-Identifier: AGPL-3.0-or-later
#include "../../native/obs-linux-probe/portal-responses.hpp"
#include <cassert>
#include <iostream>

static GVariant *response(const char *text) {
    GError *error = nullptr;
    auto *value = g_variant_parse(G_VARIANT_TYPE("(ua{sv})"), text, nullptr, nullptr, &error);
    assert(!error && value);
    return g_variant_ref_sink(value);
}
static void failsStart(const char *text, const char *code) {
    opencam::PortalResponses tracker(4, ":1.42");
    auto *session = response("(uint32 0, {'session_handle': <'/org/freedesktop/portal/desktop/session/1_42/obs1'>})");
    tracker.receive("/org/freedesktop/portal/desktop/request/1_42/obs1", session);
    g_variant_unref(session);
    auto *start = response(text);
    tracker.receive("/org/freedesktop/portal/desktop/request/1_42/obs3", start);
    g_variant_unref(start);
    assert(tracker.failure() == code);
    assert(!tracker.grant().selected());
}
int main() {
    opencam::PortalResponses tracker(4, ":1.42");
    auto *value = response("(uint32 0, {'session_handle': <'/org/freedesktop/portal/desktop/session/1_42/obs1'>})");
    tracker.receive("/org/freedesktop/portal/desktop/request/1_99/obs1", value);
    assert(tracker.session().empty());
    tracker.receive("/org/freedesktop/portal/desktop/request/1_42/obs1", value);
    assert(!tracker.session().empty());
    g_variant_unref(value);
    value = response("(uint32 0, {'streams': <[(uint32 42, {'id': <'opaque-A'>, 'source_type': <uint32 1>})]>, 'restore_token': <'private-token'>})");
    tracker.receive("/org/freedesktop/portal/desktop/request/1_42/obs3", value);
    assert(tracker.grant().selected());
    tracker.grant().requireIdentity("opaque-A");
    assert(tracker.grant().restoreToken() == "private-token");
    tracker.closed("/org/freedesktop/portal/desktop/session/1_99/obs1");
    assert(tracker.failure().empty());
    tracker.closed(tracker.session().c_str());
    assert(tracker.failure() == "portal_session_closed");
    g_variant_unref(value);

    opencam::PortalResponses cancelled(4, ":1.42");
    value = response("(uint32 1, {})");
    cancelled.receive("/org/freedesktop/portal/desktop/request/1_42/obs1", value);
    assert(cancelled.failure() == "portal_cancelled");
    g_variant_unref(value);
    value = response("(uint32 0, {'streams': <[(uint32 42, {'id': <'opaque-A'>, 'source_type': <uint32 1>})]>, 'restore_token': <'private-token'>})");
    cancelled.receive("/org/freedesktop/portal/desktop/request/1_42/obs3", value);
    assert(!cancelled.grant().selected()); // A late response cannot undo cancellation.
    opencam::PortalResponses noSession(4, ":1.42");
    noSession.receive("/org/freedesktop/portal/desktop/request/1_42/obs3", value);
    assert(noSession.failure() == "portal_identity_unavailable");
    g_variant_unref(value);
    failsStart("(uint32 0, {'streams': <@a(ua{sv}) []>, 'restore_token': <'token'>})", "portal_identity_unavailable");
    failsStart("(uint32 0, {'streams': <[(uint32 42, {'id': <'A'>, 'source_type': <uint32 1>}), (uint32 43, {'id': <'B'>, 'source_type': <uint32 1>})]>, 'restore_token': <'token'>})", "portal_identity_unavailable");
    failsStart("(uint32 0, {'streams': <[(uint32 42, {'id': <'A'>, 'source_type': <uint32 2>})]>, 'restore_token': <'token'>})", "portal_identity_unavailable");
    failsStart("(uint32 0, {'streams': <[(uint32 42, {'source_type': <uint32 1>})]>, 'restore_token': <'token'>})", "portal_identity_unavailable");
    failsStart("(uint32 0, {'streams': <[(uint32 42, {'id': <'A'>, 'source_type': <uint32 1>})]>})", "portal_restore_unavailable");
    failsStart("(uint32 2, {})", "portal_denied");
    std::cout << "PASS: actual GVariant parsing, request ownership, session scope and cancellation latching\n";
}
