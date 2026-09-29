using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class Wrath335FarClipTests
{
    [TestMethod]
    public void OldMapsUseStandardLimitUnlessOverrideCVarIsEnabled()
    {
        Assert.AreEqual(Wrath335FarClip.Minimum,
            Wrath335FarClip.Validate(1f, 0, false), 0.0001f);
        Assert.AreEqual(Wrath335FarClip.StandardMaximum,
            Wrath335FarClip.Validate(2000f, 0, false), 0.0001f);
        Assert.AreEqual(Wrath335FarClip.ExpandedMaximum,
            Wrath335FarClip.Validate(2000f, 0, true), 0.0001f);
    }

    [TestMethod]
    public void ExpansionLimitUsesMapAndPhysicalMemoryRules()
    {
        Assert.AreEqual(Wrath335FarClip.ExpandedMaximum,
            Wrath335FarClip.Validate(2000f, 530, false), 0.0001f);
        // ignore the physical memory parameter
        // Assert.AreEqual(Wrath335FarClip.StandardMaximum,
        //     Wrath335FarClip.Validate(2000f, 530, false, false), 0.0001f);
        Assert.AreEqual(Wrath335FarClip.StandardMaximum,
            Wrath335FarClip.Validate(2000f,
                Wrath335FarClip.ExpansionExceptionMapIdA, false), 0.0001f);
        Assert.AreEqual(Wrath335FarClip.ExpandedMaximum,
            Wrath335FarClip.Validate(2000f,
                Wrath335FarClip.ExpansionExceptionMapIdB, true), 0.0001f);
    }
}
