// // Copyright (c) Alexandre Mutel. All rights reserved.
// // Licensed under the MIT license.
// // See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpYaml.Syntax.Editable;

/// <summary>
/// An editable YAML document: a <see cref="YamlSyntaxTree"/> grouped into a mutable tree of
/// maps, lists and scalars with attached comments, in the spirit of Tomlyn.Syntax.DocumentSyntax
/// (see Externals/Tomlyn). Scope is intentionally bounded to what SDKMCPServers needs to edit
/// hand-written configuration files: block-style maps and sequences, plain/quoted scalars, and
/// "#" comments. Flow style ("{ }" / "[ ]"), anchors, aliases and tags are not supported and
/// cause <see cref="Parse"/> to throw <see cref="NotSupportedException"/>.
/// </summary>
public sealed class YamlEditableTree
{
    private YamlEditableTree(YamlEditableNode root, List<YamlEditableTrivia>? trailingTrivia)
    {
        Root = root;
        TrailingTrivia = trailingTrivia;
    }

    /// <summary>
    /// Gets the root node (a <see cref="YamlEditableMap"/> for the common case of a document
    /// whose top level is a mapping).
    /// </summary>
    public YamlEditableNode Root { get; }

    /// <summary>
    /// Gets trivia (typically the final newline, or trailing comments/blank lines) that follows
    /// the last significant token of the document. Might be <see langword="null"/>.
    /// </summary>
    public List<YamlEditableTrivia>? TrailingTrivia { get; }

    /// <summary>
    /// Parses YAML text into an editable tree.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The document uses a construct outside the bounded scope of this editable tree (flow
    /// style, anchors, aliases, tags, or multiple documents).
    /// </exception>
    public static YamlEditableTree Parse(string yaml)
    {
        ArgumentGuard.ThrowIfNull(yaml);

        var syntaxTree = YamlSyntaxTree.Parse(yaml);
        var builder = new Builder(syntaxTree.Tokens);
        var root = builder.BuildRoot();
        return new YamlEditableTree(root, builder.TrailingTrivia);
    }

    /// <summary>
    /// Serializes the tree back to YAML text. Parts of the document that were not edited
    /// reproduce their original text exactly, including comments and formatting.
    /// </summary>
    public string Serialize()
    {
        using var writer = new StringWriter();
        WriteTo(writer);
        return writer.ToString();
    }

    /// <summary>
    /// Writes the tree back to YAML text.
    /// </summary>
    public void WriteTo(TextWriter writer)
    {
        ArgumentGuard.ThrowIfNull(writer);
        Root.WriteTo(writer);
        if (TrailingTrivia != null)
        {
            foreach (var trivia in TrailingTrivia)
            {
                writer.Write(trivia.Text);
            }
        }
    }

    /// <inheritdoc cref="Serialize" />
    public override string ToString() => Serialize();

    /// <summary>
    /// Builds a <see cref="YamlEditableNode"/> tree from the flat, position-sorted token stream
    /// produced by <see cref="YamlSyntaxTree.Parse"/>.
    ///
    /// The stream is NOT a clean, strictly-nested sequence: zero-width structural markers
    /// (<see cref="YamlSyntaxKind.Key"/>, <see cref="YamlSyntaxKind.BlockMappingStart"/>,
    /// <see cref="YamlSyntaxKind.BlockEnd"/>) can appear in either order at the same source
    /// position (e.g. "Key" before or after "BlockMappingStart" depending on parser lookahead),
    /// and trivia is scanned independently of scalar boundaries, so a quoted scalar spanning a
    /// newline produces a spurious NewLineTrivia token nested inside the scalar's own span. This
    /// builder therefore does not assume a fixed token order: it treats the zero-width markers as
    /// structural noise consumed opportunistically, and derives shape from the tokens that do
    /// carry real text (Scalar, Value ":", BlockEntry "-", BlockSequenceStart) together with
    /// indentation recovered from trivia.
    /// </summary>
    private sealed class Builder
    {
        private readonly List<YamlSyntaxToken> _significant;
        private readonly Dictionary<int, List<YamlEditableTrivia>> _leadingTriviaByTokenIndex;
        private int _position;

        /// <summary>
        /// Trivia left over after the last significant token (e.g. the document's final
        /// newline), captured because it never becomes another token's leading trivia.
        /// </summary>
        public List<YamlEditableTrivia>? TrailingTrivia { get; private set; }

