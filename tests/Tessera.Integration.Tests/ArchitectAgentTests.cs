using Tessera.Domain.Enums;
using Tessera.Domain.Parsing;
using Tessera.Infrastructure.Ai;
using Xunit;

namespace Tessera.Integration.Tests;

public sealed class ArchitectAgentTests
{
    [Theory]
    [InlineData("src/Orders/OrderService.cs", "Orders")]
    [InlineData("Payment/PaymentController.cs", "Payment")]
    [InlineData("app/services/checkout/PaymentHandler.cs", "Checkout")]
    [InlineData("tests/Payments/OrderTests.cs", "Payments")]
    [InlineData("src/Orders/Sub/Discount.cs", "Orders")]
    [InlineData("IAuditable.cs", "Root")]
    [InlineData("order-domain/models/order.ts", "OrderDomain")]
    public void Infers_bounded_context_from_path(string path, string expected)
        => Assert.Equal(expected, RuleBasedArchitect.InferContext(path));

    [Theory]
    [InlineData("PaymentController", NodeKind.Class, "Controller")]
    [InlineData("OrderService", NodeKind.Class, "Service")]
    [InlineData("PaymentRepository", NodeKind.Class, "Repository")]
    [InlineData("PaymentRepo", NodeKind.Class, "Repository")]
    [InlineData("PaymentEndpoint", NodeKind.Class, "Endpoint")]
    [InlineData("OrderCreatedHandler", NodeKind.Class, "Handler")]
    [InlineData("PaymentProvider", NodeKind.Class, "Provider")]
    [InlineData("OrderPublisher", NodeKind.Class, "EventPublisher")]
    [InlineData("AuditSubscriber", NodeKind.Class, "EventConsumer")]
    [InlineData("AppOptions", NodeKind.Class, "Configuration")]
    [InlineData("CreateOrderRequest", NodeKind.Class, "DTO")]
    [InlineData("Order", NodeKind.Class, "Domain")]
    [InlineData("IAuditable", NodeKind.Interface, "Contract")]
    [InlineData("PaymentStatus", NodeKind.Enum, "Enumeration")]
    [InlineData("PaymentDto", NodeKind.Record, "DataRecord")]
    [InlineData("Money", NodeKind.Struct, "ValueObject")]
    [InlineData("OrderService.ApplyDiscount", NodeKind.Method, "Member")]
    [InlineData("Program", NodeKind.Class, "Domain")]
    public void Infers_role_from_symbol_and_kind(string symbol, NodeKind kind, string expected)
    {
        var entity = new ParsedEntity { Symbol = symbol, Kind = kind };
        Assert.Equal(expected, RuleBasedArchitect.InferRole(entity));
    }

    [Fact]
    public void AppendSection_renders_architecture_markdown()
    {
        var entity = new ParsedEntity
        {
            Path = "src/Orders/OrderService.cs",
            Symbol = "OrderService",
            Kind = NodeKind.Class
        };

        var sb = new System.Text.StringBuilder();
        RuleBasedArchitect.AppendSection(sb, entity);

        var text = sb.ToString();
        Assert.Contains("## Architecture", text);
        Assert.Contains("- Bounded context: Orders", text);
        Assert.Contains("- Role: Service", text);
    }
}
