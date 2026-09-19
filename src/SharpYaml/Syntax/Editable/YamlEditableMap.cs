// // Copyright (c) Alexandre Mutel. All rights reserved.
// // Licensed under the MIT license.
// // See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpYaml.Syntax.Editable;

/// <summary>
/// A block mapping: an ordered sequence of "key: value" entries sharing the same indentation.
/// </summary>
public sealed class YamlEditableMap : YamlEditableNode
{
    private readonly List<YamlEditableMapEntry> _entries = [];

    /// <summary>
    /// Initializes a new, empty instance of the <see cref="YamlEditableMap"/> class.
    /// </summary>
    public YamlEditableMap() : base(YamlEditableKind.Map)
    {
    }

    /// <summary>
    /// Gets the entries of this mapping, in source order.
    /// </summary>
    public IReadOnlyList<YamlEditableMapEntry> Entries => _entries;

    /// <summary>
    /// Gets the value for <paramref name="key"/>, or <see langword="null"/> if the key is not
    /// present.
    /// </summary>
    public YamlEditableNode? GetValue(string key) =>
        _entries.FirstOrDefault(entry => entry.Key == key)?.Value;

    /// <summary>
    /// Sets the value for <paramref name="key"/>. If the key already exists, its value node is
    /// replaced in place, keeping the entry's original indentation and surrounding comments. If
    /// the key does not exist yet, a new entry is appended using <paramref name="indent"/>
    /// (defaults to the indentation of the last existing entry, or an empty string for the root).
    /// </summary>
    public void SetValue(string key, YamlEditableNode value, string? indent = null)
    {
        ArgumentGuard.ThrowIfNull(key);
        if (key.Length == 0)
        {
            throw new ArgumentException("The key must not be empty.", nameof(key));
        }

        ArgumentGuard.ThrowIfNull(value);

        var existing = _entries.FirstOrDefault(entry => entry.Key == key);
        if (existing != null)
        {
            existing.Value = value;
            return;
        }

        var resolvedIndent = indent ?? _entries.LastOrDefault()?.Indent ?? string.Empty;
        var entry = new YamlEditableMapEntry(key, value, resolvedIndent);
        if (_entries.Count > 0)
        {
            // A newly appended entry needs its own line: synthesize the newline that a parsed
            // entry would already carry as leading trivia.
            entry.LeadingTrivia = [new YamlEditableTrivia(YamlEditableTriviaKind.NewLine, "\n")];
        }

        AddEntry(entry);
    }

    /// <summary>
    /// Appends an already-constructed entry, reparenting it to this mapping.
    /// </summary>
    public void AddEntry(YamlEditableMapEntry entry)
    {
        ArgumentGuard.ThrowIfNull(entry);
        if (entry.Parent != null)
        {
            throw new InvalidOperationException("The entry is already parented to another node.");
        }

        entry.Parent = this;
        _entries.Add(entry);
    }

    /// <summary>
    /// Removes the entry for <paramref name="key"/>. Returns <see langword="false"/> if the key
    /// was not present.
    /// </summary>
    public bool RemoveEntry(string key)
    {
        var existing = _entries.FirstOrDefault(entry => entry.Key == key);
        if (existing is null)
        {
            return false;
        }

        existing.Parent = null;
        _entries.Remove(existing);
        return true;
    }

    /// <inheritdoc />
    protected override void WriteCore(TextWriter writer)
    {
        foreach (var entry in _entries)
        {
            entry.WriteTo(writer);
        }
    }
}
