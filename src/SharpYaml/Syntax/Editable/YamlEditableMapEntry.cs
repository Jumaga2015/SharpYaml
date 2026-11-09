// // Copyright (c) Alexandre Mutel. All rights reserved.
// // Licensed under the MIT license.
// // See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;

namespace SharpYaml.Syntax.Editable;

/// <summary>
/// A single "key: value" entry of a <see cref="YamlEditableMap"/>. Mirrors
/// Tomlyn.Syntax.KeyValueSyntax: the key is kept as plain text (bare keys only, matching the
/// bounded scope of this editable tree), and the value is reparented through
/// <see cref="Value"/> like any other <see cref="YamlEditableNode"/> child.
/// </summary>
public sealed class YamlEditableMapEntry : YamlEditableNode
{
    private YamlEditableNode? _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlEditableMapEntry"/> class.
    /// </summary>
    /// <param name="key">The entry's key text (the part before ": ").</param>
    /// <param name="value">The entry's value.</param>
    /// <param name="indent">The indentation text written before the key on its own line.</param>
    public YamlEditableMapEntry(string key, YamlEditableNode value, string indent)
        : base(YamlEditableKind.MapEntry)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Indent = indent ?? throw new ArgumentNullException(nameof(indent));
        Value = value;
    }

    /// <summary>
    /// Gets or sets the entry's key text (the part before ": ").
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// Gets the indentation text written before the key on its own line.
    /// </summary>
    public string Indent { get; }

    /// <summary>
    /// Gets or sets the entry's value. Assigning a new value detaches the previous one and
    /// reparents the new node to this entry, following the same convention as
    /// Tomlyn.Syntax.KeyValueSyntax.Value.
    /// </summary>
    public YamlEditableNode Value
    {
        get => _value!;
        set => ParentToThis(ref _value, value ?? throw new ArgumentNullException(nameof(value)), this);
    }

    /// <inheritdoc />
    protected override void WriteCore(TextWriter writer)
    {
        writer.Write(Indent);
        writer.Write(Key);
        writer.Write(':');

        if (TrailingTrivia != null)
        {
            foreach (var trivia in TrailingTrivia)
            {
                writer.Write(trivia.Text);
            }
        }
        else if (Value.Kind == YamlEditableKind.Scalar)
        {
            writer.Write(' ');
        }

        Value.WriteTo(writer);
    }
}
