using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;
using Xunit;

namespace WoodFlow.Tests;

// Verifies Showroom POS & Counter Billing RBAC, CSRF, Owner-only discount/void rules, 0-stock rollback, and Till Closing variance/locking.
public sealed class PosCounterTests
{
    private const string Password = "Pos counter test password 42!";

    private static async Task<string> Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        return Uri.UnescapeDataString(response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN="))
            .Split(';')[0]["XSRF-TOKEN=".Length..]);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, bool csrf = true)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (csrf) request.Headers.Add("X-XSRF-TOKEN", await Csrf(client));
        return await client.SendAsync(request);
    }

    private static async Task Login(HttpClient client, string username) =>
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, "/api/auth/login", new { username, password = Password })).StatusCode);

    [Fact]
    public async Task PosEndpointsEnforceAuthenticationCsrfAndSalesPermissions()
    {
        await using var factory = new PosFactory();
        using var anon = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/pos/context")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/pos/invoices")).StatusCode);

        using var assistant = factory.Client();
        await Login(assistant, "assistant");

        // Default assistant only has dashboard.view -> sales.view & sales.create are forbidden
        Assert.Equal(HttpStatusCode.Forbidden, (await assistant.GetAsync("/api/pos/context")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(assistant, HttpMethod.Post, "/api/pos/customers",
            new CreatePosCustomerRequest("Kamal Perera", "077 458 1290", "077 458 1290", "Kottawa"))).StatusCode);

        // Owner grants sales.view + sales.create
        using var owner = factory.Client();
        await Login(owner, "owner");
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant",
            new { expectedVersion = 0L, grantedKeys = new[] { "sales.view", "sales.create" }, confirmed = true })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await assistant.GetAsync("/api/pos/context")).StatusCode);

        // Missing CSRF header must be rejected on state-changing POS endpoint
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(assistant, HttpMethod.Post, "/api/pos/customers",
            new CreatePosCustomerRequest("Kamal Perera", "077 458 1290", "077 458 1290", "Kottawa"), csrf: false)).StatusCode);

        var createdCustomer = await Send(assistant, HttpMethod.Post, "/api/pos/customers",
            new CreatePosCustomerRequest("Kamal Perera", "077 458 1290", "077 458 1290", "No. 45, Pannipitiya Road, Kottawa"));
        Assert.Equal(HttpStatusCode.Created, createdCustomer.StatusCode);
    }

    [Fact]
    public async Task CheckoutEnforcesZeroStockRuleOwnerOnlyDiscountAndStockDeduction()
    {
        await using var factory = new PosFactory();
        using var owner = factory.Client();
        using var assistant = factory.Client();
        await Login(owner, "owner");
        await Login(assistant, "assistant");
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant",
            new { expectedVersion = 0L, grantedKeys = new[] { "sales.view", "sales.create" }, confirmed = true })).StatusCode);

        // 0-stock item (FF-WAR-003, productId = 4) must fail with 409 Conflict
        var outOfStockResp = await Send(assistant, HttpMethod.Post, "/api/pos/invoices", new CreatePosInvoiceRequest(
            CustomerId: 1,
            Items: [new CreatePosInvoiceItemRequest(4, 1)],
            PaymentMode: "CASH",
            PaymentReference: null,
            CashReceived: 150000m,
            DiscountCode: null,
            DiscountPercent: 0m,
            DiscountAmount: 0m));
        Assert.Equal(HttpStatusCode.Conflict, outOfStockResp.StatusCode);

        // Assistant attempting Owner-Only 10% seasonal discount must be rejected with 403 Forbidden
        var assistantDiscountResp = await Send(assistant, HttpMethod.Post, "/api/pos/invoices", new CreatePosInvoiceRequest(
            CustomerId: 1,
            Items: [new CreatePosInvoiceItemRequest(1, 1), new CreatePosInvoiceItemRequest(3, 1)],
            PaymentMode: "CASH",
            PaymentReference: null,
            CashReceived: 100000m,
            DiscountCode: "DISC-10",
            DiscountPercent: 10m,
            DiscountAmount: 0m));
        Assert.Equal(HttpStatusCode.Forbidden, assistantDiscountResp.StatusCode);

        // Owner checkout with 10% seasonal discount (45,000 + 62,500 = 107,500; -10,750 = 96,750; change = 3,250)
        var ownerSaleResp = await Send(owner, HttpMethod.Post, "/api/pos/invoices", new CreatePosInvoiceRequest(
            CustomerId: 1,
            Items: [new CreatePosInvoiceItemRequest(1, 1), new CreatePosInvoiceItemRequest(3, 1)],
            PaymentMode: "CASH",
            PaymentReference: null,
            CashReceived: 100000m,
            DiscountCode: "DISC-10 · 10% Seasonal",
            DiscountPercent: 10m,
            DiscountAmount: 0m));
        Assert.Equal(HttpStatusCode.Created, ownerSaleResp.StatusCode);

        var invoice = (await ownerSaleResp.Content.ReadFromJsonAsync<PosInvoiceDto>())!;
        Assert.Equal(107500m, invoice.SubtotalAmount);
        Assert.Equal(10750m, invoice.DiscountAmount);
        Assert.Equal(96750m, invoice.TotalAmount);
        Assert.Equal(3250m, invoice.ChangeDue);

        // Stock of product 1 (was 2) is now 1
        var context = (await owner.GetFromJsonAsync<PosCounterContextResponseDto>("/api/pos/context"))!;
        Assert.Equal(1, context.Products.Single(p => p.ProductId == 1).CurrentStock);

        // Assistant cannot void invoice (OwnerOnly policy -> 403 Forbidden)
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(assistant, HttpMethod.Post, $"/api/pos/invoices/{invoice.InvoiceId}/void",
            new VoidRefundInvoiceRequest("Customer requested cancellation", "1234"))).StatusCode);

        // Owner voids invoice with Security PIN -> restores stock back to 2
        var voidResp = await Send(owner, HttpMethod.Post, $"/api/pos/invoices/{invoice.InvoiceId}/void",
            new VoidRefundInvoiceRequest("Customer requested cancellation", "1234"));
        Assert.Equal(HttpStatusCode.OK, voidResp.StatusCode);
        var voided = (await voidResp.Content.ReadFromJsonAsync<PosInvoiceDto>())!;
        Assert.Equal("VOIDED", voided.InvoiceStatus);

        var afterVoid = (await owner.GetFromJsonAsync<PosCounterContextResponseDto>("/api/pos/context"))!;
        Assert.Equal(2, afterVoid.Products.Single(p => p.ProductId == 1).CurrentStock);
    }

    [Fact]
    public async Task TillClosingCalculatesExactDenominationVarianceAndLocksDay()
    {
        await using var factory = new PosFactory();
        using var owner = factory.Client();
        await Login(owner, "owner");

        // Record Rs. 148,500 cash sale (e.g. 2x 45,000 + 58,500 or 3x 45,000 -> let's check initial expected = 25,000 + cash sales)
        // With 25,000 opening float and 0 cash sales in memory, 5x 5,000 notes = 25,000 balanced
        var countResp = await Send(owner, HttpMethod.Put, "/api/pos/till/count",
            new TillDenominationRequest(5, 0, 0, 0, 0));
        Assert.Equal(HttpStatusCode.OK, countResp.StatusCode);
        var summary = (await countResp.Content.ReadFromJsonAsync<PosTillSummaryDto>())!;
        Assert.Equal(25000m, summary.CountedCashAmount);
        Assert.Equal(0m, summary.VarianceAmount);
        Assert.Equal("BALANCED", summary.VarianceStatus);

        var closeResp = await Send(owner, HttpMethod.Post, "/api/pos/till/close",
            new CloseTillDayRequest(5, 0, 0, 0, 0, "Wimalasiri (Owner)"));
        Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);
        var closed = (await closeResp.Content.ReadFromJsonAsync<PosTillSummaryDto>())!;
        Assert.Equal("CLOSED", closed.SessionStatus);

        // Once Till is CLOSED, new POS sales are blocked with 409 Conflict
        var postLockSale = await Send(owner, HttpMethod.Post, "/api/pos/invoices", new CreatePosInvoiceRequest(
            CustomerId: 1,
            Items: [new CreatePosInvoiceItemRequest(1, 1)],
            PaymentMode: "CASH",
            PaymentReference: null,
            CashReceived: 50000m,
            DiscountCode: null,
            DiscountPercent: 0m,
            DiscountAmount: 0m));
        Assert.Equal(HttpStatusCode.Conflict, postLockSale.StatusCode);
    }

    private sealed class PosFactory : WebApplicationFactory<Program>
    {
        private readonly AuthApiTests.MemoryUsers _users = new(Password);
        private readonly MemoryPermissions _permissions;
        private readonly MemoryPosRepository _posRepo = new();

        public PosFactory() => _permissions = new(_users);

        internal HttpClient Client() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IUserRepository>();
                services.RemoveAll<IPermissionRepository>();
                services.RemoveAll<IAuthSessionRepository>();
                services.RemoveAll<IPosCounterRepository>();
                services.AddSingleton<IUserRepository>(_users);
                services.AddSingleton<IPermissionRepository>(_permissions);
                services.AddSingleton<IAuthSessionRepository, TestAuthSessions>();
                services.AddSingleton<IPosCounterRepository>(_posRepo);
            });
        }
    }

    private sealed class MemoryPermissions(AuthApiTests.MemoryUsers users) : IPermissionRepository
    {
        private PermissionSettings _current = new(["dashboard.view"], 0);
        public Task<PermissionSettings> GetAsync(CancellationToken ct) => Task.FromResult(_current);
        public Task<PermissionSaveResult> SaveAsync(int actorId, string[] keys, long expectedVersion, CancellationToken ct)
        {
            if (expectedVersion != _current.Version) return Task.FromResult(new PermissionSaveResult(Error: PermissionSaveError.Conflict));
            _current = new(keys, _current.Version + 1);
            return Task.FromResult(new PermissionSaveResult(_current));
        }
        public Task<PermissionSettings> GetEffectiveAsync(int id, CancellationToken ct) => Task.FromResult(_current);
        public Task<UserPermissionSettings?> GetUserAsync(int id, CancellationToken ct)
        {
            var user = users.Users.GetValueOrDefault(id);
            if (user?.Role != Roles.Assistant) return Task.FromResult<UserPermissionSettings?>(null);
            return Task.FromResult<UserPermissionSettings?>(new(new(user.UserId, user.FullName, user.Username, user.Role, "ACTIVE", null, user.AuthVersion), _current.GrantedKeys, true, 0, _current.Version));
        }
        public Task<UserPermissionSaveResult> SaveUserAsync(int actorId, int id, SaveUserPermissionsRequest request, CancellationToken ct) =>
            Task.FromResult(new UserPermissionSaveResult(Error: PermissionSaveError.NotFound));
    }

    private sealed class MemoryPosRepository : IPosCounterRepository
    {
        private readonly Dictionary<int, ShowroomPosProduct> _products = new()
        {
            [1] = new(1, "FF-LIV-001", "8901245001928", "Aurelia 3-Piece Teak Lounge Suite", "Living Room", 45000m, 2, 3, "lounge", true),
            [2] = new(2, "FF-BED-004", "8901245001935", "Kandy Royal King Teak Bed", "Bedroom", 78000m, 1, 3, "bed", true),
            [3] = new(3, "FF-DIN-002", "8901245001942", "Riviera Solid Teak Dining Table", "Dining Room", 62500m, 4, 3, "dining", true),
            [4] = new(4, "FF-WAR-003", "8901245002024", "Royal Arch 3-Door Teak Wardrobe", "Bedroom", 145000m, 0, 3, "wardrobe", true),
        };

        private readonly List<PosCustomerSummary> _customers =
        [
            new(1, "Nimal Silva", "+94 77 123 4567", "+94 77 123 4567", "Walk-in · Kottawa Showroom", DateTimeOffset.UtcNow)
        ];

        private readonly List<ShowroomInvoiceRecord> _invoices = [];
        private PosTillSessionRecord _till = new(
            1, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5)), "Till #01", 25000m, "08:30 AM",
            0, 0m, 0, 0m, 0, 0m, 5, 0, 0, 0, 0, 25000m, 25000m, 0m, 0m, 0m, "OPEN", null, null, null, null);

        public Task<IReadOnlyList<ShowroomPosProduct>> ListProductsAsync(string? category, string? search, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ShowroomPosProduct>>(_products.Values.ToList());

        public Task<ShowroomPosProduct?> GetProductByIdAsync(int productId, CancellationToken ct) =>
            Task.FromResult(_products.GetValueOrDefault(productId));

        public Task<IReadOnlyList<PosCustomerSummary>> ListCustomersAsync(string? search, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PosCustomerSummary>>(_customers.ToList());

        public Task<PosCustomerSummary?> GetCustomerByIdAsync(int customerId, CancellationToken ct) =>
            Task.FromResult(_customers.FirstOrDefault(c => c.CustomerId == customerId));

        public Task<PosWriteResult<PosCustomerSummary>> CreateCustomerAsync(CreatePosCustomerRequest request, CancellationToken ct)
        {
            if (_customers.Any(c => c.PhoneNumber == request.PhoneNumber))
                return Task.FromResult(new PosWriteResult<PosCustomerSummary>(Error: PosWriteError.DuplicatePhone, Message: "Duplicate phone"));
            var created = new PosCustomerSummary(_customers.Count + 1, request.FullName, request.PhoneNumber, request.WhatsappNumber, request.DeliveryAddress, DateTimeOffset.UtcNow);
            _customers.Add(created);
            return Task.FromResult(new PosWriteResult<PosCustomerSummary>(created));
        }

        public Task<IReadOnlyList<PosActiveOrderSummary>> ListActiveOrdersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PosActiveOrderSummary>>([]);

        public Task<IReadOnlyList<ShowroomInvoiceRecord>> ListInvoicesAsync(string? search, string? paymentMode, DateOnly? date, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ShowroomInvoiceRecord>>(_invoices.ToList());

        public Task<ShowroomInvoiceRecord?> GetInvoiceByIdAsync(int invoiceId, CancellationToken ct) =>
            Task.FromResult(_invoices.FirstOrDefault(i => i.InvoiceId == invoiceId));

        public Task<PosWriteResult<ShowroomInvoiceRecord>> CreateInvoiceAsync(int actorId, string actorName, PosComputedCheckout checkout, CancellationToken ct)
        {
            foreach (var line in checkout.Lines)
            {
                var p = _products[line.ProductId];
                if (p.CurrentStock < line.Quantity)
                    return Task.FromResult(new PosWriteResult<ShowroomInvoiceRecord>(Error: PosWriteError.InsufficientStock, Message: "Insufficient stock"));
                _products[line.ProductId] = p with { CurrentStock = p.CurrentStock - line.Quantity };
            }

            var cust = checkout.CustomerId.HasValue ? _customers.First(c => c.CustomerId == checkout.CustomerId.Value) : null;
            var id = _invoices.Count + 891;
            var record = new ShowroomInvoiceRecord(
                id, $"INV-2026-{id:D4}", checkout.CustomerId, cust?.FullName ?? "Walk-in Customer", cust?.PhoneNumber ?? "+94 77 000 0000", cust?.DeliveryAddress,
                checkout.SubtotalAmount, checkout.DiscountCode, checkout.DiscountPercent, checkout.DiscountAmount, checkout.TotalAmount,
                checkout.PaymentMode, checkout.PaymentReference, checkout.CashReceived, checkout.ChangeDue, checkout.InvoiceStatus,
                checkout.IsCustomOrderDeposit, checkout.TillNumber, actorId, actorName, DateTimeOffset.UtcNow, null, null,
                checkout.Lines.Select((l, idx) => new ShowroomInvoiceLine(idx + 1, id, l.ProductId, l.SkuCode, l.ItemName, l.Quantity, l.UnitPrice, l.Subtotal, false)).ToList());
            _invoices.Insert(0, record);
            return Task.FromResult(new PosWriteResult<ShowroomInvoiceRecord>(record));
        }

        public Task<PosWriteResult<ShowroomInvoiceRecord>> VoidOrRefundInvoiceAsync(int actorId, string actorName, int invoiceId, string reason, IReadOnlyCollection<int>? itemIds, CancellationToken ct)
        {
            var idx = _invoices.FindIndex(i => i.InvoiceId == invoiceId);
            if (idx < 0) return Task.FromResult(new PosWriteResult<ShowroomInvoiceRecord>(Error: PosWriteError.NotFound));
            var existing = _invoices[idx];
            foreach (var line in existing.Items)
            {
                var p = _products[line.ProductId];
                _products[line.ProductId] = p with { CurrentStock = p.CurrentStock + line.Quantity };
            }
            var updated = existing with { InvoiceStatus = "VOIDED", VoidReason = reason, VoidedAt = DateTimeOffset.UtcNow };
            _invoices[idx] = updated;
            return Task.FromResult(new PosWriteResult<ShowroomInvoiceRecord>(updated));
        }

        public Task<PosTillSessionRecord> GetTillSessionAsync(DateOnly date, CancellationToken ct) => Task.FromResult(_till);

        public Task<PosTillSessionRecord> SaveTillCountAsync(DateOnly date, TillDenominationRequest r, CancellationToken ct)
        {
            var counted = (r.Notes5000Count * 5000m) + (r.Notes1000Count * 1000m) + (r.Notes500Count * 500m) + (r.Notes100Count * 100m) + (r.Coins50Count * 50m);
            _till = _till with
            {
                Notes5000Count = r.Notes5000Count, Notes1000Count = r.Notes1000Count, Notes500Count = r.Notes500Count,
                Notes100Count = r.Notes100Count, Coins50Count = r.Coins50Count, CountedCashAmount = counted,
                VarianceAmount = counted - _till.ExpectedCashAmount
            };
            return Task.FromResult(_till);
        }

        public Task<PosWriteResult<PosTillSessionRecord>> CloseTillDayAsync(int actorId, string actorName, DateOnly date, CloseTillDayRequest r, CancellationToken ct)
        {
            var counted = (r.Notes5000Count * 5000m) + (r.Notes1000Count * 1000m) + (r.Notes500Count * 500m) + (r.Notes100Count * 100m) + (r.Coins50Count * 50m);
            _till = _till with
            {
                CountedCashAmount = counted, VarianceAmount = counted - _till.ExpectedCashAmount,
                SessionStatus = "CLOSED", ZReportNumber = $"ZR-{date:yyyy-MMdd}-01",
                ClosedByName = actorName, VerifiedByName = r.VerifiedByName ?? "Wimalasiri (Owner)", ClosedAt = DateTimeOffset.UtcNow
            };
            return Task.FromResult(new PosWriteResult<PosTillSessionRecord>(_till));
        }
    }
}
