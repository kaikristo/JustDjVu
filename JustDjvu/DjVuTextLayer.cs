using System.Globalization;
using System.Text;

namespace JustDjvu;

public sealed record DjVuTextFragment(
    double XMin,
    double YMin,
    double XMax,
    double YMax,
    string Text,
    int Line);

public sealed record PageTextLayer(
    double Width,
    double Height,
    IReadOnlyList<DjVuTextFragment> Fragments)
{
    public static PageTextLayer Empty { get; } = new(1, 1, []);
}

internal static class DjVuTextLayerParser
{
    public static PageTextLayer Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return PageTextLayer.Empty;
        }

        var parser = new SExpressionParser(text);
        var page = parser.ParseFirstNode();
        if (page is null || !page.Type.Equals("page", StringComparison.OrdinalIgnoreCase))
        {
            return PageTextLayer.Empty;
        }

        var width = Math.Max(1, page.XMax - page.XMin);
        var height = Math.Max(1, page.YMax - page.YMin);
        var fragments = new List<DjVuTextFragment>();
        var lineCounter = 0;
        CollectWords(page, page.XMin, page.YMin, ref lineCounter, 0, fragments);

        if (fragments.Count == 0)
        {
            lineCounter = 0;
            CollectTextLeaves(page, page.XMin, page.YMin, ref lineCounter, fragments);
        }

        return new PageTextLayer(width, height, fragments);
    }

    private static void CollectWords(
        TextNode node,
        int pageX,
        int pageY,
        ref int lineCounter,
        int currentLine,
        List<DjVuTextFragment> fragments)
    {
        if (node.Type.Equals("line", StringComparison.OrdinalIgnoreCase))
        {
            currentLine = ++lineCounter;
        }

        if (node.Type.Equals("word", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(node.Text))
        {
            if (currentLine == 0)
            {
                currentLine = ++lineCounter;
            }
            fragments.Add(CreateFragment(node, pageX, pageY, currentLine));
            return;
        }

        foreach (var child in node.Children)
        {
            CollectWords(child, pageX, pageY, ref lineCounter, currentLine, fragments);
        }
    }

    private static void CollectTextLeaves(
        TextNode node,
        int pageX,
        int pageY,
        ref int lineCounter,
        List<DjVuTextFragment> fragments)
    {
        if (!string.IsNullOrEmpty(node.Text))
        {
            fragments.Add(CreateFragment(node, pageX, pageY, ++lineCounter));
            return;
        }

        foreach (var child in node.Children)
        {
            CollectTextLeaves(child, pageX, pageY, ref lineCounter, fragments);
        }
    }

    private static DjVuTextFragment CreateFragment(
        TextNode node, int pageX, int pageY, int line)
    {
        var xMin = Math.Min(node.XMin, node.XMax) - pageX;
        var xMax = Math.Max(node.XMin, node.XMax) - pageX;
        var yMin = Math.Min(node.YMin, node.YMax) - pageY;
        var yMax = Math.Max(node.YMin, node.YMax) - pageY;
        return new DjVuTextFragment(xMin, yMin, xMax, yMax, node.Text!, line);
    }

    private sealed record TextNode(
        string Type,
        int XMin,
        int YMin,
        int XMax,
        int YMax,
        string? Text,
        IReadOnlyList<TextNode> Children);

    private ref struct SExpressionParser
    {
        private readonly ReadOnlySpan<char> _input;
        private int _position;

        public SExpressionParser(string input) => _input = input.AsSpan();

        public TextNode? ParseFirstNode()
        {
            SkipWhitespace();
            while (_position < _input.Length)
            {
                if (_input[_position] == '(')
                {
                    return ParseNode();
                }
                _position++;
            }
            return null;
        }

        private TextNode? ParseNode()
        {
            if (!TryConsume('('))
            {
                return null;
            }

            var type = ReadAtom();
            if (string.IsNullOrWhiteSpace(type) ||
                !TryReadInt(out var xMin) ||
                !TryReadInt(out var yMin) ||
                !TryReadInt(out var xMax) ||
                !TryReadInt(out var yMax))
            {
                SkipCurrentList();
                return null;
            }

            var children = new List<TextNode>();
            string? value = null;
            while (_position < _input.Length)
            {
                SkipWhitespace();
                if (_position >= _input.Length)
                {
                    break;
                }

                switch (_input[_position])
                {
                    case ')':
                        _position++;
                        return new TextNode(type, xMin, yMin, xMax, yMax, value, children);
                    case '(':
                        var child = ParseNode();
                        if (child is not null)
                        {
                            children.Add(child);
                        }
                        break;
                    case '"':
                        value = ReadString();
                        break;
                    default:
                        ReadAtom();
                        break;
                }
            }

            return new TextNode(type, xMin, yMin, xMax, yMax, value, children);
        }

        private bool TryReadInt(out int value)
        {
            var atom = ReadAtom();
            return int.TryParse(atom, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private string ReadAtom()
        {
            SkipWhitespace();
            var start = _position;
            while (_position < _input.Length &&
                   !char.IsWhiteSpace(_input[_position]) &&
                   _input[_position] is not '(' and not ')' and not '"')
            {
                _position++;
            }
            return _input[start.._position].ToString();
        }

        private string ReadString()
        {
            if (!TryConsume('"'))
            {
                return string.Empty;
            }

            var result = new StringBuilder();
            while (_position < _input.Length)
            {
                var character = _input[_position++];
                if (character == '"')
                {
                    break;
                }
                if (character != '\\' || _position >= _input.Length)
                {
                    result.Append(character);
                    continue;
                }

                var escaped = _input[_position++];
                result.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    _ when escaped is >= '0' and <= '7' => ReadOctal(escaped),
                    _ => escaped
                });
            }
            return result.ToString();
        }

        private char ReadOctal(char first)
        {
            var value = first - '0';
            var count = 1;
            while (count < 3 && _position < _input.Length &&
                   _input[_position] is >= '0' and <= '7')
            {
                value = value * 8 + _input[_position++] - '0';
                count++;
            }
            return (char)value;
        }

        private void SkipCurrentList()
        {
            var depth = 1;
            var inString = false;
            while (_position < _input.Length && depth > 0)
            {
                var character = _input[_position++];
                if (character == '\\' && inString && _position < _input.Length)
                {
                    _position++;
                }
                else if (character == '"')
                {
                    inString = !inString;
                }
                else if (!inString && character == '(')
                {
                    depth++;
                }
                else if (!inString && character == ')')
                {
                    depth--;
                }
            }
        }

        private bool TryConsume(char character)
        {
            SkipWhitespace();
            if (_position >= _input.Length || _input[_position] != character)
            {
                return false;
            }
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _input.Length && char.IsWhiteSpace(_input[_position]))
            {
                _position++;
            }
        }
    }
}
