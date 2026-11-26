// // Copyright (c) Alexandre Mutel. All rights reserved.
// // Licensed under the MIT license.
// // See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;

namespace SharpYaml.Syntax.Editable;

/// <summary>
/// A flow collection with content, e.g. the "[left, center]" in "key: [left, center]". The node keeps the verbatim
/// text and does not model its entries: read them with a YAML parser over <see cref="Text"/>. An empty collection
/// with only spaces inside ("[]", "{ }") is a <see cref="YamlEditableSequence"/> or a <see cref="YamlEditableMapping"/>
/// instead; one with a line break or a comment inside is a <see cref="YamlEditableFlowCollection"/>.
/// </summary>
public sealed class YamlEditableFlowCollection : YamlEditableNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlEditableFlowCollection"/> class.
    /// </summary>
    /// <param name="text">The verbatim collection text, including its brackets or braces.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public YamlEditableFlowCollection(string text) : base(YamlEditableKind.FlowCollection)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>
    /// Gets the verbatim text of the collection, from its opening bracket or brace to its closing one.
    /// </summary>
    public string Text { get; }

    /// <inheritdoc />
    private protected override void WriteCore(TextWriter writer)
    {
        (writer as YamlEditableTextWriter)?.Record(this, Text);
        writer.Write(Text);
    }
}
