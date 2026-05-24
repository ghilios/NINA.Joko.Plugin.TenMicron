using System.Globalization;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.ValidationRules;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.ValidationRules {

    [TestFixture]
    public class PositiveIntegerOrInfiniteRuleTests {

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private readonly PositiveIntegerOrInfiniteRule sut = new PositiveIntegerOrInfiniteRule();

        [Test]
        public void Validate_Null_Fails() {
            var result = sut.Validate(null, Invariant);

            result.IsValid.Should().BeFalse();
            result.ErrorContent.Should().Be("Null value");
        }

        [Test]
        public void Validate_UnlimitedString_Passes() {
            var result = sut.Validate("unlimited", Invariant);

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void Validate_PositiveInt_Passes() {
            var result = sut.Validate("42", Invariant);

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void Validate_Zero_Passes() {
            // FLAG: Rule name says "PositiveIntegerOrInfinite" but accepts 0 and negatives via int.TryParse.
            // This test asserts CURRENT behavior (any int parses). If the rule should enforce > 0,
            // this is a behavior gap to revisit.
            var result = sut.Validate("0", Invariant);

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void Validate_NonNumericString_Fails() {
            var result = sut.Validate("not-a-number", Invariant);

            result.IsValid.Should().BeFalse();
            result.ErrorContent.Should().Be("Value must be an integer or unlimited");
        }

        [Test]
        public void Validate_Decimal_Fails() {
            // int.TryParse rejects decimals.
            var result = sut.Validate("3.14", Invariant);

            result.IsValid.Should().BeFalse();
        }
    }
}
