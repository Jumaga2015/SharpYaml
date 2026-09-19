// // Copyright (c) Alexandre Mutel. All rights reserved.
// // Licensed under the MIT license.
// // See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;

namespace SharpYaml.Syntax.Editable;

/// <summary>
/// Base class for a node in an editable YAML tree. Mirrors the design of
/// Tomlyn.Syntax.SyntaxNode (see Externals/Tomlyn): comments and blank lines are kept as trivia
/// attached to nodes, children are set through typed properties that reparent the child, and
/// <see cref="WriteTo(TextWriter)"/> reproduces the original text unless a node was edited.
/// </summary>
public abstract class YamlEditableNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlEditableNode"/> class.
    /// </summary>
    /// <param name="kind">The kind of this node.</param>
    protected YamlEditableNode(YamlEditableKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the kind of this node.
    /// </summary>
    public YamlEditableKind Kind { get; }

    /// <summary>
    /// Gets the parent of this node, or <see langword="null"/> for the root.
    /// </summary>
    public YamlEditableNode? Parent { get; internal set; }

    /// <summary>
    /// Gets comment lines and blank lines that appear before this node, in source order.
    /// Might be <see langword="null"/> if there is no leading trivia.
    /// </summary>
    public List<YamlEditableTrivia>? LeadingTrivia { get; set; }

    /// <summary>
    /// Gets trivia attached after this node's own marker and before its child value: for a
    /// <see cref="YamlEditableMapEntry"/>, whatever appears between "key:" and the value (a
    /// single space, or a same-line comment followed by a newline and the child's indentation
    /// when the value is a nested map/list on its own line); for a
    /// <see cref="YamlEditableListEntry"/>, the equivalent text after "-". Not used by
    /// <see cref="YamlEditableScalar"/>, <see cref="YamlEditableMap"/> or
    /// <see cref="YamlEditableList"/>. Might be <see langword="null"/> if there is none.
    /// </summary>
    public List<YamlEditableTrivia>? TrailingTrivia { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        var writer = new StringWriter();
        WriteTo(writer);
        return writer.ToString();
    }

    /// <summary>
    /// Writes this node back to YAML text.
    /// </summary>
    /// <param name="writer">The destination writer.</param>
    public void WriteTo(TextWriter writer)
    {
        ArgumentGuard.ThrowIfNull(writer);
        WriteLeadingTrivia(writer);
        WriteCore(writer);
    }

    /// <summary>
    /// Writes the node-specific content (without leading trivia).
    /// </summary>
    protected abstract void WriteCore(TextWriter writer);

    private void WriteLeadingTrivia(TextWriter writer)
    {
        if (LeadingTrivia is null)
        {
            return;
        }

        foreach (var trivia in LeadingTrivia)
        {
            writer.Write(trivia.Text);
        }
    }

    /// <summary>
    /// Reparents <paramref name="node"/> to this instance, detaching it from any previous
    /// parent slot and throwing if it is already parented elsewhere. Mirrors
    /// Tomlyn.Syntax.SyntaxNode.ParentToThis.
    /// </summary>
    protected static void ParentToThis<TNode>(ref TNode? slot, TNode? node, YamlEditableNode owner)
        where TNode : YamlEditableNode
    {
        if (node?.Parent != null)
        {
            throw new InvalidOperationException("The node is already parented to another node.");
        }

        if (slot != null)
        {
            slot.Parent = null;
        }

        if (node != null)
        {
            node.Parent = owner;
        }

        slot = node;
    }
}
