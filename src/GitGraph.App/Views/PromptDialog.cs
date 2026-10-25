using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GitGraph.App.Services;

namespace GitGraph.App.Views;

/// <summary>A small modal for confirmations and short forms. Closes with the field values, or null when cancelled.</summary>
public sealed class PromptDialog : Window
{
    private readonly List<Control> _inputs = [];

    public PromptDialog(string title, string? message, IReadOnlyList<PromptField> fields, string confirmText, bool destructive)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Spacing = 14 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold });
        if (!string.IsNullOrEmpty(message))
            stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });

        foreach (var f in fields)
        {
            if (f.IsCheckbox)
            {
                var cb = new CheckBox { Content = f.Label, IsChecked = f.Value == "true" };
                _inputs.Add(cb);
                stack.Children.Add(cb);
                continue;
            }
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock { Text = f.Label, FontWeight = FontWeight.Medium, FontSize = 12 });
            var tb = new TextBox { Text = f.Value, PlaceholderText = f.Placeholder };
            // A multiline field takes Enter as a new line, and Ctrl+Enter confirms.
            if (f.IsMultiline)
            {
                tb.AcceptsReturn = true;
                tb.TextWrapping = TextWrapping.Wrap;
                tb.MinHeight = 96;
                tb.MaxHeight = 260;
            }
            // Tunnel, ahead of the text box, which takes every Enter (Ctrl+Enter too) as a new line when it accepts returns.
            tb.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.Enter && (!f.IsMultiline || e.KeyModifiers.HasFlag(KeyModifiers.Control))) { Confirm(); e.Handled = true; }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _inputs.Add(tb);
            group.Children.Add(tb);
            if (f.Hint != null) group.Children.Add(new TextBlock { Text = f.Hint, FontSize = 11.5, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(group);
        }

        var cancel = new Button { Content = "Cancel", Classes = { "secondary" }, IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        var ok = new Button { Content = confirmText, Classes = { "primary" }, IsDefault = fields.Count == 0 };
        if (destructive) ok.Classes.Add("danger");
        ok.Click += (_, _) => Confirm();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { cancel, ok },
        };
        stack.Children.Add(buttons);
        Content = stack;

        Opened += (_, _) =>
        {
            if (_inputs.OfType<TextBox>().FirstOrDefault() is { } first)
            {
                first.Focus();
                first.SelectAll();
            }
            else ok.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
        };
    }

    private void Confirm()
    {
        Close(_inputs.Select(c => c switch
        {
            TextBox t => t.Text ?? "",
            CheckBox cb => cb.IsChecked == true ? "true" : "false",
            _ => "",
        }).ToArray());
    }
}
