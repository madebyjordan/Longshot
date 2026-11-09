// The worker threads (vk_jobs.h). As many as a third of the hardware threads (2 to 4): the game itself runs six
// virtual cores, and the replay thread is one of the busiest.
#include <algorithm>
#include <condition_variable>
#include <deque>
#include <mutex>
#include <thread>
#include <vector>

#include "vk_jobs.h"
#include "vk_internal.h"

namespace mkwvk {
namespace {

std::mutex mu;
std::condition_variable work_cv, done_cv;
struct Job {
    std::function<void()> fn;
    const void* flag = nullptr;   // (jobs_push_for's: what jobs_wait looks for)
};
std::deque<Job> queue;
std::atomic<int> pending{0};
bool started = false;

void worker() {
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_BELOW_NORMAL);
    SetThreadDescription(GetCurrentThread(), L"mkw_vk worker");
    for (;;) {
        std::function<void()> fn;
        {
            std::unique_lock lk(mu);
            work_cv.wait(lk, [] { return !queue.empty(); });
            fn = std::move(queue.front().fn);
            queue.pop_front();
        }
        fn();
        pending--;
        jobs_notify();
    }
}

void start() {
    const unsigned n = std::clamp(std::thread::hardware_concurrency() / 3, 2u, 4u);
    for (unsigned i = 0; i < n; i++) std::thread(worker).detach();
    started = true;
    logf("vk: %u background workers (shader translation, pipelines)", n);
}

} // namespace

void jobs_push(std::function<void()> fn, bool urgent) {
    std::lock_guard lk(mu);
    if (!started) start();
    pending++;
    if (urgent) queue.push_front({std::move(fn)});
    else queue.push_back({std::move(fn)});
    work_cv.notify_one();
}
void jobs_push_for(const std::atomic<bool>& flag, std::function<void()> fn) {
    std::lock_guard lk(mu);
    if (!started) start();
    pending++;
    queue.push_back({std::move(fn), &flag});
    work_cv.notify_one();
}
// (issue 496) Intel A750: four of the game's big compute pipelines built at once by Intel's compiler (up to 58 s each) froze
// the whole PC. One thread builds them one at a time there, below normal priority, beside the shared workers
void jobs_push_serial(std::function<void()> fn) {
    static std::mutex smu;
    static std::condition_variable scv;
    static std::deque<std::function<void()>> sq;
    static bool sstarted = false;
    std::lock_guard lk(smu);
    if (!sstarted) {
        sstarted = true;
        std::thread([] {
            SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_BELOW_NORMAL);
            SetThreadDescription(GetCurrentThread(), L"mkw_vk serial builds");
            for (;;) {
                std::function<void()> fn;
                {
                    std::unique_lock lk(smu);
                    scv.wait(lk, [] { return !sq.empty(); });
                    fn = std::move(sq.front());
                    sq.pop_front();
                }
                fn();
                jobs_notify();
            }
        }).detach();
        logf("vk: compute pipelines are built one at a time");
    }
    sq.push_back(std::move(fn));
    scv.notify_one();
}
void jobs_notify() {
    std::lock_guard lk(mu);
    done_cv.notify_all();
}
// (longshot) A job waited for that no worker has taken yet is run here, now: the queue is first come, first served, and on a
// first start (no pipelines built before the game, AMD's driver cache empty) a draw that must wait for its pipeline waited
// for every pipeline queued before it: freezes of 5 to 27 s from new game to the Deku Tree (RX 6700 XT, Ryzen 5 5600).
// Now it waits for its own build only. A job a worker has begun is waited for, as before
void jobs_wait(const std::atomic<bool>& flag) {
    std::unique_lock lk(mu);
    if (flag.load(std::memory_order_acquire)) return;
    for (auto it = queue.begin(); it != queue.end(); ++it)
        if (it->flag == &flag) {
            std::function<void()> fn = std::move(it->fn);
            queue.erase(it);
            lk.unlock();
            fn();
            pending--;
            jobs_notify();
            lk.lock();
            break;
        }
    done_cv.wait(lk, [&] { return flag.load(std::memory_order_acquire); });
}
int jobs_pending() {
    return pending.load();
}

} // namespace mkwvk
