using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Hookshot.Launcher;

/// A game controller (Xbox, PlayStation, Switch Pro, the Steam Deck's own controls, anything SDL2 knows) drives the window the way the
/// keyboard does, so the launcher works from a couch with no mouse:
///   d-pad / left stick  move the focus (Avalonia's own directional focus, which it keeps for gamepads), held keys repeat
///   LB / RB             Shift+Tab / Tab
///   A                   Space on the focused control (clicks a button, flips a switch, opens or picks in a drop-down)
///   B                   Escape on the focused control (closes an open drop-down); if nothing takes it, the Back event
///   Start (☰)           the Menu event
/// A thread reads the controllers about 60 times a second through SDL2 and hands what they mean to the UI thread; it reads
/// controllers plugged in or out while the launcher runs. Without SDL2 (or with HOOKSHOT_NO_GAMEPAD set) Start gives null and the
/// window is just as it was. Input only counts while the window is the active one, so a game in front of it doesn't drive it.
public sealed class Gamepad : IDisposable
{
    readonly TopLevel top;
    readonly Sdl sdl;
    readonly Thread thread;
    readonly ManualResetEventSlim stop = new();
    volatile bool active;
    bool disposed;   // read and written on the UI thread only

    /// B was pressed and no control used it (the window closes its menu panel, say)
    public event Action? Back;
    /// Start / ☰ was pressed (the window opens or closes its menu panel)
    public event Action? Menu;

    /// Starts reading controllers for `top` (call on the UI thread), or null when there is no SDL2 to read them with
    public static Gamepad? Start(TopLevel top)
    {
        if (Environment.GetEnvironmentVariable("HOOKSHOT_NO_GAMEPAD") is { Length: > 0 }) return null;
        try
        {
            return Sdl.Load() is { } sdl ? new Gamepad(top, sdl) : null;
        }
        catch (Exception e)   // a broken library must not stop the launcher
        {
            Trace.WriteLine($"Hookshot: no controller support ({e.Message})");
            return null;
        }
    }

    Gamepad(TopLevel top, Sdl sdl)
    {
        this.top = top;
        this.sdl = sdl;
        if (top is WindowBase window)
        {
            active = window.IsActive;
            window.Activated += OnActivated;
            window.Deactivated += OnDeactivated;
        }
        else active = true;
        thread = new Thread(Poll) { IsBackground = true, Name = "Hookshot gamepad" };
        thread.Start();
    }

    void OnActivated(object? sender, EventArgs e) => active = true;
    void OnDeactivated(object? sender, EventArgs e) => active = false;

