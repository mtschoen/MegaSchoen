using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class SlugEncoderTests
{
    [TestMethod]
    public void Encode_WindowsPathWithDriveLetter_ProducesDoubleHyphen()
    {
        Assert.AreEqual("C--Users-mtsch-MegaSchoen",
            SlugEncoder.Encode(@"C:\Users\mtsch\MegaSchoen"));
    }

    [TestMethod]
    public void Encode_PathWithForwardSlashes_HyphenatesEach()
    {
        Assert.AreEqual("-Users-sam-Projects-dev-journal",
            SlugEncoder.Encode("/Users/sam/Projects/dev-journal"));
    }

    [TestMethod]
    public void Encode_PathWithMixedSeparators_HyphenatesAll()
    {
        Assert.AreEqual("C--Users-mtsch-mix",
            SlugEncoder.Encode(@"C:/Users\mtsch/mix"));
    }

    [TestMethod]
    public void Encode_TrailingSeparator_DoesNotProduceTrailingHyphen()
    {
        // Trailing separator is not part of the canonical cwd we get from Windows; trim before encoding to be safe.
        Assert.AreEqual("C--Users-mtsch", SlugEncoder.Encode(@"C:\Users\mtsch\"));
    }
}
