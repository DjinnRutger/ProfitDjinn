using Dapper;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.Core.Services;

/// <summary>The service-item price list. A port of 1.x app/blueprints/items.py.</summary>
public sealed class ItemService
{
    private readonly Database _db;

    public ItemService(Database db) => _db = db;

    public IReadOnlyList<ServiceItem> List(bool includeInactive) => _db.Run(db => db.Query<ServiceItem>(
        includeInactive
            ? "SELECT * FROM service_items ORDER BY description"
            : "SELECT * FROM service_items WHERE is_active = 1 ORDER BY description").ToList());

    public IReadOnlyList<ServiceItem> Active() => List(includeInactive: false);

    public ServiceItem Get(long id) =>
        _db.Run(db => db.QuerySingleOrDefault<ServiceItem>("SELECT * FROM service_items WHERE id = @id", new { id })) ?? throw NotFound();

    public Created Create(string description, double? price, bool isActive)
    {
        string desc = Validate(description, price);
        long id = _db.Run(db => db.ExecuteScalar<long>(
            "INSERT INTO service_items (description, price, is_active) VALUES (@desc, @price, @isActive); SELECT last_insert_rowid();",
            new { desc, price, isActive }));
        return new Created(id, Notice.Success($"Item '{desc}' created."));
    }

    public Notice Update(long id, string description, double? price, bool isActive)
    {
        string desc = Validate(description, price);
        int n = _db.Run(db => db.Execute(
            "UPDATE service_items SET description = @desc, price = @price, is_active = @isActive WHERE id = @id",
            new { desc, price, isActive, id }));
        if (n == 0) throw NotFound();
        return Notice.Success($"Item '{desc}' updated.");
    }

    public Notice ToggleActive(long id)
    {
        var item = Get(id);
        _db.Run(db => db.Execute("UPDATE service_items SET is_active = @active WHERE id = @id", new { active = !item.IsActive, id }));
        return Notice.Info($"Item '{item.Description}' {(item.IsActive ? "deactivated" : "activated")}.");
    }

    public Notice Delete(long id)
    {
        var item = Get(id);
        _db.Run(db => db.Execute("DELETE FROM service_items WHERE id = @id", new { id }));
        return Notice.Warning($"Item '{item.Description}' deleted.");
    }

    /// <summary>
    /// Description required (500 max), price 0 or more. Fixed in 2.0: a $0 price is allowed;
    /// 1.x rejected it because its "required" check treated 0 as empty.
    /// </summary>
    private static string Validate(string description, double? price)
    {
        var checks = new Checks();
        checks.RequireText("description", description, 500);
        if (price is null || double.IsNaN(price.Value)) checks.Add("price", Checks.Required);
        else if (price < 0) checks.Add("price", "Number must be at least 0.");
        checks.ThrowIfAny();
        return description.Trim();
    }

    private static UserFacingException NotFound() => new("That item no longer exists. It may have been deleted.");
}
