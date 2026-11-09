// The texture budget's cut near what the driver allows (gpu/vk/vram_squeeze.h): no game, no GPU
#include "../gpu/vk/vram_squeeze.h"

#include <cstdio>

static int failures = 0;
#define CHECK(c)                                                                \
    do {                                                                        \
        if (!(c)) std::printf("FAIL line %d: %s\n", __LINE__, #c), failures++; \
    } while (0)

int main() {
    constexpr uint64_t MB = 1ull << 20;
    using vram_squeeze::over_97;

    // below 97% of what the driver allows: no cut
    CHECK(over_97(9000 * MB, 10700 * MB, 0, false) == 0);
    CHECK(over_97(10379 * MB, 10700 * MB, 0, false) == 0);

    // an RX 6700 XT's log: 10693 of 10693 MB, 4511 MB free in the blocks. It was cut by ~321 MB every 10 frames; the free
    // space takes that now
    CHECK(over_97(10693 * MB, 10693 * MB, 4511 * MB, false) == 0);

    // little free in the blocks: the excess, less half of it
    const uint64_t line = 10700 * MB / 100 * 97;
    CHECK(over_97(10700 * MB, 10700 * MB, 100 * MB, false) == 10700 * MB - line - 50 * MB);

    // nothing free in the blocks: the whole excess, as before
    CHECK(over_97(10700 * MB, 10700 * MB, 0, false) == 10700 * MB - line);

    // RX_VK_BUDGET_SQUEEZE_ALL=1: the whole excess whatever the blocks hold
    CHECK(over_97(10693 * MB, 10693 * MB, 4511 * MB, true) == 10693 * MB - 10693 * MB / 100 * 97);

    // a driver that reports use past what it allows (not AMD's): still cut
    CHECK(over_97(11000 * MB, 10700 * MB, 200 * MB, false) == 11000 * MB - line - 100 * MB);

    if (failures) return 1;
    std::printf("texture budget cut near the VRAM limit ok\n");
    return 0;
}
