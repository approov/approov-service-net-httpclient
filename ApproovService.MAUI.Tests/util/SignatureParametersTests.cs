using Approov.Util.HttpSfv;
using Approov.Util.Sig;
using Xunit;

namespace Approov.Tests;

public class SignatureParametersTests
{
    [Fact]
    public void AddComponentIdentifier_AppearsInToComponentValue()
    {
        var sp = new SignatureParameters();
        sp.AddComponentIdentifier(new StringItem("@method"));
        var inner = sp.ToComponentValue();
        Assert.Single(inner);
        Assert.Equal("@method", inner[0].Value);
    }

    [Fact]
    public void AddParameter_AppendedToComponentValue()
    {
        var sp = new SignatureParameters();
        sp.AddParameter("created", 1735000000L);
        var inner = sp.ToComponentValue();
        Assert.Empty(inner); // no component identifiers
        // Parameters are on SignatureParameters itself, not on inner list items
        Assert.Equal("1735000000", sp.GetParameterValue("created")?.ToString());
    }

    [Fact]
    public void ToComponentValue_MultipleIdentifiers_PreservesOrder()
    {
        var sp = new SignatureParameters();
        sp.AddComponentIdentifier(new StringItem("@method"));
        sp.AddComponentIdentifier(new StringItem("@path"));
        sp.AddComponentIdentifier(new StringItem("@authority"));
        var inner = sp.ToComponentValue();
        Assert.Equal(3, inner.Count);
        Assert.Equal("@method", inner[0].Value);
        Assert.Equal("@path", inner[1].Value);
        Assert.Equal("@authority", inner[2].Value);
    }
}
