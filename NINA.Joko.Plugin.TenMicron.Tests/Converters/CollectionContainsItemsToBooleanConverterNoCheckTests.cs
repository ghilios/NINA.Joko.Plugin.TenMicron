using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class CollectionContainsItemsToBooleanConverterNoCheckTests {

        private readonly CollectionContainsItemsToBooleanConverterNoCheck sut = new CollectionContainsItemsToBooleanConverterNoCheck();

        [Test]
        public void Convert_NonEmptyCollection_ReturnsTrue() {
            var list = new List<int> { 1, 2, 3 };

            sut.Convert(list, typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(true);
        }

        [Test]
        public void Convert_EmptyCollection_ReturnsFalse() {
            var list = new List<int>();

            sut.Convert(list, typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(false);
        }

        [Test]
        public void Convert_Null_ReturnsFalse() {
            sut.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(false);
        }

        [Test]
        public void Convert_NonEmptyArray_ReturnsTrue() {
            sut.Convert(new[] { "a" }, typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(true);
        }

        [Test]
        public void ConvertBack_Throws_NotSupported() {
            Action act = () => sut.ConvertBack(true, typeof(object), null, CultureInfo.InvariantCulture);

            act.Should().Throw<NotSupportedException>();
        }
    }
}
