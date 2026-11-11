namespace Hookshot.Launcher;

/// What a controller press asks of the window
public enum PadAction { Up, Down, Left, Right, Next, Previous, Activate, Back, Menu }

/// The controller buttons the launcher listens to (positions on an Xbox-style pad: A is the bottom face button, B the right one)
[Flags]
public enum PadButtons
{
    None = 0,
    Up = 1, Down = 2, Left = 4, Right = 8,          // the d-pad
    A = 16, B = 32, Start = 64,                     // Start is the ☰ "Menu" button
    LeftShoulder = 128, RightShoulder = 256,
}

/// One reading of the controller(s): the buttons held and the left stick (-32768..32767 per axis, as SDL gives it; y grows downwards)
public readonly record struct PadState(PadButtons Buttons, short StickX, short StickY)
{
    /// Two controllers read as one: a button held on either counts, and the stick pushed further wins
    public static PadState Merge(PadState a, PadState b) =>
        new(a.Buttons | b.Buttons,
            Math.Abs((int)b.StickX) > Math.Abs((int)a.StickX) ? b.StickX : a.StickX,
            Math.Abs((int)b.StickY) > Math.Abs((int)a.StickY) ? b.StickY : a.StickY);
}

/// Turns a stream of controller readings into actions, with no SDL and no window, so it can be tested on its own.
/// The d-pad and the left stick move like held arrow keys: once at once, again after FirstRepeatMs, then every RepeatMs.
/// The other buttons act once per press (A activates, B goes back, Start opens the menu, the shoulders tab back and forth).
public sealed class PadMap
{
    /// The stick counts as pushed past Press (half way), and stays pushed until it falls back under Release,
    /// so a thumb resting near the edge doesn't flicker on and off
    public const double Press = 0.5, Release = 0.35;
    public const long FirstRepeatMs = 400, RepeatMs = 120;

    PadButtons last;
    PadAction? held;
    long nextRepeat;

    /// Adds to `into` what this reading means, `now` being a clock in milliseconds
    public void Step(PadState state, long now, List<PadAction> into)
    {
        var dir = Direction(state);
        if (dir != held)
        {
            held = dir;
            if (dir is { } d)
            {
                into.Add(d);
                nextRepeat = now + FirstRepeatMs;
            }
        }
        else if (dir is { } d && now >= nextRepeat)
        {
            into.Add(d);
            nextRepeat += RepeatMs;
            if (nextRepeat <= now) nextRepeat = now + RepeatMs;   // after a stall, carry on at the normal pace rather than in a burst
        }

        var pressed = state.Buttons & ~last;
        last = state.Buttons;
        if (pressed.HasFlag(PadButtons.A)) into.Add(PadAction.Activate);
        if (pressed.HasFlag(PadButtons.B)) into.Add(PadAction.Back);
        if (pressed.HasFlag(PadButtons.Start)) into.Add(PadAction.Menu);
        if (pressed.HasFlag(PadButtons.LeftShoulder)) into.Add(PadAction.Previous);
        if (pressed.HasFlag(PadButtons.RightShoulder)) into.Add(PadAction.Next);
    }

    /// The way the d-pad or else the stick points, if any (the d-pad wins; on a diagonal, up/down beats left/right)
    PadAction? Direction(PadState s)
    {
        if (s.Buttons.HasFlag(PadButtons.Up)) return PadAction.Up;
        if (s.Buttons.HasFlag(PadButtons.Down)) return PadAction.Down;
        if (s.Buttons.HasFlag(PadButtons.Left)) return PadAction.Left;
        if (s.Buttons.HasFlag(PadButtons.Right)) return PadAction.Right;

        double x = s.StickX / 32767.0, y = s.StickY / 32767.0;
        double ax = Math.Abs(x), ay = Math.Abs(y);
        var dir = ay >= ax ? (y < 0 ? PadAction.Up : PadAction.Down) : (x < 0 ? PadAction.Left : PadAction.Right);
        return Math.Max(ax, ay) >= (dir == held ? Release : Press) ? dir : null;
    }
}
