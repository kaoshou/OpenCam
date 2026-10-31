// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include "portal-grant.hpp"
#include <gio/gio.h>
#include <algorithm>
#include <memory>

namespace opencam {
// Consumes only portal signals delivered on this helper's own bus connection.
// No monitor-name, geometry or PipeWire node-id is treated as stable identity.
class PortalResponses {
    struct Unref { void operator()(GVariant *v) const { if (v) g_variant_unref(v); } };
    using Variant = std::unique_ptr<GVariant, Unref>;
    unsigned version_;
    std::string requestPrefix_, sessionPrefix_, session_, failure_;
    PortalGrant grant_;
    static bool owned(const char *path, const std::string &prefix) {
        if (!path) return false;
        const std::string value(path);
        if (value.compare(0, prefix.size(), prefix) != 0 || value.size() == prefix.size()) return false;
        return std::all_of(value.begin() + prefix.size(), value.end(), [](char c) { return c >= '0' && c <= '9'; });
    }
public:
    PortalResponses(unsigned version, const std::string &uniqueName) : version_(version) {
        if (uniqueName.size() < 2 || uniqueName.front() != ':' ||
            uniqueName.find_first_not_of("0123456789.", 1) != std::string::npos)
            throw std::runtime_error("portal_bus_unavailable");
        auto sender = uniqueName.substr(1);
        std::replace(sender.begin(), sender.end(), '.', '_');
        requestPrefix_ = "/org/freedesktop/portal/desktop/request/" + sender + "/obs";
        sessionPrefix_ = "/org/freedesktop/portal/desktop/session/" + sender + "/obs";
    }
    const PortalGrant &grant() const noexcept { return grant_; }
    const std::string &session() const noexcept { return session_; }
    const std::string &failure() const noexcept { return failure_; }
    void closed(const char *path) {
        if (failure_.empty() && !session_.empty() && path && session_ == path)
            failure_ = "portal_session_closed";
    }
    void receive(const char *path, GVariant *parameters) noexcept {
        if (!failure_.empty() || !owned(path, requestPrefix_)) return;
        try {
            if (!parameters || !g_variant_is_of_type(parameters, G_VARIANT_TYPE("(ua{sv})")))
                throw std::runtime_error("portal_identity_unavailable");
            guint32 response = 0;
            GVariant *raw = nullptr;
            g_variant_get(parameters, "(u@a{sv})", &response, &raw);
            Variant results(raw);
            if (response != 0) throw std::runtime_error(response == 1 ? "portal_cancelled" : "portal_denied");
            Variant session(g_variant_lookup_value(results.get(), "session_handle", nullptr));
            if (session) {
                // ScreenCast preserves the historical string wire type even
                // though the value names an object path (XDG portal contract).
                if (!session_.empty() || !g_variant_is_of_type(session.get(), G_VARIANT_TYPE_STRING) ||
                    !owned(g_variant_get_string(session.get(), nullptr), sessionPrefix_))
                    throw std::runtime_error("portal_identity_unavailable");
                session_ = g_variant_get_string(session.get(), nullptr);
            }
            Variant streams(g_variant_lookup_value(results.get(), "streams", nullptr));
            if (!streams) return; // CreateSession/SelectSources are not Start.
            if (session_.empty() || !g_variant_is_of_type(streams.get(), G_VARIANT_TYPE("a(ua{sv})")) ||
                g_variant_n_children(streams.get()) != 1)
                throw std::runtime_error("portal_identity_unavailable");
            Variant stream(g_variant_get_child_value(streams.get(), 0));
            guint32 node = 0, sourceType = 0;
            g_variant_get(stream.get(), "(u@a{sv})", &node, &raw);
            Variant properties(raw);
            const char *id = nullptr, *token = nullptr;
            if (!g_variant_lookup(properties.get(), "id", "&s", &id) ||
                !g_variant_lookup(properties.get(), "source_type", "u", &sourceType))
                throw std::runtime_error("portal_identity_unavailable");
            if (!g_variant_lookup(results.get(), "restore_token", "&s", &token))
                throw std::runtime_error("portal_restore_unavailable");
            grant_.accept(version_, 1, id, node, sourceType, token);
        } catch (const std::runtime_error &e) {
            failure_ = e.what(); // All thrown messages here are fixed codes, never payloads.
        } catch (...) { failure_ = "portal_identity_unavailable"; }
    }
};
} // namespace opencam
