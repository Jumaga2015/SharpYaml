// // Copyright (c) Alexandre Mutel. All rights reserved.
// // Licensed under the MIT license.
// // See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;

namespace SharpYaml.Syntax.Editable;

/// <summary>
/// A block sequence: an ordered list of "- item" entries sharing the same indentation.
/// </summary>
public sealed class YamlEditableList : YamlEditableNode
{
    private readonly List<YamlEditableListEntry> _items = [];

    /// <summary>
    /// Initializes a new, empty instance of the <see cref="YamlEditableList"/> class.
    /// </summary>
    public YamlEditableList() : base(YamlEditableKind.List)
    {
    }

    /// <summary>
    /// Gets the entries of this sequence, in source order.
    /// </summary>
    public IReadOnlyList<YamlEditableListEntry> Items => _items;

    /// <summary>
    /// Appends an already-constructed entry, reparenting it to this list.
    /// </summary>
    public void AddEntry(YamlEditableListEntry entry)
    {
        ArgumentGuard.ThrowIfNull(entry);
        if (entry.Parent != null)
        {
            throw new InvalidOperationException("The entry is already parented to another node.");
        }

        entry.Parent = this;
        _items.Add(entry);
    }

    /// <summary>
    /// Removes the entry at <paramref name="index"/>.
    /// </summary>
    public void RemoveAt(int index)
    {
        var entry = _items[index];
        entry.Parent = null;
        _items.RemoveAt(index);
    }

    /// <inheritdoc />
    protected override void WriteCore(TextWriter writer)
    {
        foreach (var item in _items)
        {
            item.WriteTo(writer);
        }
    }
}
