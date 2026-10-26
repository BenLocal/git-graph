using Avalonia.Input;
using Iciclecreek.Terminal;

namespace GitGraph.App.Controls;

/// <summary>Leaves composition keystrokes to the platform input method.</summary>
public sealed class ImeTerminalView : TerminalView
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Avalonia keeps KeySymbol on ImeProcessed events. TerminalView 4.1.x
        // sends that symbol to the PTY and marks it handled, blocking the IME.
        // Committed text still reaches the inherited OnTextInput implementation.
        if (e.Key == Key.ImeProcessed) return;
        base.OnKeyDown(e);
    }
}
