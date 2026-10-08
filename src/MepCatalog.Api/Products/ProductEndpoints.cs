using MepCatalog.Core;
using MepCatalog.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MepCatalog.Api.Products;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products").WithTags("Products");

        group.MapGet("/", Search).WithSummary("Search the catalog with optional category filter and paging");
        group.MapGet("/{id:int}", GetById);
        group.MapGet("/lookup", Lookup).WithSummary("Find one product by exact manufacturer + model");
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);
        group.MapPost("/import", Import)
            .WithSummary("Import a manufacturer CSV file")
            .DisableAntiforgery();
        group.MapGet("/categories", () => Enum.GetNames<ProductCategory>().Where(c => c != nameof(ProductCategory.Unknown)));
    }

    private static async Task<Ok<PagedResult<ProductDto>>> Search(
        CatalogDbContext db,
        string? search,
        ProductCategory? category,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Products.AsNoTracking();
        if (category is { } c and not ProductCategory.Unknown)
            query = query.Where(p => p.Category == c);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                p.Manufacturer.ToLower().Contains(term) ||
                p.Model.ToLower().Contains(term) ||
                (p.Description != null && p.Description.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Manufacturer).ThenBy(p => p.Model)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<ProductDto>(items.Select(ProductDto.From).ToList(), page, pageSize, total));
    }

    private static async Task<Results<Ok<ProductDto>, NotFound>> GetById(int id, CatalogDbContext db, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(ProductDto.From(product));
    }

    private static async Task<Results<Ok<ProductDto>, NotFound>> Lookup(
        string manufacturer, string model, CatalogDbContext db, CancellationToken ct)
    {
        var m = manufacturer.Trim().ToUpper();
        var code = model.Trim().ToUpperInvariant();
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Manufacturer.ToUpper() == m && p.Model == code, ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(ProductDto.From(product));
    }

    private static async Task<Results<Created<ProductDto>, ValidationProblem, Conflict<string>>> Create(
        ProductInput input, CatalogDbContext db, CancellationToken ct)
    {
        var errors = input.Validate();
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var product = new Product { Manufacturer = "", Model = "" };
        input.ApplyTo(product);

        if (await IsDuplicate(db, product, ct))
            return TypedResults.Conflict($"{product.Manufacturer} {product.Model} already exists.");

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/products/{product.Id}", ProductDto.From(product));
    }

    private static async Task<Results<Ok<ProductDto>, NotFound, ValidationProblem, Conflict<string>>> Update(
        int id, ProductInput input, CatalogDbContext db, CancellationToken ct)
    {
        var errors = input.Validate();
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var product = await db.Products.FindAsync([id], ct);
        if (product is null)
            return TypedResults.NotFound();

        input.ApplyTo(product);
        if (await IsDuplicate(db, product, ct))
            return TypedResults.Conflict($"{product.Manufacturer} {product.Model} already exists.");

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ProductDto.From(product));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, CatalogDbContext db, CancellationToken ct)
    {
        var deleted = await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ImportReport>, BadRequest<string>>> Import(
        IFormFile file, ProductImportService importer, CancellationToken ct)
    {
        if (file.Length == 0)
            return TypedResults.BadRequest("The file is empty.");

        using var reader = new StreamReader(file.OpenReadStream());
        List<RawProductRow> rows;
        try
        {
            rows = CsvProductReader.Read(reader);
        }
        catch (Exception ex) when (ex is CsvHelper.CsvHelperException or FormatException)
        {
            return TypedResults.BadRequest($"Could not read the CSV file: {ex.Message}");
        }

        return TypedResults.Ok(await importer.ImportAsync(rows, ct));
    }

    private static Task<bool> IsDuplicate(CatalogDbContext db, Product product, CancellationToken ct)
    {
        var m = product.Manufacturer.ToUpper();
        return db.Products.AnyAsync(p => p.Id != product.Id && p.Manufacturer.ToUpper() == m && p.Model == product.Model, ct);
    }
}