        public Builder(IReadOnlyList<YamlSyntaxToken> tokens)
        {
            _significant = [];
            _leadingTriviaByTokenIndex = [];

            var pendingTrivia = new List<YamlEditableTrivia>();
            var containingScalarEnd = -1;
            foreach (var token in tokens)
            {
                if (IsTrivia(token.Kind))
                {
                    if (token.Span.Start.Index < containingScalarEnd)
                    {
                        // Trivia scanned independently of scalar boundaries can fall inside a
                        // quoted/block scalar that already contains this text verbatim (e.g. a
                        // newline inside a double-quoted scalar). Skip it: the owning Scalar
                        // token's Text already carries it.
                        continue;
                    }

                    pendingTrivia.Add(ToTrivia(token));
                    continue;
                }

                if (token.Kind is YamlSyntaxKind.StreamStart or YamlSyntaxKind.StreamEnd
                    or YamlSyntaxKind.DocumentStart or YamlSyntaxKind.DocumentEnd
                    or YamlSyntaxKind.Key or YamlSyntaxKind.BlockMappingStart or YamlSyntaxKind.BlockEnd)
                {
                    // Zero-width structural noise: not represented as nodes in the editable tree.
                    // Map/list nesting is instead recovered from indentation (see IndentOf) and
                    // from Value/BlockEntry/BlockSequenceStart, which do carry reliable shape.
                    // Their position relative to neighboring trivia is not fixed (see class
                    // remarks), so they are dropped rather than consumed in an assumed order.
                    continue;
                }

                if (IsUnsupported(token.Kind))
                {
                    throw new NotSupportedException(
                        $"YamlEditableTree does not support '{token.Kind}'. Only block maps, block " +
                        "sequences, scalars and comments are supported.");
                }

                if (pendingTrivia.Count > 0)
                {
                    _leadingTriviaByTokenIndex[_significant.Count] = pendingTrivia;
                    pendingTrivia = [];
                }

                _significant.Add(token);
                if (token.Kind == YamlSyntaxKind.Scalar)
                {
                    containingScalarEnd = Math.Max(containingScalarEnd, token.Span.End.Index);
                }
            }

            if (pendingTrivia.Count > 0)
            {
                TrailingTrivia = pendingTrivia;
            }
        }

        public YamlEditableNode BuildRoot()
        {
            if (_significant.Count == 0)
            {
                return new YamlEditableMap();
            }

            var node = ParseValue(string.Empty);
            if (_position != _significant.Count)
            {
                throw new NotSupportedException(
                    "YamlEditableTree encountered unexpected trailing content while parsing.");
            }

            return node;
        }

        /// <summary>
        /// Parses the value that starts at the current position, which is either a "Value" or
        /// "BlockEntry" marker's payload, or (only at the document root) the very first token.
        /// Looks ahead to decide whether it is a nested map (next significant token is a
        /// "Value" at greater or equal indentation than <paramref name="parentIndent"/>), a
        /// nested list ("BlockSequenceStart"), or a scalar.
        /// </summary>
        private YamlEditableNode ParseValue(string parentIndent)
        {
            if (Current.Kind == YamlSyntaxKind.BlockSequenceStart)
            {
                return ParseList(parentIndent);
            }

            if (Current.Kind == YamlSyntaxKind.Scalar && IsMapKeyAhead())
            {
                return ParseMap(parentIndent);
            }

            if (Current.Kind == YamlSyntaxKind.Scalar)
            {
                var token = Take();
                return new YamlEditableScalar(token.Text);
            }

            throw new NotSupportedException(
                $"YamlEditableTree encountered an unexpected token '{Current.Kind}' while parsing.");
        }

        /// <summary>
        /// A "Scalar" starts a nested map (rather than being a leaf value) when it is
        /// immediately followed by a "Value" token, i.e. it is itself a key.
        /// </summary>
        private bool IsMapKeyAhead() =>
            _position + 1 < _significant.Count && _significant[_position + 1].Kind == YamlSyntaxKind.Value;

        private YamlEditableMap ParseMap(string parentIndent)
        {
            var map = new YamlEditableMap();
            string? mapIndent = null;

            while (_position < _significant.Count && Current.Kind == YamlSyntaxKind.Scalar && IsMapKeyAhead())
            {
                var rawLeading = TakeLeadingTrivia();
                var (indentOrNull, keyLeading) = SplitIndent(rawLeading);
                var indent = indentOrNull ?? parentIndent;

                if (mapIndent is null)
                {
                    mapIndent = indent;
                }
                else if (indent != mapIndent)
                {
                    // A dedent back to (or below) the parent level ends this mapping; the caller
                    // continues parsing at its own level from the current position. Restore the
                    // trivia exactly as read, so the caller's own SplitIndent sees the full run.
                    UndoLeadingTrivia(rawLeading);
                    break;
                }

                var keyScalarToken = Take();
                Expect(YamlSyntaxKind.Value);

                // When the value is a nested list or map on its own line, the trivia right after
                // "Value" (a newline plus the nested block's own indentation) is what the nested
                // ParseList/ParseMap needs as the leading trivia of its own first entry, not a
                // separator owned by this MapEntry — leave it in place instead of taking it here.
                var nestedBlockAhead = Current.Kind == YamlSyntaxKind.BlockSequenceStart
                    || (Current.Kind == YamlSyntaxKind.Scalar && IsMapKeyAhead());
                var separatorTrivia = nestedBlockAhead ? null : TakeLeadingTrivia();
                var valueNode = ParseValue(indent);

                var entry = new YamlEditableMapEntry(keyScalarToken.Text, valueNode, indent)
                {
                    LeadingTrivia = keyLeading,
                    TrailingTrivia = separatorTrivia,
                };
                map.AddEntry(entry);
            }

            return map;
        }

