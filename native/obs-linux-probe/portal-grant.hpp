// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <string>

namespace opencam {
// A single successful portal Start response. Credentials stay inside the
// capture process; neither this type nor its errors serialize their contents.
class PortalGrant {
    std::string identity_, token_;
    static bool bounded(const std::string &value, std::size_t limit) {
        if (value.empty() || value.size() > limit) return false;
        for (unsigned char c : value)
            if (c < 32 || c == 127) return false;
        return true;
    }
public:
    bool selected() const noexcept { return !identity_.empty(); }
    void accept(unsigned version, std::size_t count, const std::string &identity,
                std::uint32_t node, unsigned sourceType, const std::string &token) {
        if (selected() || version < 4 || count != 1 || sourceType != 1 ||
            node == 0 || node == std::numeric_limits<std::uint32_t>::max() ||
            !bounded(identity, 1024))
            throw std::runtime_error("portal_identity_unavailable");
        if (!bounded(token, 4096))
            throw std::runtime_error("portal_restore_unavailable");
        // Construct before committing, including allocation failure.
        auto nextIdentity = identity;
        auto nextToken = token;
        identity_.swap(nextIdentity);
        token_.swap(nextToken);
    }
    void requireIdentity(const std::string &expected) const {
        if (!selected() || identity_ != expected)
            throw std::runtime_error("portal_source_changed");
    }
    const std::string &identity() const noexcept { return identity_; }
    const std::string &restoreToken() const noexcept { return token_; }
};
} // namespace opencam
