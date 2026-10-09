using Shouldly;
using Xunit;
using SlideRule.Hosting;
using SlideRule.Validation;

namespace SlideRule.Tests;

/// <summary>
///     The one public way to validate a spec, for a rule pack's own tests: <see cref="SpecValidation.Validate" />
///     returns every error the host would refuse the spec with, in the words the host prints, and nothing for a
///     valid spec.
/// </summary>
public sealed class SpecValidationEntryTests
{
    [Fact]
    public void Validate_AValidSpec_ReturnsNothing()
    {
        SpecValidation.Validate(new ArchitectureSpec())
            .ShouldBeEmpty();
    }

    [Fact]
    public void Validate_AnInvalidSpec_ReturnsEachErrorAsTheHostPrintsIt()
    {
        // Arrange
        var spec = new FamilyAsOperandSpec();
        var refusal = Should.Throw<SpecValidationException>(() => ArchModelBuilder.Build(spec));

        // Act
        IReadOnlyList<string> errors = SpecValidation.Validate(spec);

        // Assert
        errors.ShouldNotBeEmpty();
        errors.ShouldBe(refusal.Errors.Select(static error => error.Message));
    }
}
