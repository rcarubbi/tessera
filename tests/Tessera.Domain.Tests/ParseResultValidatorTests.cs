using Tessera.Domain.Enums;
using Tessera.Domain.Parsing;

namespace Tessera.Domain.Tests;

public class ParseResultValidatorTests
{
    private static ParsedEntity Entity(string key, int startLine = 1, int endLine = 2) => new()
    {
        Key = key,
        Path = $"src/{key}.cs",
        Symbol = key,
        Kind = NodeKind.Class,
        Language = "csharp",
        StartLine = startLine,
        EndLine = endLine,
        StructuralHash = "hash"
    };

    [Fact]
    public void Valid_result_produces_no_errors_or_diagnostics()
    {
        var parse = new ParseResult
        {
            Entities = { Entity("A"), Entity("B") },
            Relationships = { new ParsedRelationship { From = "A", To = "B", Type = EdgeType.Calls } }
        };

        var diagnostics = ParseResultValidator.Validate(parse);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Duplicate_entity_keys_throw()
    {
        var parse = new ParseResult { Entities = { Entity("A"), Entity("A") } };

        var ex = Assert.Throws<ParseResultValidationException>(() => ParseResultValidator.Validate(parse));
        Assert.Contains(ex.Errors, e => e.Contains("Duplicate entity key"));
    }

    [Fact]
    public void Empty_entity_key_throws()
    {
        var parse = new ParseResult { Entities = { Entity("") } };

        Assert.Throws<ParseResultValidationException>(() => ParseResultValidator.Validate(parse));
    }

    [Fact]
    public void Negative_start_line_throws()
    {
        var parse = new ParseResult { Entities = { Entity("A", startLine: -1, endLine: 5) } };

        Assert.Throws<ParseResultValidationException>(() => ParseResultValidator.Validate(parse));
    }

    [Fact]
    public void EndLine_before_StartLine_throws()
    {
        var parse = new ParseResult { Entities = { Entity("A", startLine: 10, endLine: 5) } };

        Assert.Throws<ParseResultValidationException>(() => ParseResultValidator.Validate(parse));
    }

    [Fact]
    public void Empty_relationship_endpoint_throws()
    {
        var parse = new ParseResult
        {
            Entities = { Entity("A") },
            Relationships = { new ParsedRelationship { From = "A", To = "", Type = EdgeType.Calls } }
        };

        Assert.Throws<ParseResultValidationException>(() => ParseResultValidator.Validate(parse));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Out_of_range_confidence_throws(double confidence)
    {
        var parse = new ParseResult
        {
            Entities = { Entity("A"), Entity("B") },
            Relationships = { new ParsedRelationship { From = "A", To = "B", Type = EdgeType.Calls, Confidence = confidence } }
        };

        Assert.Throws<ParseResultValidationException>(() => ParseResultValidator.Validate(parse));
    }

    [Fact]
    public void Missing_endpoint_is_a_diagnostic_not_an_error()
    {
        var parse = new ParseResult
        {
            Entities = { Entity("A") },
            Relationships = { new ParsedRelationship { From = "A", To = "Unresolved", Type = EdgeType.Calls } }
        };

        var diagnostics = ParseResultValidator.Validate(parse);

        Assert.Single(diagnostics);
        Assert.Contains("Unresolved", diagnostics[0]);
    }
}