    /// Stops reading (on the UI thread, when the window closes); SDL is shut down on the reading thread
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (top is WindowBase window)
        {
            window.Activated -= OnActivated;
            window.Deactivated -= OnDeactivated;
        }
        stop.Set();
        thread.Join(TimeSpan.FromSeconds(1));
    }

    /// The reading thread: every SDL call happens here
    void Poll()
    {
        var pads = new Dictionary<int, IntPtr>();   // SDL instance id -> open controller
        try
        {
            sdl.SetHint("SDL_NO_SIGNAL_HANDLERS", "1");                    // leave Ctrl+C / SIGTERM to .NET
            sdl.SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");      // SDL has no window of its own to be focused; we check ours
            sdl.SetHint("SDL_GAMECONTROLLER_USE_BUTTON_LABELS", "0");      // A is the bottom button on every pad, Nintendo's too
            sdl.SetHint("SDL_JOYSTICK_THREAD", "1");                       // (Windows) SDL pumps its own device messages
            sdl.SetHint("SDL_TIMER_RESOLUTION", "0");                      // (Windows) don't raise the system timer rate
            if (sdl.Init(Sdl.InitGameController) != 0)
            {
                sdl.Quit();   // undo whatever part did start; there is just no controller support
                return;
            }
            try
            {
                sdl.GameControllerEventState(Sdl.Ignore);                  // controllers are read directly, so let no events pile up
                sdl.JoystickEventState(Sdl.Ignore);
                var map = new PadMap();
                var actions = new List<PadAction>();
                var clock = Stopwatch.StartNew();
                int seen = -1;
                long nextScan = 0;
                while (!stop.IsSet)
                {
                    sdl.GameControllerUpdate();                            // reads the devices, notices any plugged in or out
                    long now = clock.ElapsedMilliseconds;
                    int count = sdl.NumJoysticks();
                    if (count != seen || now >= nextScan)
                    {
                        Scan(pads);
                        seen = count;
                        nextScan = now + 1000;
                    }
                    var state = default(PadState);
                    foreach (var pad in pads.Values) state = PadState.Merge(state, Read(pad));
                    actions.Clear();
                    map.Step(state, now, actions);                         // stepped even when inactive, so a held button doesn't fire on return
                    if (actions.Count > 0 && active)
                    {
                        var batch = actions.ToArray();
                        Dispatcher.UIThread.Post(() => Perform(batch));
                    }
                    stop.Wait(pads.Count == 0 ? 250 : active ? 16 : 100);   // slow down with no controller, or the window in the back
                }
            }
            finally
            {
                foreach (var pad in pads.Values) sdl.GameControllerClose(pad);
                sdl.Quit();
            }
        }
        catch (Exception e)   // never let the reading thread take the launcher down
        {
            Trace.WriteLine($"Hookshot: controller support stopped ({e.Message})");
        }
    }

    /// Closes controllers that went away and opens new ones
    void Scan(Dictionary<int, IntPtr> pads)
    {
        foreach (var (id, pad) in pads.ToArray())
        {
            if (sdl.GameControllerGetAttached(pad) != 0) continue;
            sdl.GameControllerClose(pad);
            pads.Remove(id);
        }
        int count = sdl.NumJoysticks();
        for (int i = 0; i < count; i++)
        {
            int id = sdl.JoystickGetDeviceInstanceID(i);
            if (id < 0 || pads.ContainsKey(id) || sdl.IsGameController(i) == 0) continue;
            var pad = sdl.GameControllerOpen(i);
            if (pad != IntPtr.Zero) pads[id] = pad;
        }
    }

    static readonly (int Button, PadButtons Flag)[] Buttons =
    [
        (Sdl.ButtonDpadUp, PadButtons.Up), (Sdl.ButtonDpadDown, PadButtons.Down),
        (Sdl.ButtonDpadLeft, PadButtons.Left), (Sdl.ButtonDpadRight, PadButtons.Right),
        (Sdl.ButtonA, PadButtons.A), (Sdl.ButtonB, PadButtons.B), (Sdl.ButtonStart, PadButtons.Start),
        (Sdl.ButtonLeftShoulder, PadButtons.LeftShoulder), (Sdl.ButtonRightShoulder, PadButtons.RightShoulder),
    ];

    PadState Read(IntPtr pad)
    {
        var held = PadButtons.None;
        foreach (var (button, flag) in Buttons)
            if (sdl.GameControllerGetButton(pad, button) != 0) held |= flag;
        return new PadState(held, sdl.GameControllerGetAxis(pad, Sdl.AxisLeftX), sdl.GameControllerGetAxis(pad, Sdl.AxisLeftY));
    }

    void Perform(PadAction[] batch)
    {
        if (disposed) return;
        foreach (var action in batch) Act(top, action, Back, Menu);
    }

    /// Plays one action on the window (UI thread): Menu calls `menu`; the rest press a key on the focused control,
    /// and B that nothing took calls `back`
    internal static void Act(TopLevel top, PadAction action, Action? back, Action? menu)
    {
        if (action == PadAction.Menu) menu?.Invoke();
        else if (!PressKey(top, action) && action == PadAction.Back) back?.Invoke();
    }

    /// Presses and lets go of the key the action stands for, on the focused control (or the window when nothing has focus), as the
    /// keyboard would, marked as coming from a gamepad: that is what turns on Avalonia's up/down/left/right focus moves (XYFocus) and
    /// keeps a closed drop-down from changing its choice on up/down. Gives whether the key press was used.
    internal static bool PressKey(TopLevel top, PadAction action)
    {
        var (key, modifiers) = action switch
        {
            PadAction.Up => (Key.Up, KeyModifiers.None),
            PadAction.Down => (Key.Down, KeyModifiers.None),
            PadAction.Left => (Key.Left, KeyModifiers.None),
            PadAction.Right => (Key.Right, KeyModifiers.None),
            PadAction.Next => (Key.Tab, KeyModifiers.None),
            PadAction.Previous => (Key.Tab, KeyModifiers.Shift),
            PadAction.Activate => (Key.Space, KeyModifiers.None),
            PadAction.Back => (Key.Escape, KeyModifiers.None),
            _ => (Key.None, KeyModifiers.None),
        };
        if (key == Key.None) return false;

        KeyEventArgs Args(RoutedEvent routed, Interactive source) => new()
        {
            RoutedEvent = routed, Source = source, Key = key, KeyModifiers = modifiers, KeyDeviceType = KeyDeviceType.Gamepad,
        };
        Interactive Focused() => top.FocusManager?.GetFocusedElement() as Interactive ?? top;

        var target = Focused();
        var down = Args(InputElement.KeyDownEvent, target);
        target.RaiseEvent(down);
        target = Focused();   // the key comes up wherever the focus is now, as a real key does
        target.RaiseEvent(Args(InputElement.KeyUpEvent, target));
        return down.Handled;
    }
}
