#pragma once

// {826D7E6E-085D-4C2B-9402-AC3519103B88}
// CLSID for ExplorerCommandHandler. Generated once; must stay stable across builds — it's
// the identity Explorer/registry use to find this handler. Kept as both a GUID and its
// registry-key string form (must match __uuidof(ExplorerCommandHandler) in ExplorerCommand.h)
// so Registration.cpp doesn't need to format one at runtime.
static constexpr GUID CLSID_ExplorerCommandHandler =
{ 0x826d7e6e, 0x085d, 0x4c2b, { 0x94, 0x02, 0xac, 0x35, 0x19, 0x10, 0x3b, 0x88 } };

inline constexpr wchar_t CLSID_ExplorerCommandHandlerString[] = L"{826D7E6E-085D-4C2B-9402-AC3519103B88}";
