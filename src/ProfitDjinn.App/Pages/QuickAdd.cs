using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// 2.6. Add a customer or vendor from the invoice or expense form without leaving it: a dialog
/// with the typed name filled in. Only the name is required; the rest can be filled in later on
/// the customer or vendor page.
///
/// Never Reload here: the form being filled in is rebuilt from the database by Reload, so a
/// half-typed invoice would be lost. The caller adds the returned item to its picker.
/// </summary>
public static class QuickAdd
{
    /// <summary>
    /// The customer picker for the invoice forms: active customers, plus the record's own customer
    /// if it has since been made inactive (marked). Add Customer opens the quick-add dialog and
    /// picks the new customer.
    /// </summary>
    public static RecordPicker CustomerPicker(MainWindow shell, long? selected, Core.Model.Customer? current)
    {
        var items = shell.Store.Customers.ActiveForPicker().Select(c => new PickItem(c.Id, c.Name, c.Attn)).ToList();
        if (current is not null && items.All(i => i.Id != current.Id)) items.Add(new PickItem(current.Id, current.Name + " (inactive)", current.Attn));
        var picker = new RecordPicker(items, selected, "Type a customer name", "Add Customer");
        picker.AddRequested += async name =>
        {
            if (await Customer(shell, name) is { } made) picker.Add(made);
            picker.FocusBox();
        };
        return picker;
    }

    public const string CustomerProblem = "No customer has that name. Pick one from the list, or click Add Customer.";

    /// <summary>
    /// The vendor picker for the expense forms, with Add Vendor wired up. <paramref name="added"/>
    /// gets a quick-added vendor's id and default category, so the form can learn and apply it.
    /// </summary>
    public static RecordPicker VendorPicker(MainWindow shell, long? current, Action<long, long?> added)
    {
        var picker = ExpenseUi.VendorPicker(shell.Store, current);
        picker.AddRequested += async name =>
        {
            if (await Vendor(shell, name) is { } made)
            {
                added(made.Item.Id, made.DefaultCategory);
                picker.Add(made.Item);
            }
            picker.FocusBox();
        };
        return picker;
    }

    public static async Task<PickItem?> Customer(MainWindow shell, string typedName)
    {
        var name = Ui.TextBox(typedName);
        var attn = Ui.TextBox(null);
        var email = Ui.TextBox(null);
        var phone = Ui.TextBox(null);
        var address = Ui.TextBox(null);
        var city = Ui.TextBox(null);
        var state = Ui.TextBox(null).Also(t => { t.MaxLength = 2; t.CharacterCasing = CharacterCasing.Upper; });
        var zip = Ui.TextBox(null);
        var fields = new Dictionary<string, Field>
        {
            ["name"] = Ui.Field("Company / Name", name, required: true),
            ["attn"] = Ui.Field("Attn / Contact", attn),
            ["email"] = Ui.Field("Email", email),
            ["phone"] = Ui.Field("Phone", phone),
            ["address"] = Ui.Field("Address", address),
            ["city"] = Ui.Field("City", city),
            ["state"] = Ui.Field("State", state),
            ["zip_code"] = Ui.Field("ZIP", zip),
        };
        var body = Ui.Stack(0,
            fields["name"],
            Ui.Columns(16, (Ui.Star(), fields["attn"]), (Ui.Star(), fields["email"])),
            Ui.Columns(16, (Ui.Star(), fields["phone"]), (Ui.Star(), new System.Windows.Controls.Border())),
            fields["address"],
            Ui.Columns(16, (Ui.Star(2), fields["city"]), (Ui.Star(), fields["state"]), (Ui.Star(), fields["zip_code"])),
            Ui.Muted("Only the name is needed now. The address prints on the invoice; add the rest any time on the customer's page.", 12.8)
                .Also(t => t.TextWrapping = System.Windows.TextWrapping.Wrap));

        PickItem? made = null;
        bool ok = await shell.OpenDialog("Add Customer", "person-plus", body, "Add Customer", () =>
        {
            foreach (var f in fields.Values) f.Error = null;
            try
            {
                var created = shell.Store.Customers.Create(new CustomerDraft(name.Text, attn.Text, address.Text, city.Text, state.Text, zip.Text,
                    phone.Text, email.Text, "", true));
                made = new PickItem(created.Id, name.Text.Trim(), attn.Text.Trim());
                shell.ShowNotice(created.Notice);
                return true;
            }
            catch (ValidationException v) { return ShowErrors(v, fields); }
            catch (UserFacingException ex) { fields["name"].Error = ex.Message; return false; }
        }, "Btn.Success", "check-lg", maxWidth: 560);
        return ok ? made : null;
    }

    public static async Task<(PickItem Item, long? DefaultCategory)?> Vendor(MainWindow shell, string typedName)
    {
        var name = Ui.TextBox(typedName);
        var contact = Ui.TextBox(null);
        var email = Ui.TextBox(null);
        var phone = Ui.TextBox(null);
        var category = ExpenseUi.CategoryPicker(shell.Store, null, allowNone: true);
        var fields = new Dictionary<string, Field>
        {
            ["name"] = Ui.Field("Vendor Name", name, required: true),
            ["contact"] = Ui.Field("Contact", contact),
            ["email"] = Ui.Field("Email", email),
            ["phone"] = Ui.Field("Phone", phone),
            ["default_category_id"] = Ui.Field("Default Category", category, hint: "Filled in on new expenses from this vendor."),
        };
        var body = Ui.Stack(0,
            fields["name"],
            Ui.Columns(16, (Ui.Star(), fields["contact"]), (Ui.Star(), fields["phone"])),
            fields["email"],
            fields["default_category_id"],
            Ui.Muted("Only the name is needed now; add the address and notes any time on the vendor's page.", 12.8)
                .Also(t => t.TextWrapping = System.Windows.TextWrapping.Wrap));

        (PickItem, long?)? made = null;
        bool ok = await shell.OpenDialog("Add Vendor", "shop", body, "Add Vendor", () =>
        {
            foreach (var f in fields.Values) f.Error = null;
            long? cat = ExpenseUi.SelectedId(category);
            try
            {
                var created = shell.Store.Vendors.Create(new VendorDraft(name.Text, contact.Text, "", "", "", "", phone.Text, email.Text, cat, "", true));
                made = (new PickItem(created.Id, name.Text.Trim(), contact.Text.Trim()), cat);
                shell.ShowNotice(created.Notice);
                return true;
            }
            catch (ValidationException v) { return ShowErrors(v, fields); }
            catch (UserFacingException ex) { fields["name"].Error = ex.Message; return false; }
        }, "Btn.Success", "check-lg", maxWidth: 520);
        return ok ? made : null;
    }

    private static bool ShowErrors(ValidationException v, Dictionary<string, Field> fields)
    {
        foreach (var (key, message) in v.Fields)
            (fields.GetValueOrDefault(key) ?? fields["name"]).Error = message;
        return false;
    }
}
