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
        yield return new object[]
        {
            "name: Ada\nServers:\n  - Name: prod\n    Port: 1521\n  - Name: dev\n    Port: 1522\n",
        };
        yield return new object[]
        {
            "server:\n  host: localhost\n  port: 5432\nclient:\n  timeout: 30\n",
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
    public void Parse_ListOfMultiKeyMaps_ExposesEveryKeyOfEachItem()
    {
        // The first key of each item shares its line with "-", so it has no indentation of its
        // own; the next key ("Port") is on its own line. Both must stay in the same map.
        const string yaml = "Servers:\n  - Name: prod\n    Port: 1521\n  - Name: dev\n    Port: 1522\n";
        var tree = YamlEditableTree.Parse(yaml);
        var root = (YamlEditableMap)tree.Root;
        var servers = (YamlEditableList)root.GetValue("Servers")!;

        Assert.HasCount(2, servers.Items);
        var first = (YamlEditableMap)servers.Items[0].Value;
        Assert.AreEqual("prod", ((YamlEditableScalar)first.GetValue("Name")!).Text);
        Assert.AreEqual("1521", ((YamlEditableScalar)first.GetValue("Port")!).Text);
        var second = (YamlEditableMap)servers.Items[1].Value;
        Assert.AreEqual("dev", ((YamlEditableScalar)second.GetValue("Name")!).Text);
        Assert.AreEqual("1522", ((YamlEditableScalar)second.GetValue("Port")!).Text);
    }

    [TestMethod]
    public void Parse_DedentAfterNestedMap_EndsNestedMapAtParentLevel()
    {
        const string yaml = "server:\n  host: localhost\n  port: 5432\nclient:\n  timeout: 30\n";
        var tree = YamlEditableTree.Parse(yaml);
        var root = (YamlEditableMap)tree.Root;

        var server = (YamlEditableMap)root.GetValue("server")!;
        var client = (YamlEditableMap)root.GetValue("client")!;
        Assert.AreEqual("5432", ((YamlEditableScalar)server.GetValue("port")!).Text);
        Assert.IsNull(server.GetValue("client"));
        Assert.AreEqual("30", ((YamlEditableScalar)client.GetValue("timeout")!).Text);
    }

    [TestMethod]
    public void ToSyntaxTree_WithoutEdits_ReturnsSourceSnapshot()
    {
        var tree = YamlEditableTree.Parse("a: 1 # keep\n");

        Assert.AreSame(tree.SyntaxTree, tree.ToSyntaxTree());
    }

    [TestMethod]
    public void ToSyntaxTree_AfterEdit_ReturnsValidatedTreeAndKeepsSourceSnapshot()
    {
        const string yaml = "# top\nServers:\n  - Name: prod # main\n    Port: 1521\n";
        var tree = YamlEditableTree.Parse(yaml);
        var servers = (YamlEditableList)((YamlEditableMap)tree.Root).GetValue("Servers")!;
        ((YamlEditableMap)servers.Items[0].Value).SetValue("Port", new YamlEditableScalar("1522"));

        var edited = tree.ToSyntaxTree();

        Assert.AreEqual("# top\nServers:\n  - Name: prod # main\n    Port: 1522\n", edited.ToFullString());
        Assert.AreEqual(tree.Serialize(), edited.ToFullString());
        Assert.AreEqual(yaml, tree.SyntaxTree.ToFullString());
    }

    [TestMethod]
    public void ToSyntaxTree_DocumentationExample_ProducesDocumentedOutput()
    {
        // Same code as site/docs/low-level/syntax-tree.md, "Edit by key with the editable tree".
        var tree = YamlEditableTree.Parse("# servers\nServers:\n  - Name: prod # main\n    Port: 1521\n");
        var servers = (YamlEditableList)((YamlEditableMap)tree.Root).GetValue("Servers")!;
        var prod = (YamlEditableMap)servers.Items[0].Value;
        prod.SetValue("Port", new YamlEditableScalar("1522"));
        prod.SetValue("Host", new YamlEditableScalar("db1"));

        var edited = tree.ToSyntaxTree();

        Assert.AreEqual("# servers\nServers:\n  - Name: prod # main\n    Port: 1522\n    Host: db1\n", edited.ToFullString());
    }

    [TestMethod]
    public void ToSyntaxTree_EditProducesInvalidYaml_ThrowsYamlException()
    {
        var tree = YamlEditableTree.Parse("a: 1\n");
        ((YamlEditableMap)tree.Root).SetValue("a", new YamlEditableScalar("[unclosed"));

        Assert.Throws<YamlException>(() => tree.ToSyntaxTree());
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
