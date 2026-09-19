using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpYaml.Syntax.Editable;

namespace SharpYaml.Tests.Syntax.Editable;

[TestClass]
public class YamlEditableTreeTests
{
    public static IEnumerable<object[]> RoundTripCases()
    {
        yield return new object[]
        {
            "# comment\nroot:\n  key: value\n\nlist:\n  - a\n  - b\n",
        };
        yield return new object[]
        {
            "a: 1\r\nb:\r\n  - 2\r\n  - 3\r\n",
        };
        yield return new object[]
        {
            "server:\n  # inner comment\n  host: localhost\n  port: 5432\n",
        };
    }

    [TestMethod]
    [DynamicData(nameof(RoundTripCases))]
    public void ParseAndSerializeWithoutEditingPreservesOriginalText(string yaml)
    {
        var tree = YamlEditableTree.Parse(yaml);

        Assert.AreEqual(yaml, tree.Serialize());

        using var writer = new StringWriter();
        tree.WriteTo(writer);
        Assert.AreEqual(yaml, writer.ToString());
    }

    [TestMethod]
    public void SetValue_OnExistingScalarKey_PreservesSurroundingComments()
    {
        const string yaml = "# top comment\nroot:\n  key: value\n  other: 1\n";
        var tree = YamlEditableTree.Parse(yaml);
        var root = (YamlEditableMap)tree.Root;
        var inner = (YamlEditableMap)root.GetValue("root")!;

        inner.SetValue("key", new YamlEditableScalar("changed"));

        var result = tree.Serialize();
        Assert.Contains("# top comment", result);
        Assert.Contains("key: changed", result);
        Assert.Contains("other: 1", result);
        Assert.DoesNotContain("key: value", result);
    }

    [TestMethod]
    public void SetValue_AddsNewKey_AtRootMap()
    {
        const string yaml = "a: 1\nb: 2\n";
        var tree = YamlEditableTree.Parse(yaml);
        var root = (YamlEditableMap)tree.Root;

        root.SetValue("c", new YamlEditableScalar("3"));

        var result = tree.Serialize();
        Assert.Contains("a: 1\n", result);
        Assert.Contains("b: 2\n", result);
        Assert.Contains("c: 3", result);
    }

    [TestMethod]
    public void RemoveEntry_ExistingKey_DropsItFromSerializedOutput()
    {
        const string yaml = "a: 1\nb: 2\nc: 3\n";
        var tree = YamlEditableTree.Parse(yaml);
        var root = (YamlEditableMap)tree.Root;

        var removed = root.RemoveEntry("b");

        Assert.IsTrue(removed);
        var result = tree.Serialize();
        Assert.Contains("a: 1", result);
        Assert.Contains("c: 3", result);
        Assert.DoesNotContain("b: 2", result);
    }

    [TestMethod]
    public void Parse_ListOfScalars_ExposesItemsInOrder()
    {
        const string yaml = "items:\n  - a\n  - b\n  - c\n";
        var tree = YamlEditableTree.Parse(yaml);
        var root = (YamlEditableMap)tree.Root;
        var list = (YamlEditableList)root.GetValue("items")!;

        Assert.HasCount(3, list.Items);
        Assert.AreEqual("a", ((YamlEditableScalar)list.Items[0].Value).Text);
        Assert.AreEqual("b", ((YamlEditableScalar)list.Items[1].Value).Text);
        Assert.AreEqual("c", ((YamlEditableScalar)list.Items[2].Value).Text);
    }

    [TestMethod]
    public void Parse_FlowStyleMapping_ThrowsNotSupportedException()
    {
        const string yaml = "flow: { a: 1, b: 2 }\n";
        Assert.ThrowsExactly<NotSupportedException>(() => YamlEditableTree.Parse(yaml));
    }

    [TestMethod]
    public void Parse_Anchor_ThrowsNotSupportedException()
    {
        const string yaml = "node: &n1 value\nalias: *n1\n";
        Assert.ThrowsExactly<NotSupportedException>(() => YamlEditableTree.Parse(yaml));
    }
}
