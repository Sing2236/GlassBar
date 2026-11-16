using System.Windows;

namespace GlassBar.Controls;

/// <summary>
/// Marks one option button in a choice group as the current value. Buttons use
/// their Tag for the value (which is what the existing click handlers read), so
/// selection state needs its own attached property rather than overloading Tag
/// the way the old nav rail did.
/// </summary>
public static class Choice
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsSelected",
            typeof(bool),
            typeof(Choice),
            new FrameworkPropertyMetadata(false));

    public static bool GetIsSelected(DependencyObject element) =>
        (bool)element.GetValue(IsSelectedProperty);

    public static void SetIsSelected(DependencyObject element, bool value) =>
        element.SetValue(IsSelectedProperty, value);
}
