// gpu/vk/vk_jobs.cpp's jobs_wait (tests/test_jobs_wait.py builds it with a stand-in vk_internal.h): a job waited for that no
// worker has taken is run by the waiting thread, not left behind the queue.
#include "vk_jobs.h"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <future>
#include <mutex>
#include <thread>

#define CHECK(name, condition) do { if (!(condition)) { std::fprintf(stderr, "FAIL %s at line %d\n", name, __LINE__); return 1; } } while (0)

using namespace mkwvk;

int main() {
    // every worker held by a job that waits for the test to let it go (more than any worker count: 2 to 4)
    std::mutex m;
    std::condition_variable cv;
    bool go = false;
    std::atomic<int> blockers_done{0};
    for (int i = 0; i < 8; i++)
        jobs_push([&] {
            std::unique_lock lk(m);
            cv.wait(lk, [&] { return go; });
            blockers_done++;
        });

    // the job a draw waits for, queued behind them
    std::atomic<bool> ready{false};
    std::thread::id ran_on;
    jobs_push_for(ready, [&] {
        ran_on = std::this_thread::get_id();
        ready.store(true, std::memory_order_release);
    });
    auto waited = std::async(std::launch::async, [&] {
        jobs_wait(ready);
        return std::this_thread::get_id();
    });
    CHECK("the waited-for job runs at once, the workers all busy", waited.wait_for(std::chrono::seconds(5)) == std::future_status::ready);
    CHECK("it ran on the waiting thread", waited.get() == ran_on);
    CHECK("the blocking jobs are still waiting", blockers_done.load() == 0);

    // a flag already set: no wait at all
    jobs_wait(ready);

    // a job already taken by a worker is waited for, as before
    {
        std::lock_guard lk(m);
        go = true;
    }
    cv.notify_all();
    std::atomic<bool> later{false};
    jobs_push_for(later, [&] {
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
        later.store(true, std::memory_order_release);
        jobs_notify();
    });
    auto second = std::async(std::launch::async, [&] { jobs_wait(later); });
    CHECK("a job waited for finishes", second.wait_for(std::chrono::seconds(5)) == std::future_status::ready);
    CHECK("it set its flag", later.load());
    for (int i = 0; i < 100 && blockers_done.load() < 8; i++) std::this_thread::sleep_for(std::chrono::milliseconds(20));
    CHECK("the other jobs all ran", blockers_done.load() == 8);
    for (int i = 0; i < 100 && jobs_pending() != 0; i++) std::this_thread::sleep_for(std::chrono::milliseconds(20));
    CHECK("nothing left pending", jobs_pending() == 0);
    std::puts("jobs wait: all checks passed");
}
