// (hookshot) The texture budget's cut near what the driver allows (vk_texture.cpp, texture_frame), at 97% of it.
//
// Past 97% the budget shrank by the whole excess, whatever VMA's blocks held free. But an evicted texture's space stays in
// its block, which goes back to the driver only when every image in it is gone, so the driver's count hardly moved: 10
// frames later the budget shrank again, and again. On an RX 6700 XT (10.7 GB allowed) the textures went from 5.3 GB to
// 2.5 GB while 4.5 GB sat free in the blocks and the driver still read 10693 of 10693 MB; the textures thrown out were made
// again as soon as they were drawn, a 23 s stall once in "texture lookups". Now the cut is the excess less half the free
// space in the blocks (new images go there before VMA makes a block; half, as it is in pieces).
// RX_VK_BUDGET_SQUEEZE_ALL=1: the whole excess, as before.
//
// No Vulkan here, so tests/test_vram_squeeze.cpp runs it as it is.
#pragma once

#include <cstdint>

namespace vram_squeeze {

/// What the budget is cut by: the use past 97% of what the driver allows, less what half the blocks' free space can take
inline uint64_t over_97(uint64_t used, uint64_t allowed, uint64_t block_free, bool whole_excess) {
    const uint64_t line = allowed / 100 * 97;
    if (used <= line) return 0;
    const uint64_t over = used - line;
    const uint64_t absorb = whole_excess ? 0 : block_free / 2;
    return over > absorb ? over - absorb : 0;
}

} // namespace vram_squeeze
