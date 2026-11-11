using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Hookshot.Launcher;

/// Runs every check and prints each failure; exit code 0 when all pass.
/// `no-sdl` instead checks that a launcher with no SDL2 beside it has no controller support and no trouble.
static class Program
{
    static int failures, passes;

    static void Check(bool ok, string what)
    {
        if (ok) passes++;
        else { failures++; Console.WriteLine($"FAIL {what}"); }
    }

    static void Same<T>(IEnumerable<T> got, IEnumerable<T> want, string what)
    {
        var g = got.ToList();
        var w = want.ToList();
        Check(g.SequenceEqual(w), $"{what}: got [{string.Join(", ", g)}], want [{string.Join(", ", w)}]");
    }

    [STAThread]
    static int Main(string[] args)
    {
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true }).SetupWithoutStarting();
        if (args.Contains("no-sdl"))
        {
            NoSdl();
        }
        else
        {
            Mapping();
            Keys();
            SdlSmoke();
        }
        Console.WriteLine($"{passes} passed, {failures} failed");
        return failures == 0 ? 0 : 1;
    }

    // ---- the input -> action mapping (no SDL, no window) ----

    static List<PadAction> Step(PadMap map, PadState state, long now)
    {
        var into = new List<PadAction>();
        map.Step(state, now, into);
        return into;
    }

    static PadState Pad(PadButtons b) => new(b, 0, 0);
    static PadState Stick(double x, double y) => new(PadButtons.None, (short)(x * 32767), (short)(y * 32767));

    /// Holds `state` from `from` to `to` in 16 ms ticks (as the reading thread does) and gives every action with its time
    static List<(long, PadAction)> Hold(PadMap map, PadState state, long from, long to)
    {
        var got = new List<(long, PadAction)>();
        for (long t = from; t <= to; t += 16)
            foreach (var a in Step(map, state, t)) got.Add((t, a));
        return got;
    }

    static void Mapping()
    {
        // nothing held: nothing happens
        var map = new PadMap();
        Same(Hold(map, default, 0, 2000), [], "idle");

        // d-pad held: once at once, then after 400 ms, then every 120 ms (on 16 ms ticks: 0, 400, 528, 640, 768, 880, 1008)
        map = new PadMap();
        var held = Hold(map, Pad(PadButtons.Down), 0, 1010);
        Same(held.Select(h => h.Item2), Enumerable.Repeat(PadAction.Down, 7), "d-pad repeat count");
        Same(held.Select(h => h.Item1), [0L, 400, 528, 640, 768, 880, 1008], "d-pad repeat times");

        // let go and press again: at once again
        Same(Step(map, default, 1024), [], "release");
        Same(Step(map, Pad(PadButtons.Down), 1040), [PadAction.Down], "press again");

        // change direction while held: the new one at once, and its own 400 ms wait
        map = new PadMap();
        Hold(map, Pad(PadButtons.Up), 0, 600);
        Same(Step(map, Pad(PadButtons.Left), 608), [PadAction.Left], "turn");
        Same(Hold(map, Pad(PadButtons.Left), 624, 1000).Select(h => h.Item2), [], "turn waits 400 ms");
        Same(Step(map, Pad(PadButtons.Left), 1008), [PadAction.Left], "turn repeats");

        // a long stall (a slow tick) doesn't make a burst of repeats
        map = new PadMap();
        Step(map, Pad(PadButtons.Right), 0);
        Same(Step(map, Pad(PadButtons.Right), 3000), [PadAction.Right], "after stall: one");
        Same(Step(map, Pad(PadButtons.Right), 3016), [], "after stall: then waits");
        Same(Step(map, Pad(PadButtons.Right), 3120), [PadAction.Right], "after stall: at the normal pace");

        // the stick: nothing inside the dead zone; past half way it's an arrow; it stays pushed until under 0.35
        map = new PadMap();
        Same(Step(map, Stick(0, 0.45), 0), [], "stick in dead zone");
        Same(Step(map, Stick(0, 0.6), 16), [PadAction.Down], "stick down (y grows downwards)");
        Same(Step(map, Stick(0, 0.4), 32), [], "stick eased back, still held (no new press)");
        Same(Step(map, Stick(0, 0.4), 416), [PadAction.Down], "stick eased back still repeats");
        Same(Step(map, Stick(0, 0.3), 432), [], "stick under release: let go");
        Same(Step(map, Stick(0, 0.45), 448), [], "stick needs half way again");
        Same(Step(map, Stick(0, -1), 464), [PadAction.Up], "stick up (full, -32767)");
        Same(Step(new PadMap(), new PadState(PadButtons.None, 0, short.MinValue), 0), [PadAction.Up], "stick up (-32768)");
        Same(Step(new PadMap(), Stick(0.7, 0.6), 0), [PadAction.Right], "stick diagonal: the larger axis");
        Same(Step(new PadMap(), Stick(-0.6, 0.6), 0), [PadAction.Down], "stick exact diagonal: up/down");
        Same(Step(new PadMap(), new PadState(PadButtons.Left, 0, 32767), 0), [PadAction.Left], "the d-pad beats the stick");

        // buttons: once per press, however long held
        map = new PadMap();
        Same(Hold(map, Pad(PadButtons.A), 0, 2000).Select(h => h.Item2), [PadAction.Activate], "A held");
        Same(Step(map, default, 2016), [], "A up");
        Same(Step(map, Pad(PadButtons.A), 2032), [PadAction.Activate], "A again");
        Same(Step(new PadMap(), Pad(PadButtons.B), 0), [PadAction.Back], "B");
        Same(Step(new PadMap(), Pad(PadButtons.Start), 0), [PadAction.Menu], "Start");
        Same(Step(new PadMap(), Pad(PadButtons.LeftShoulder), 0), [PadAction.Previous], "LB");
        Same(Step(new PadMap(), Pad(PadButtons.RightShoulder), 0), [PadAction.Next], "RB");
        Same(Step(new PadMap(), Pad(PadButtons.None), 0), [], "nothing");

        // a button pressed while a direction is held: both, and the direction keeps repeating
        map = new PadMap();
        Step(map, Pad(PadButtons.Down), 0);
        Same(Step(map, Pad(PadButtons.Down | PadButtons.A), 16), [PadAction.Activate], "A while holding down");
        Same(Step(map, Pad(PadButtons.Down | PadButtons.A), 400), [PadAction.Down], "down still repeats");

        // two controllers read as one
        var merged = PadState.Merge(new PadState(PadButtons.A, 100, -30000), new PadState(PadButtons.Up, -20000, 50));
        Check(merged == new PadState(PadButtons.A | PadButtons.Up, -20000, -30000), $"merge: {merged}");
        Check(PadState.Merge(default, new PadState(PadButtons.None, short.MinValue, 0)).StickX == short.MinValue, "merge -32768");
    }

    // ---- the key presses on a real (screenless) Avalonia window ----

    static void Keys()
    {
        var first = new Button { Content = "First" };
        var tick = new ToggleButton { Content = "Tick" };
        var drop = new ComboBox { ItemsSource = new[] { "one", "two", "three" }, SelectedIndex = 0 };
        var clicks = 0;
        var last = new Button { Content = "Last" };
        last.Click += (_, _) => clicks++;
        var right = new Button { Content = "Right" };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { last, right } };
        var window = new Window
        {
            Width = 400, Height = 400,
            Content = new StackPanel { Spacing = 20, Margin = new Thickness(20), Children = { first, tick, drop, row } },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var backs = 0;
        var menus = 0;
        void Do(PadAction a)
        {
            Gamepad.Act(window, a, () => backs++, () => menus++);
            Dispatcher.UIThread.RunJobs();
        }
        IInputElement? Focused() => window.FocusManager?.GetFocusedElement();

        Do(PadAction.Down);
        Check(Focused() == first, $"down with nothing focused focuses the first control (got {Focused()})");
        Do(PadAction.Down);
        Check(Focused() == tick, $"down moves to the next control (got {Focused()})");
        Do(PadAction.Activate);
        Check(tick.IsChecked == true, "A flips a toggle");
        Do(PadAction.Activate);
        Check(tick.IsChecked == false, "A flips it back");
        Do(PadAction.Down);
        Check(Focused() == drop, $"down reaches the drop-down (got {Focused()})");
        Do(PadAction.Down);
        Check(Focused() == last && drop.SelectedIndex == 0, $"down leaves a closed drop-down without changing it (got {Focused()}, choice {drop.SelectedIndex})");
        Do(PadAction.Right);
        Check(Focused() == right, $"right moves sideways (got {Focused()})");
        Do(PadAction.Left);
        Check(Focused() == last, $"left moves back (got {Focused()})");
        Do(PadAction.Activate);
        Check(clicks == 1, $"A clicks a button (clicks {clicks})");
        Do(PadAction.Up);
        Check(Focused() == drop, $"up goes back to the drop-down (got {Focused()})");

        Do(PadAction.Activate);
        Check(drop.IsDropDownOpen, "A opens a drop-down");
        Do(PadAction.Down);
        Do(PadAction.Activate);
        Check(!drop.IsDropDownOpen && drop.SelectedIndex == 1, $"down + A in an open drop-down picks the next choice (open {drop.IsDropDownOpen}, choice {drop.SelectedIndex})");
        Do(PadAction.Activate);
        Do(PadAction.Back);
        Check(!drop.IsDropDownOpen && backs == 0, $"B closes an open drop-down and is not a Back (open {drop.IsDropDownOpen}, backs {backs})");
        Check(drop.SelectedIndex == 1, $"B leaves the choice as it was ({drop.SelectedIndex})");
        Do(PadAction.Back);
        Check(backs == 1, $"B with nothing to close is a Back ({backs})");
        Do(PadAction.Menu);
        Check(menus == 1 && backs == 1, $"Start is a Menu ({menus})");

        first.Focus();
        Do(PadAction.Next);
        Check(Focused() == tick, $"RB tabs forward (got {Focused()})");
        Do(PadAction.Previous);
        Check(Focused() == first, $"LB tabs back (got {Focused()})");

        // Start on a live window, stop on close: no controller here, so only that it starts and stops cleanly
        var pad = Gamepad.Start(window);
        Check(pad is not null, "Gamepad.Start finds the SDL2 beside the program");
        Thread.Sleep(600);   // the reading thread initialises SDL and polls a few times
        var clock = System.Diagnostics.Stopwatch.StartNew();
        pad?.Dispose();
        Check(clock.ElapsedMilliseconds < 1000, $"Dispose stops the reading thread quickly ({clock.ElapsedMilliseconds} ms)");
        pad?.Dispose();   // twice is harmless
        Environment.SetEnvironmentVariable("HOOKSHOT_NO_GAMEPAD", "1");
        Check(Gamepad.Start(window) is null, "HOOKSHOT_NO_GAMEPAD turns it off");
        Environment.SetEnvironmentVariable("HOOKSHOT_NO_GAMEPAD", null);
        window.Close();
    }

    // ---- SDL2 itself, with no window: it loads, starts, sees no controller on a PC without one ----

    static void SdlSmoke()
    {
        var sdl = Sdl.Load();
        Check(sdl is not null, "SDL2 loads");
        if (sdl is null) return;
        sdl.SetHint("SDL_NO_SIGNAL_HANDLERS", "1");
        sdl.SetHint("SDL_JOYSTICK_THREAD", "1");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var init = sdl.Init(Sdl.InitGameController);
        Check(init == 0, $"SDL_Init(GAMECONTROLLER) = {init}");
        var initMs = clock.ElapsedMilliseconds;
        sdl.GameControllerEventState(Sdl.Ignore);
        sdl.GameControllerUpdate();
        var count = sdl.NumJoysticks();
        Console.WriteLine($"SDL2 started in {initMs} ms; {count} joystick(s) attached");
        for (int i = 0; i < count; i++)
        {
            Console.WriteLine($"  #{i}: game controller {sdl.IsGameController(i) != 0}, instance {sdl.JoystickGetDeviceInstanceID(i)}");
            if (sdl.IsGameController(i) == 0) continue;
            // a controller that happens to be there: open it and read it once (untouched, so no buttons and a centred stick)
            var pad = sdl.GameControllerOpen(i);
            Check(pad != IntPtr.Zero && sdl.GameControllerGetAttached(pad) != 0, $"controller #{i} opens");
            if (pad == IntPtr.Zero) continue;
            sdl.GameControllerUpdate();
            var held = Enumerable.Range(0, 15).Where(b => sdl.GameControllerGetButton(pad, b) != 0);
            Console.WriteLine($"     buttons held [{string.Join(", ", held)}], stick {sdl.GameControllerGetAxis(pad, Sdl.AxisLeftX)}, {sdl.GameControllerGetAxis(pad, Sdl.AxisLeftY)}");
            sdl.GameControllerClose(pad);
        }
        sdl.Quit();
    }

    static void NoSdl()
    {
        Check(Sdl.Load() is null, "no SDL2 beside the program: Load gives null");
        var button = new Button { Content = "Still works" };
        var window = new Window { Content = button };
        window.Show();
        Check(Gamepad.Start(window) is null, "no SDL2: Gamepad.Start gives null");
        button.Focus();
        Dispatcher.UIThread.RunJobs();
        Check(window.FocusManager?.GetFocusedElement() == button, "the window still works");
        window.Close();
    }
}

class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
