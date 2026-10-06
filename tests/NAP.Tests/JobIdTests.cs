using System.Globalization;
using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class JobIdTests
{
    private const string CanonicalValue = "job_00112233445566778899aabbccddeeff";
    private const string InvalidMessage = "A Job ID must use the canonical form 'job_' followed by 32 lowercase ASCII hexadecimal characters and must not be all zero.";

    [Fact]
    public void PublicContractIsExactlyASealedRecordWithValueConstructorToStringAndCreate()
    {
        var type = typeof(JobId);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.NotNull(type.GetMethod("<Clone>$"));
        Assert.True(typeof(IEquatable<JobId>).IsAssignableFrom(type));
        var parameter = Assert.Single(Assert.Single(type.GetConstructors()).GetParameters());
        Assert.Equal("value", parameter.Name);
        Assert.Equal(typeof(string), parameter.ParameterType);
        var property = Assert.Single(type.GetProperties());
        Assert.Equal("Value", property.Name);
        Assert.Equal(typeof(string), property.PropertyType);
        Assert.True(property.GetMethod!.IsPublic);
        Assert.False(property.GetMethod.IsStatic);
        Assert.Null(property.SetMethod);
        var toString = type.GetMethod("ToString", Type.EmptyTypes)!;
        Assert.Equal(type, toString.DeclaringType);
        Assert.Equal(typeof(string), toString.ReturnType);
        var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        Assert.NotNull(create);
        Assert.Empty(create.GetParameters());
        Assert.Equal(type, create.ReturnType);
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Empty(type.GetEvents());
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name + "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name)) + ")")
            .OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(new[] { "<Clone>$()", "Create()", "Equals(JobId)", "Equals(Object)", "GetHashCode()", "ToString()",
            "get_Value()", "op_Equality(JobId,JobId)", "op_Inequality(JobId,JobId)" }.OrderBy(name => name, StringComparer.Ordinal), methods);
    }

    [Theory]
    [InlineData(CanonicalValue)]
    [InlineData("job_ffffffffffffffffffffffffffffffff")]
    [InlineData("job_00000000000000000000000000000001")]
    [InlineData("job_10000000000000000000000000000000")]
    [InlineData("job_0123456789abcdef0123456789abcdef")]
    public void ValidValuesAreRetainedExactlyWithoutNormalization(string value)
    {
        var id = new JobId(value);
        Assert.Same(value, id.Value);
        Assert.Same(value, id.ToString());
        AssertCanonical(id);
    }

    [Fact]
    public void NullIsRejectedWithExactParameter() =>
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => new JobId(null!)).ParamName);

    public static IEnumerable<object[]> InvalidRepresentations()
    {
        var suffix = CanonicalValue[4..];
        foreach (var value in new[]
        {
            "", " ", "\t\r\n", "job_", "job_" + suffix[..31], "job_" + suffix + "f",
            CanonicalValue + "extra", " " + CanonicalValue, CanonicalValue + " ", "\t" + CanonicalValue,
            CanonicalValue + "\t", "\n" + CanonicalValue, CanonicalValue + "\n", CanonicalValue + "\r\n",
            "JOB_" + suffix, "Job_" + suffix, "joB_" + suffix, "jobs_" + suffix,
            suffix, "run_" + suffix, "job-" + suffix, "job " + suffix,
            "job_" + suffix.ToUpperInvariant(), "job_0x" + suffix, "job_0x" + suffix[2..],
            "job_00112233-4455-6677-8899-aabbccddeeff", "job_{" + suffix + "}", "{" + CanonicalValue + "}",
            "00112233-4455-6677-8899-aabbccddeeff", "job_(" + suffix + ")"
        }) yield return [value];
        // Preserve length/prefix so each forbidden character is rejected by the ASCII boundary.
        foreach (var character in new[] { 'A', 'B', 'C', 'D', 'E', 'F', 'g', 'z', '-', '_', ' ', '\t', '\n', '\r',
            ':', '/', '\\', '{', '}', 'é', '漢', 'ａ', '０', '٠', '۱', '\0', '\u200b', '\u00a0' })
            yield return ["job_" + suffix[..15] + character + suffix[16..]];
    }

    [Theory]
    [MemberData(nameof(InvalidRepresentations))]
    public void EveryNoncanonicalRepresentationUsesExactExceptionParameterAndBaseMessage(string value) =>
        AssertInvalid(value);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    public void AsciiValidationIncludesEverySuffixPosition(int position)
    {
        var characters = CanonicalValue.ToCharArray();
        characters[4 + position] = 'G';
        AssertInvalid(new string(characters));
    }

    [Fact]
    public void AllZeroIsInvalidButAnOtherwiseZeroValueWithOneNonzeroNibbleIsValid()
    {
        AssertInvalid("job_00000000000000000000000000000000");
        for (var position = 0; position < 32; position++)
        {
            var suffix = new string('0', 32).ToCharArray();
            suffix[position] = '1';
            var value = "job_" + new string(suffix);
            Assert.Equal(value, new JobId(value).Value);
        }
    }

    [Fact]
    public void EqualCanonicalValuesHaveValueEqualityOperatorsAndCoherentHashCodes()
    {
        var first = new JobId(new string(CanonicalValue.ToCharArray()));
        var same = new JobId(new string(CanonicalValue.ToCharArray()));
        var different = new JobId("job_00000000000000000000000000000001");
        Assert.NotSame(first, same);
        Assert.NotSame(first.Value, same.Value);
        Assert.True(first.Equals(same));
        Assert.True(first.Equals((object)same));
        Assert.True(first == same);
        Assert.False(first != same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.False(first.Equals(different));
        Assert.False(first.Equals((object)different));
        Assert.False(first == different);
        Assert.True(first != different);
        Assert.False(first.Equals(null));
        Assert.False(first.Equals(CanonicalValue));
        Assert.Equal(2, new HashSet<JobId> { first, same, different }.Count);
    }

    [Fact]
    public void CreateProducesCanonicalRehydratableIdentitiesWithoutAssumingCollisionFreedom()
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var id = JobId.Create();
            AssertCanonical(id);
            var reconstructed = new JobId(id.Value);
            Assert.Equal(id, reconstructed);
            Assert.True(id == reconstructed);
            Assert.Equal(id.GetHashCode(), reconstructed.GetHashCode());
        }
        // Uniqueness is not asserted from a finite random sample.
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void ValidationFormattingGenerationAndEqualityAreCultureIndependent(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        var before = new JobId(CanonicalValue);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var same = new JobId(CanonicalValue);
            Assert.Equal(CanonicalValue, same.ToString());
            Assert.Equal(before, same);
            Assert.True(before == same);
            Assert.Equal(before.GetHashCode(), same.GetHashCode());
            Assert.False(before == new JobId("job_00000000000000000000000000000001"));
            Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => new JobId(null!)).ParamName);
            foreach (var row in InvalidRepresentations()) AssertInvalid((string)row[0]);
            AssertInvalid("job_00000000000000000000000000000000");
            AssertCanonical(JobId.Create());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void IdentityHasNoUniverseAssetStateTimestampPathOrMetadataDependencies()
    {
        var type = typeof(JobId);
        Assert.Equal(new[] { "Value" }, type.GetProperties().Select(property => property.Name));
        var field = Assert.Single(type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Equal(typeof(string), field.FieldType);
        Assert.False(field.IsStatic);
        Assert.True(field.IsInitOnly);
        Assert.Equal(new[] { typeof(string) }, Assert.Single(type.GetConstructors()).GetParameters().Select(p => p.ParameterType));
    }

    private static void AssertInvalid(string value)
    {
        var error = Assert.Throws<ArgumentException>(() => new JobId(value));
        Assert.Equal("value", error.ParamName);
        Assert.Equal(new ArgumentException(InvalidMessage, "value").Message, error.Message);
    }

    private static void AssertCanonical(JobId id)
    {
        Assert.IsType<JobId>(id);
        Assert.Equal(36, id.Value.Length);
        Assert.Equal("job_", id.Value[..4]);
        Assert.All(id.Value[4..], character => Assert.True(character is >= '0' and <= '9' or >= 'a' and <= 'f'));
        Assert.NotEqual("job_00000000000000000000000000000000", id.Value);
        Assert.Equal(id.Value, id.ToString());
    }
}
