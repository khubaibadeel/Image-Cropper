using System.Windows;
using System.Windows.Media;
using System.Windows.Input;

namespace PrecisionImageCropper.Dialogs;

public enum DialogVariant { Information, Warning, Error, Confirmation, Destructive }

public partial class AppDialogWindow : Window
{
    public AppDialogWindow() => InitializeComponent();

    public static bool Confirm(Window owner, string title, string body, string secondary, string action = "Remove")
    {
        var dialog = new AppDialogWindow { Owner = owner };
        dialog.Configure(DialogVariant.Destructive, title, body, secondary, action);
        return dialog.ShowDialog() == true;
    }

    public static void ShowNotice(Window owner, string title, string body, string secondary = "")
    {
        var dialog = new AppDialogWindow { Owner = owner };
        dialog.Configure(DialogVariant.Information, title, body, secondary, "OK");
        dialog.CancelButton.Visibility = Visibility.Collapsed;
        dialog.ConfirmButton.Style = (Style)dialog.FindResource("PrimaryButton");
        dialog.ConfirmButton.IsDefault = true;
        dialog.ShowDialog();
    }

    public static void ShowError(Window owner, string title, string body, string secondary = "")
    {
        var dialog = new AppDialogWindow { Owner = owner };
        dialog.Configure(DialogVariant.Error, title, body, secondary, "OK");
        dialog.CancelButton.Visibility = Visibility.Collapsed;
        dialog.ConfirmButton.Style = (Style)dialog.FindResource("PrimaryButton");
        dialog.ConfirmButton.IsDefault = true;
        dialog.ShowDialog();
    }

    private void Configure(DialogVariant variant, string title, string body, string secondary, string action)
    {
        TitleText.Text = title;
        BodyText.Text = body;
        SecondaryText.Text = secondary;
        ConfirmButton.Content = action;
        var (icon, background, foreground) = variant switch
        {
            DialogVariant.Information => ("IconInfo", "#EAF3FF", "#0D6EFD"),
            DialogVariant.Warning => ("IconWarning", "#FFF7E5", "#A86A00"),
            DialogVariant.Error => ("IconError", "#FFF0F1", "#C43C43"),
            DialogVariant.Confirmation => ("IconInfo", "#EAF3FF", "#0D6EFD"),
            _ => ("IconTrash", "#FFF1F1", "#D5484D")
        };
        DialogIcon.Data = (Geometry)FindResource(icon);
        DialogIcon.Stroke = (Brush)new BrushConverter().ConvertFromString(foreground)!;
        IconBadge.Background = (Brush)new BrushConverter().ConvertFromString(background)!;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        base.OnKeyDown(e);
    }
}
