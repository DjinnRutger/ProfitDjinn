using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace ProfitDjinn.App.Controls;

/// <summary>
/// A ComboBox whose choice UI Automation can set by its label (the item's ToString), without
/// opening the list. Screen readers get a value to read and change, and the smoke tests can run
/// in a window that never has focus, where an opened list would close at once.
/// </summary>
public sealed class Dropdown : ComboBox
{
    public Dropdown() => SetResourceReference(StyleProperty, typeof(ComboBox));

    protected override AutomationPeer OnCreateAutomationPeer() => new DropdownPeer(this);

    private sealed class DropdownPeer : ComboBoxAutomationPeer, IValueProvider
    {
        public DropdownPeer(Dropdown owner) : base(owner) { }

        private Dropdown Box => (Dropdown)Owner;

        public override object GetPattern(PatternInterface pattern) =>
            pattern == PatternInterface.Value && !Box.IsEditable ? this : base.GetPattern(pattern);

        public string Value => Box.SelectedItem?.ToString() ?? "";

        public bool IsReadOnly => !Box.IsEnabled;

        public void SetValue(string value)
        {
            if (!Box.IsEnabled) throw new ElementNotEnabledException();
            for (int i = 0; i < Box.Items.Count; i++)
            {
                if (string.Equals(Box.Items[i]?.ToString(), value, StringComparison.Ordinal))
                {
                    Box.SelectedIndex = i;
                    return;
                }
            }
            throw new ArgumentException($"No choice named '{value}'.");
        }
    }
}
