using Avalonia.Input;
using Edit.ComponentModel;

namespace Edit.Shell;

/// <summary>
/// Maps Avalonia key events to registered command ids using Ctrl/Alt/Shift + key gestures.
/// </summary>
public static class KeybindingDispatcher
{
    public static string? Match(KeyEventArgs e, ICommandRegistry commands, IReadOnlyDictionary<string, string>? keybindings = null)
    {
        if (e.Key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return null;

        var chord = FormatChord(e);

        if (keybindings is not null)
        {
            foreach (var (gesture, commandId) in keybindings)
            {
                if (string.Equals(Normalize(gesture), chord, StringComparison.OrdinalIgnoreCase))
                    return commandId;
            }
        }

        foreach (var command in commands.All)
        {
            if (command.KeyGesture is null) continue;
            if (string.Equals(Normalize(command.KeyGesture), chord, StringComparison.OrdinalIgnoreCase))
                return command.Id;
        }

        return null;
    }

    public static string FormatChord(KeyEventArgs e)
    {
        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Meta");
        parts.Add(FormatKey(e.Key));
        return string.Join('+', parts);
    }

    private static string FormatKey(Key key) => key switch
    {
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.D0 => "0",
        Key.D1 => "1",
        Key.D2 => "2",
        Key.D3 => "3",
        Key.D4 => "4",
        Key.D5 => "5",
        Key.D6 => "6",
        Key.D7 => "7",
        Key.D8 => "8",
        Key.D9 => "9",
        _ => key.ToString()
    };

    private static string Normalize(string gesture) =>
        gesture.Replace(" ", "", StringComparison.Ordinal)
            .Replace("Control", "Ctrl", StringComparison.OrdinalIgnoreCase);
}
