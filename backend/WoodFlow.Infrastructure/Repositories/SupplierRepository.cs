using System.Data;
using Npgsql;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class SupplierRepository(Database database) : ISupplierRepository
{
    private static Supplier ReadSupplier(NpgsqlDataReader r) => new(
        r.GetGuid(0),
        r.GetString(1),
        r.GetString(2),
        r.GetString(3),
        r.GetString(4),
        r.GetString(5),
        r.GetString(6),
        r.GetString(7),
        r.GetBoolean(8),
        r.GetDecimal(9),
        r.GetInt32(10),
        r.GetDecimal(11),
        r.GetFieldValue<DateTimeOffset>(12)
    );

    private const string SupplierSelect = """
        s.supplier_id, s.supplier_code, s.company_name, s.contact_person,
        s.phone, s.email, s.address, s.primary_category, s.is_active, s.rating,
        COUNT(ms.material_id)::int AS materials_supplied_count,
        COALESCE(AVG(ms.lead_time_days), 3.0)::numeric(5,1) AS avg_lead_time_days,
        s.created_at
        """;

    public async Task<IReadOnlyList<Supplier>> ListSuppliersAsync(string? category, string? search, bool includeInactive, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);

        var sql = $"""
            SELECT {SupplierSelect}
            FROM public.suppliers s
            LEFT JOIN public.material_suppliers ms ON s.supplier_id = ms.supplier_id
            WHERE (@includeInactive OR s.is_active IS TRUE)
              AND (@category IS NULL OR @category = '' OR s.primary_category ILIKE @category)
              AND (@search IS NULL OR @search = '' OR (s.supplier_code ILIKE @searchPattern OR s.company_name ILIKE @searchPattern))
            GROUP BY s.supplier_id
            ORDER BY s.company_name ASC
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("includeInactive", includeInactive);
        cmd.Parameters.AddWithValue("category", (object?)category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("search", (object?)search ?? DBNull.Value);
        cmd.Parameters.AddWithValue("searchPattern", string.IsNullOrWhiteSpace(search) ? DBNull.Value : $"%{search.Trim()}%");

        var list = new List<Supplier>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(ReadSupplier(r));
        }
        return list;
    }

    public async Task<Supplier?> FindByIdAsync(Guid supplierId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);

        var sql = $"""
            SELECT {SupplierSelect}
            FROM public.suppliers s
            LEFT JOIN public.material_suppliers ms ON s.supplier_id = ms.supplier_id
            WHERE s.supplier_id = @id
            GROUP BY s.supplier_id
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", supplierId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadSupplier(r) : null;
    }

    public async Task<IReadOnlyList<MaterialSupplierMapping>> GetSupplierMaterialsAsync(Guid supplierId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);

        const string sql = """
            SELECT
                ms.material_id, ms.supplier_id, m.name, m.material_code,
                ms.unit_price, ms.lead_time_days, ms.is_preferred
            FROM public.material_suppliers ms
            JOIN public.raw_materials m ON ms.material_id = m.material_id
            WHERE ms.supplier_id = @sId
            ORDER BY m.name ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("sId", supplierId);

        var list = new List<MaterialSupplierMapping>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new MaterialSupplierMapping(
                r.GetGuid(0),
                r.GetGuid(1),
                r.GetString(2),
                r.GetString(3),
                r.GetDecimal(4),
                r.GetInt32(5),
                r.GetBoolean(6)
            ));
        }
        return list;
    }

    public async Task<Supplier> CreateAsync(Supplier supplier, Guid? initialMaterialId, decimal? initialUnitPrice, int? initialLeadTimeDays, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            const string sql = """
                INSERT INTO public.suppliers (
                    supplier_id, supplier_code, company_name, contact_person,
                    phone, email, address, primary_category, is_active, rating, created_at
                ) VALUES (
                    @id, @code, @name, @contact,
                    @phone, @email, @addr, @cat, @active, @rating, @created
                );
                """;

            await using (var cmd = new NpgsqlCommand(sql, connection, tx))
            {
                cmd.Parameters.AddWithValue("id", supplier.SupplierId);
                cmd.Parameters.AddWithValue("code", supplier.SupplierCode);
                cmd.Parameters.AddWithValue("name", supplier.CompanyName);
                cmd.Parameters.AddWithValue("contact", supplier.ContactPerson);
                cmd.Parameters.AddWithValue("phone", supplier.Phone);
                cmd.Parameters.AddWithValue("email", supplier.Email);
                cmd.Parameters.AddWithValue("addr", supplier.Address);
                cmd.Parameters.AddWithValue("cat", supplier.PrimaryCategory);
                cmd.Parameters.AddWithValue("active", supplier.IsActive);
                cmd.Parameters.AddWithValue("rating", supplier.Rating);
                cmd.Parameters.AddWithValue("created", supplier.CreatedAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            if (initialMaterialId.HasValue)
            {
                const string mapSql = """
                    INSERT INTO public.material_suppliers (material_id, supplier_id, unit_price, lead_time_days, is_preferred)
                    VALUES (@mId, @sId, @price, @lead, FALSE)
                    ON CONFLICT (material_id, supplier_id) DO NOTHING;
                    """;
                await using var mapCmd = new NpgsqlCommand(mapSql, connection, tx);
                mapCmd.Parameters.AddWithValue("mId", initialMaterialId.Value);
                mapCmd.Parameters.AddWithValue("sId", supplier.SupplierId);
                mapCmd.Parameters.AddWithValue("price", initialUnitPrice ?? 0.00m);
                mapCmd.Parameters.AddWithValue("lead", initialLeadTimeDays ?? 3);
                await mapCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return supplier;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<Supplier?> UpdateAsync(Supplier supplier, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);

        const string sql = """
            UPDATE public.suppliers
            SET company_name = @name,
                contact_person = @contact,
                phone = @phone,
                email = @email,
                address = @addr,
                primary_category = @cat,
                is_active = @active
            WHERE supplier_id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("name", supplier.CompanyName);
        cmd.Parameters.AddWithValue("contact", supplier.ContactPerson);
        cmd.Parameters.AddWithValue("phone", supplier.Phone);
        cmd.Parameters.AddWithValue("email", supplier.Email);
        cmd.Parameters.AddWithValue("addr", supplier.Address);
        cmd.Parameters.AddWithValue("cat", supplier.PrimaryCategory);
        cmd.Parameters.AddWithValue("active", supplier.IsActive);
        cmd.Parameters.AddWithValue("id", supplier.SupplierId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0 ? supplier : null;
    }
}
