#pragma once

#include <algorithm>
#include <array>
#include <cwctype>
#include <string_view>

namespace ClickToImages::ShellExtension
{
    // The context menu's visibility gate. Must stay in lockstep with Core's SupportedExtensions.All;
    // SupportedExtensionsSyncTests (Core.Tests) fails if they drift.
    inline constexpr std::array<std::wstring_view, 1> SupportedExtensions =
    {
        L".pdf"
    };

    inline bool EqualsCaseInsensitive(std::wstring_view a, std::wstring_view b)
    {
        return a.size() == b.size() && std::equal(a.begin(), a.end(), b.begin(),
            [](wchar_t left, wchar_t right)
            {
                return std::towlower(left) == std::towlower(right);
            });
    }

    inline bool IsSupportedExtension(std::wstring_view extension)
    {
        for (std::wstring_view candidate : SupportedExtensions)
        {
            if (EqualsCaseInsensitive(candidate, extension))
            {
                return true;
            }
        }

        return false;
    }
}
