namespace Danslicer.App.Input;

internal static class SpaceMouseButtons
{
    public static SixAxisButton Map(int code) => code switch
    {
        1 => SixAxisButton.Menu, 2 => SixAxisButton.Fit,
        3 => SixAxisButton.ViewTop, 4 => SixAxisButton.ViewLeft,
        5 => SixAxisButton.ViewRight, 6 => SixAxisButton.ViewFront,
        7 => SixAxisButton.ViewBottom, 8 => SixAxisButton.ViewBack,
        9 => SixAxisButton.RollClockwise, 10 => SixAxisButton.RollCounterClockwise,
        11 => SixAxisButton.Iso1, 12 => SixAxisButton.Iso2,
        13 => SixAxisButton.Key1, 14 => SixAxisButton.Key2,
        15 => SixAxisButton.Key3, 16 => SixAxisButton.Key4,
        23 => SixAxisButton.Escape, 24 => SixAxisButton.Alt,
        25 => SixAxisButton.Shift, 26 => SixAxisButton.Ctrl,
        27 => SixAxisButton.RotationLock,
        _ => SixAxisButton.Unknown,
    };

    // Older devices report physical button positions, not V3DKey codes.
    public static int FromBit(int bit, uint product) => product switch
    {
        0xC621 or 0xC623 => bit + 13, // Legacy devices: generic numbered buttons.
        0xC627 => bit switch // SpaceExplorer
        {
            0 => 13, 1 => 14, 2 => 3, 3 => 4, 4 => 5, 5 => 6,
            6 => 23, 7 => 24, 8 => 25, 9 => 26, 10 => 2, 11 => 1,
            12 => 30, 13 => 31, 14 => 27, _ => 0,
        },
        0xC625 => bit switch // SpacePilot (not Pro)
        {
            >= 0 and <= 5 => bit + 13,
            6 => 3, 7 => 4, 8 => 5, 9 => 6, 10 => 23, 11 => 24,
            12 => 25, 13 => 26, 14 => 2, 15 => 1, 16 => 30,
            17 => 31, 18 => 29, 19 => 27, _ => 0,
        },
        _ => bit + 1, // SpaceNavigator, SpaceMouse Pro and current wireless models
    };

    public static int FromLongPress(int code) => code switch
    {
        3 => 7, 5 => 4, 6 => 8, 9 => 10, 11 => 12,
        32 => 33, 34 => 35, 103 => 139, 104 => 140, 105 => 141,
        _ => code,
    };
}
