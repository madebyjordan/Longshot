using System.Runtime.InteropServices;

namespace Hookshot.Launcher;

/// The few SDL2 calls the controller support needs (SDL2 is zlib-licensed). They are looked up when the launcher starts from the
/// first SDL2 library that loads, so a missing library only means no controller support: Load gives null and nothing else changes.
/// The SDL2 beside the launcher comes first (Hookshot.csproj copies it there, from the Ultz.Native.SDL package), then the system's.
sealed class Sdl
{
    public const uint InitGameController = 0x2000;   // SDL_INIT_GAMECONTROLLER (it brings the joystick and events parts with it)
    public const int Ignore = 0;                     // SDL_IGNORE: no events queued for controllers, which are read directly instead

    // SDL_GameControllerButton and SDL_GameControllerAxis values
    public const int ButtonA = 0, ButtonB = 1, ButtonStart = 6, ButtonLeftShoulder = 9, ButtonRightShoulder = 10,
                     ButtonDpadUp = 11, ButtonDpadDown = 12, ButtonDpadLeft = 13, ButtonDpadRight = 14;
    public const int AxisLeftX = 0, AxisLeftY = 1;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int InitFn(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void VoidFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int SetHintFn([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int IntFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int IndexFn(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr OpenFn(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void CloseFn(IntPtr controller);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int AttachedFn(IntPtr controller);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate byte ButtonFn(IntPtr controller, int button);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate short AxisFn(IntPtr controller, int axis);

    public readonly SetHintFn SetHint;
    public readonly InitFn Init;
    public readonly VoidFn Quit;
    public readonly IndexFn GameControllerEventState, JoystickEventState;   // (int state) -> previous state
    public readonly VoidFn GameControllerUpdate;
    public readonly IntFn NumJoysticks;
    public readonly IndexFn IsGameController, JoystickGetDeviceInstanceID;
    public readonly OpenFn GameControllerOpen;
    public readonly CloseFn GameControllerClose;
    public readonly AttachedFn GameControllerGetAttached;
    public readonly ButtonFn GameControllerGetButton;
    public readonly AxisFn GameControllerGetAxis;

    Sdl(IntPtr lib)
    {
        T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
        SetHint = Get<SetHintFn>("SDL_SetHint");
        Init = Get<InitFn>("SDL_Init");
        Quit = Get<VoidFn>("SDL_Quit");
        GameControllerEventState = Get<IndexFn>("SDL_GameControllerEventState");
        JoystickEventState = Get<IndexFn>("SDL_JoystickEventState");
        GameControllerUpdate = Get<VoidFn>("SDL_GameControllerUpdate");
        NumJoysticks = Get<IntFn>("SDL_NumJoysticks");
        IsGameController = Get<IndexFn>("SDL_IsGameController");
        JoystickGetDeviceInstanceID = Get<IndexFn>("SDL_JoystickGetDeviceInstanceID");
        GameControllerOpen = Get<OpenFn>("SDL_GameControllerOpen");
        GameControllerClose = Get<CloseFn>("SDL_GameControllerClose");
        GameControllerGetAttached = Get<AttachedFn>("SDL_GameControllerGetAttached");
        GameControllerGetButton = Get<ButtonFn>("SDL_GameControllerGetButton");
        GameControllerGetAxis = Get<AxisFn>("SDL_GameControllerGetAxis");
    }

    /// The first SDL2 library that loads and has every call above, or null
    public static Sdl? Load()
    {
        foreach (var name in Candidates())
        {
            if (!NativeLibrary.TryLoad(name, out var lib)) continue;
            try { return new Sdl(lib); }
            catch (EntryPointNotFoundException) { NativeLibrary.Free(lib); }   // too old or not SDL2 after all: try the next
        }
        return null;
    }

    /// Where SDL2 may be: beside the launcher first, then (not on Windows, where only the copy we ship is trusted) the system's
    static IEnumerable<string> Candidates()
    {
        var here = AppContext.BaseDirectory;
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(here, "SDL2.dll");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(here, "libSDL2-2.0.dylib");
            yield return "libSDL2-2.0.0.dylib";
        }
        else
        {
            yield return Path.Combine(here, "libSDL2-2.0.so");
            yield return "libSDL2-2.0.so.0";
            yield return "libSDL2.so";
        }
    }
}