        private YamlEditableList ParseList(string parentIndent)
        {
            // The whitespace before "BlockSequenceStart" (a zero-width marker sharing the first
            // entry's position) carries the list's indentation; capture it before Expect
            // consumes the marker, since no trivia is separately attached to the first
            // BlockEntry itself.
            var firstEntryLeadingRaw = TakeLeadingTrivia();
            Expect(YamlSyntaxKind.BlockSequenceStart);
            var list = new YamlEditableList();
            var isFirstEntry = true;

            while (_position < _significant.Count && Current.Kind == YamlSyntaxKind.BlockEntry)
            {
                var rawLeading = isFirstEntry ? firstEntryLeadingRaw : TakeLeadingTrivia();
                isFirstEntry = false;
                var (indentOrNull, entryLeading) = SplitIndent(rawLeading);
                var indent = indentOrNull ?? parentIndent;
                Take();

                var itemLeading = TakeLeadingTrivia();
                var itemNode = ParseValue(indent);

                var entry = new YamlEditableListEntry(itemNode, indent)
                {
                    LeadingTrivia = entryLeading,
                    TrailingTrivia = itemLeading,
                };
                list.AddEntry(entry);
            }

            return list;
        }

        private static bool IsTrivia(YamlSyntaxKind kind) =>
            kind is YamlSyntaxKind.WhitespaceTrivia or YamlSyntaxKind.NewLineTrivia or YamlSyntaxKind.CommentTrivia;

        private static bool IsUnsupported(YamlSyntaxKind kind) =>
            kind is YamlSyntaxKind.FlowSequenceStart or YamlSyntaxKind.FlowSequenceEnd
                or YamlSyntaxKind.FlowMappingStart or YamlSyntaxKind.FlowMappingEnd
                or YamlSyntaxKind.FlowEntry or YamlSyntaxKind.Anchor or YamlSyntaxKind.AnchorAlias
                or YamlSyntaxKind.Tag or YamlSyntaxKind.VersionDirective or YamlSyntaxKind.TagDirective;

        private static YamlEditableTrivia ToTrivia(YamlSyntaxToken token) => new(
            token.Kind switch
            {
                YamlSyntaxKind.WhitespaceTrivia => YamlEditableTriviaKind.Whitespace,
                YamlSyntaxKind.NewLineTrivia => YamlEditableTriviaKind.NewLine,
                _ => YamlEditableTriviaKind.Comment,
            },
            token.Text);

        /// <summary>
        /// Splits leading trivia into (a) the indentation string of the token it precedes: the
        /// run of whitespace immediately before it, after the last newline; and (b) the
        /// remaining trivia with that trailing indentation run removed, suitable for a node's
        /// <see cref="YamlEditableNode.LeadingTrivia"/> — indentation is instead reproduced by
        /// the owning <see cref="YamlEditableMapEntry.Indent"/>/<see cref="YamlEditableListEntry.Indent"/>
        /// in <c>WriteCore</c>, so writing both would duplicate it. Returns
        /// (<see langword="null"/>, the input unchanged) if there is no leading trivia, or no
        /// newline in it (e.g. a same-line comment before this token does not encode
        /// indentation).
        /// </summary>
        private static (string? Indent, List<YamlEditableTrivia>? Trivia) SplitIndent(List<YamlEditableTrivia>? leadingTrivia)
        {
            if (leadingTrivia is null || leadingTrivia.Count == 0)
            {
                return (null, leadingTrivia);
            }

            var lastNewLine = leadingTrivia.FindLastIndex(t => t.Kind == YamlEditableTriviaKind.NewLine);
            if (lastNewLine < 0)
            {
                return (null, leadingTrivia);
            }

            var indentRunStart = lastNewLine + 1;
            if (indentRunStart >= leadingTrivia.Count || leadingTrivia[indentRunStart].Kind != YamlEditableTriviaKind.Whitespace)
            {
                // No whitespace run after the last newline: zero indentation, nothing to trim.
                return (string.Empty, leadingTrivia);
            }

            var indent = leadingTrivia[indentRunStart].Text;
            var remaining = leadingTrivia.Take(indentRunStart).ToList();
            return (indent, remaining.Count > 0 ? remaining : null);
        }

        private YamlSyntaxToken Current => _significant[_position];

        private YamlSyntaxToken Take()
        {
            var token = Current;
            _position++;
            return token;
        }

        private YamlSyntaxToken Expect(YamlSyntaxKind kind)
        {
            var token = Current;
            if (token.Kind != kind)
            {
                throw new NotSupportedException(
                    $"YamlEditableTree expected '{kind}' but found '{token.Kind}'.");
            }

            _position++;
            return token;
        }

        private List<YamlEditableTrivia>? TakeLeadingTrivia()
        {
            if (!_leadingTriviaByTokenIndex.TryGetValue(_position, out var trivia))
            {
                return null;
            }

            _leadingTriviaByTokenIndex.Remove(_position);
            return trivia;
        }

        private void UndoLeadingTrivia(List<YamlEditableTrivia>? trivia)
        {
            if (trivia != null)
            {
                _leadingTriviaByTokenIndex[_position] = trivia;
            }
        }
    }
}
