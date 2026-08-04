// MarkdownDotNetRenderer
// Copyright (C) 2026 MarkdownDotNetRenderer contributors
//
// This library is free software; you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation; either version 3 of the License, or (at your option) any
// later version.
//
// This library is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along
// with this library; see the file LICENSE.LESSER. If not, see
// <https://www.gnu.org/licenses/>.

using System.Globalization;
using System.Text;

namespace MarkdownDotNetRenderer.Svg;

/// <summary>
/// A minimal, allocation-friendly SVG emitter over a <see cref="StringBuilder"/>. Every
/// attribute value and text node is XML-escaped and every number is formatted with
/// <see cref="CultureInfo.InvariantCulture"/> at a fixed precision, so output is safe and
/// byte-identical on every platform.
/// </summary>
public sealed class SvgBuilder
{
    /// <summary>The SVG namespace URI.</summary>
    public const string SvgNamespace = "http://www.w3.org/2000/svg";

    private const int DecimalPlaces = 2;

    private readonly StringBuilder _text = new();
    private readonly List<Frame> _open = new();
    private bool _startTagOpen;

    /// <summary>Opens a new element, closing the parent's start tag if necessary.</summary>
    /// <param name="name">Element name, e.g. <c>rect</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public SvgBuilder StartElement(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        CloseStartTag();

        if (_open.Count > 0)
        {
            _open[^1].HasElementChildren = true;
            _text.Append('\n');
            _text.Append(' ', _open.Count * 2);
        }

        _text.Append('<').Append(name);
        _open.Add(new Frame(name));
        _startTagOpen = true;
        return this;
    }

    /// <summary>Writes a string attribute on the element currently being opened.</summary>
    /// <param name="name">Attribute name.</param>
    /// <param name="value">Attribute value; XML-escaped.</param>
    /// <returns>This builder, for chaining.</returns>
    public SvgBuilder Attribute(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        if (!_startTagOpen)
        {
            throw new InvalidOperationException(
                "Attributes can only be written directly after StartElement.");
        }

        _text.Append(' ').Append(name).Append("=\"");
        AppendEscaped(value, forAttribute: true);
        _text.Append('"');
        return this;
    }

    /// <summary>Writes a numeric attribute, formatted invariantly at a fixed precision.</summary>
    /// <param name="name">Attribute name.</param>
    /// <param name="value">Attribute value.</param>
    /// <returns>This builder, for chaining.</returns>
    public SvgBuilder Attribute(string name, double value) => Attribute(name, Number(value));

    /// <summary>Writes escaped text content into the current element.</summary>
    /// <param name="text">The text to write.</param>
    /// <returns>This builder, for chaining.</returns>
    public SvgBuilder Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_open.Count == 0)
        {
            throw new InvalidOperationException("Text can only be written inside an element.");
        }

        CloseStartTag();
        AppendEscaped(text, forAttribute: false);
        return this;
    }

    /// <summary>Closes the current element, self-closing it when it has no content.</summary>
    /// <returns>This builder, for chaining.</returns>
    public SvgBuilder EndElement()
    {
        if (_open.Count == 0)
        {
            throw new InvalidOperationException("There is no open element to end.");
        }

        Frame frame = _open[^1];
        _open.RemoveAt(_open.Count - 1);

        if (_startTagOpen)
        {
            _text.Append(" />");
            _startTagOpen = false;
        }
        else
        {
            if (frame.HasElementChildren)
            {
                _text.Append('\n');
                _text.Append(' ', _open.Count * 2);
            }

            _text.Append("</").Append(frame.Name).Append('>');
        }

        return this;
    }

    /// <summary>Formats a number the way this builder writes attributes.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The invariant, fixed-precision representation.</returns>
    public static string Number(double value)
    {
        double rounded = Math.Round(value, DecimalPlaces, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            rounded = 0; // Collapse negative zero so output stays stable.
        }

        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Returns the emitted SVG markup.</summary>
    /// <returns>The markup written so far.</returns>
    public override string ToString()
    {
        if (_open.Count > 0)
        {
            throw new InvalidOperationException(
                $"{_open.Count} element(s) are still open; call EndElement for each StartElement.");
        }

        return _text.ToString();
    }

    private void CloseStartTag()
    {
        if (!_startTagOpen)
        {
            return;
        }

        _text.Append('>');
        _startTagOpen = false;
    }

    private void AppendEscaped(string value, bool forAttribute) =>
        AppendEscaped(_text, value, forAttribute);

    private static void AppendEscaped(StringBuilder target, string value, bool forAttribute)
    {
        foreach (char c in value)
        {
            switch (c)
            {
                case '&':
                    target.Append("&amp;");
                    break;
                case '<':
                    target.Append("&lt;");
                    break;
                case '>':
                    target.Append("&gt;");
                    break;
                case '"' when forAttribute:
                    target.Append("&quot;");
                    break;
                case '\'' when forAttribute:
                    target.Append("&#39;");
                    break;
                default:
                    target.Append(c);
                    break;
            }
        }
    }

    private sealed class Frame(string name)
    {
        public string Name { get; } = name;

        public bool HasElementChildren { get; set; }
    }
}
