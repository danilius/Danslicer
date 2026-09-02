using System.Globalization;

namespace Danslicer.Core.Utilities;

public enum UnitKind
{
    /// <summary>Base unit millimetres.</summary>
    Length,
    /// <summary>Base unit degrees.</summary>
    Angle,
    /// <summary>Dimensionless; percent is accepted.</summary>
    Scalar,
}

/// <summary>
/// Evaluates arithmetic expressions with unit suffixes, as typed into numeric fields.
/// Examples: "12.5", "10 + 2*3", "1in + 5mm", "90deg", "(4 - 1) / 2 cm".
/// Result is in the base unit of the requested kind.
/// </summary>
public static class ExpressionParser
{
    public static bool TryEvaluate(string text, UnitKind kind, out double value)
    {
        try
        {
            value = Evaluate(text, kind);
            return true;
        }
        catch (FormatException)
        {
            value = 0;
            return false;
        }
    }

    public static double Evaluate(string text, UnitKind kind)
    {
        var parser = new Parser(text, kind);
        var result = parser.ParseExpression();
        parser.ExpectEnd();
        return result;
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly UnitKind _kind;
        private int _pos;

        public Parser(string text, UnitKind kind)
        {
            _text = text;
            _kind = kind;
        }

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Match('+')) value += ParseTerm();
                else if (Match('-')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (Match('*')) value *= ParseFactor();
                else if (Match('/'))
                {
                    var divisor = ParseFactor();
                    if (divisor == 0) throw new FormatException("Division by zero.");
                    value /= divisor;
                }
                else return value;
            }
        }

        private double ParseFactor()
        {
            SkipWhitespace();
            if (Match('-')) return -ParseFactor();
            if (Match('+')) return ParseFactor();
            if (Match('('))
            {
                var inner = ParseExpression();
                SkipWhitespace();
                if (!Match(')')) throw new FormatException("Expected ')'.");
                return inner * ParseOptionalUnit();
            }
            return ParseNumber() * ParseOptionalUnit();
        }

        private double ParseNumber()
        {
            SkipWhitespace();
            var start = _pos;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
                _pos++;
            if (_pos < _text.Length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
            {
                var save = _pos;
                _pos++;
                if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                if (_pos < _text.Length && char.IsDigit(_text[_pos]))
                    while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
                else
                    _pos = save;
            }
            if (start == _pos) throw new FormatException($"Expected a number at position {start}.");
            var token = _text.AsSpan(start, _pos - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"Invalid number '{token}'.");
            return value;
        }

        private double ParseOptionalUnit()
        {
            SkipWhitespace();
            var start = _pos;
            while (_pos < _text.Length && (char.IsLetter(_text[_pos]) || _text[_pos] is '"' or '°' or '%'))
                _pos++;
            if (start == _pos) return 1;

            var unit = _text.Substring(start, _pos - start).ToLowerInvariant();
            return _kind switch
            {
                UnitKind.Length => unit switch
                {
                    "mm" => 1,
                    "cm" => 10,
                    "m" => 1000,
                    "in" or "\"" or "inch" => 25.4,
                    "um" or "µm" or "micron" or "microns" => 0.001,
                    _ => throw new FormatException($"Unknown length unit '{unit}'."),
                },
                UnitKind.Angle => unit switch
                {
                    "deg" or "°" or "d" => 1,
                    "rad" or "r" => 180.0 / Math.PI,
                    _ => throw new FormatException($"Unknown angle unit '{unit}'."),
                },
                UnitKind.Scalar => unit switch
                {
                    "%" => 0.01,
                    "x" => 1,
                    _ => throw new FormatException($"Unknown unit '{unit}'."),
                },
                _ => throw new FormatException("Unknown unit kind."),
            };
        }

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_pos != _text.Length)
                throw new FormatException($"Unexpected '{_text[_pos]}' at position {_pos}.");
        }

        private bool Match(char c)
        {
            if (_pos < _text.Length && _text[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        private void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }
    }
}
