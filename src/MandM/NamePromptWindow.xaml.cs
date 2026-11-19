using System.Windows;

namespace MandM;

public partial class NamePromptWindow : Window
{
    public string ResultName { get; private set; } = string.Empty;

    public NamePromptWindow(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        TxtPrompt.Text = prompt;
        TxtName.Text = initial;
        Loaded += (_, _) =>
        {
            TxtName.Focus();
            TxtName.SelectAll();
        };
    }

    public static string? Prompt(Window owner, string title, string prompt, string initial)
    {
        var dlg = new NamePromptWindow(title, prompt, initial) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.ResultName : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show(this, "Name is required.", "MandM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultName = TxtName.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
