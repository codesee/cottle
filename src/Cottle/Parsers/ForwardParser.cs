﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Cottle.Builtins;
using Cottle.Parsers.Forward;

namespace Cottle.Parsers
{
    internal class ForwardParser : IParser
    {
        private static readonly Dictionary<string, Keyword> Keywords = new Dictionary<string, Keyword>
        {
            ["_"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateComment(out c, out f), false),
            ["declare"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateDeclare(out c, out f), true),
            ["define"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateDefine(out c, out f), true),
            ["dump"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateDump(out c, out f), true),
            ["echo"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateEcho(out c, out f), true),
            ["for"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateFor(out c, out f), true),
            ["if"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateIfThen(out c, out f), true),
            ["return"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateReturn(out c, out f), false),
            ["set"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateSet(out c, out f), true),
            ["unwrap"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateUnwrap(out c, out f), true),
            ["while"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateWhile(out c, out f), true),
            ["wrap"] = new Keyword((ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                p.TryCreateWrap(out c, out f), true)
        };

        private readonly Lexer _lexer;
        private readonly Func<string, string> _trimmer;

        public ForwardParser(string blockBegin, string blockContinue, string blockEnd, char escape,
            Func<string, string> trimmer)
        {
            _lexer = new Lexer(blockBegin, blockContinue, blockEnd, escape);
            _trimmer = trimmer;
        }

        public bool Parse(TextReader reader, out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            _lexer.Reset(reader);
            _lexer.NextRaw();

            if (!TryParseStatement(out statement, out reports))
                return false;

            if (_lexer.Current.Type != LexemType.EndOfFile)
            {
                reports = CreateReportsExpected("end of file");

                return false;
            }

            reports = Array.Empty<DocumentReport>();

            return true;
        }

        private static Expression BuildInvoke(IFunction function, params Expression[] arguments)
        {
            var source = Expression.CreateConstant(Value.FromFunction(function));

            return Expression.CreateInvoke(source, arguments);
        }

        private bool TryCreateComment(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            do
            {
                _lexer.NextRaw();
            } while (_lexer.Current.Type == LexemType.Text);

            statement = null;
            reports = Array.Empty<DocumentReport>();

            return true;
        }

        private bool TryCreateDeclare(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            return TryParseAssignment(StoreMode.Local, out statement, out reports);
        }

        private bool TryCreateDefine(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            var obsoletes = CreateReportsObsolete("keyword \"define\"", "keyword \"declare\"");
            var result = TryParseAssignment(StoreMode.Global, out statement, out var assignmentReports);

            reports = assignmentReports.Concat(obsoletes);

            return result;
        }

        private bool TryCreateDump(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            return TryParseStatementOperand(Statement.CreateDump, out statement, out reports);
        }

        private bool TryCreateEcho(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            return TryParseStatementOperand(Statement.CreateEcho, out statement, out reports);
        }

        private bool TryCreateFor(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            if (!TryParseSymbol(out var element, out reports))
            {
                statement = default;

                return false;
            }

            string key;
            string value;

            if (_lexer.Current.Type == LexemType.Comma)
            {
                _lexer.NextBlock();

                if (!TryParseSymbol(out value, out reports))
                {
                    statement = default;

                    return false;
                }

                key = element;
            }
            else
            {
                key = string.Empty;
                value = element;
            }

            if (!TryParseExpected(LexemType.Symbol, "in", "'in' keyword", out reports) ||
                !TryParseExpression(out var source, out reports) ||
                !TryParseStatementBody(out var body, out reports))
            {
                statement = default;

                return false;
            }

            Statement empty;

            if (_lexer.Current.Type == LexemType.BlockContinue)
            {
                _lexer.NextBlock();

                if (!TryParseExpected(LexemType.Symbol, "empty", "'empty' keyword", out reports) ||
                    !TryParseStatementBody(out empty, out reports))
                {
                    statement = default;

                    return false;
                }
            }
            else
                empty = Statement.NoOp;

            statement = Statement.CreateFor(key, value, source, body, empty);
            reports = Array.Empty<DocumentReport>();

            return true;
        }

        private bool TryCreateIfThen(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            if (!TryParseExpression(out var ifCondition, out reports) ||
                !TryParseStatementBody(out var ifBody, out reports))
            {
                statement = null;

                return false;
            }

            var branches = new List<(Expression, Statement)>();
            var final = Statement.NoOp;
            var next = true;

            branches.Add((ifCondition, ifBody));

            while (next && _lexer.Current.Type == LexemType.BlockContinue)
            {
                _lexer.NextBlock();

                switch (_lexer.Current.Type == LexemType.Symbol ? _lexer.Current.Value : string.Empty)
                {
                    case "elif":
                        _lexer.NextBlock();

                        if (!TryParseExpression(out var elifCondition, out reports) ||
                            !TryParseStatementBody(out var elifBody, out reports))
                        {
                            statement = default;

                            return false;
                        }

                        branches.Add((elifCondition, elifBody));

                        break;

                    case "else":
                        _lexer.NextBlock();

                        if (!TryParseStatementBody(out var elseBody, out reports))
                        {
                            statement = default;

                            return false;
                        }

                        final = elseBody;
                        next = false;

                        break;

                    default:
                        statement = default;
                        reports = CreateReportsExpected("'elif' or 'else' keyword");

                        return false;
                }
            }

            for (var i = branches.Count - 1; i >= 0; --i)
                final = Statement.CreateIf(branches[i].Item1, branches[i].Item2, final);

            statement = final;
            reports = Array.Empty<DocumentReport>();

            return true;
        }

        private bool TryCreateReturn(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            return TryParseStatementOperand(Statement.CreateReturn, out statement, out reports);
        }

        private bool TryCreateSet(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            return TryParseAssignment(StoreMode.Global, out statement, out reports);
        }

        private bool TryCreateUnwrap(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            if (!TryParseStatementBody(out var body, out reports))
            {
                statement = default;

                return false;
            }

            statement = Statement.CreateUnwrap(body);

            return true;
        }

        private bool TryCreateWhile(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            if (!TryParseExpression(out var condition, out reports) ||
                !TryParseStatementBody(out var body, out reports))
            {
                statement = default;

                return false;
            }

            statement = Statement.CreateWhile(condition, body);

            return true;
        }

        private bool TryCreateWrap(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            if (!TryParseExpression(out var wrapper, out reports) ||
                !TryParseStatementBody(out var body, out reports))
            {
                statement = default;

                return false;
            }

            statement = Statement.CreateWrap(wrapper, body);

            return true;
        }

        private bool TryParseAssignment(StoreMode mode, out Statement statement,
            out IEnumerable<DocumentReport> reports)
        {
            List<string> arguments;

            if (!TryParseSymbol(out var name, out reports))
            {
                statement = default;

                return false;
            }

            // Parse function arguments if provided
            if (_lexer.Current.Type == LexemType.ParenthesisBegin)
            {
                arguments = new List<string>();

                for (_lexer.NextBlock(); _lexer.Current.Type != LexemType.ParenthesisEnd;)
                {
                    if (!TryParseSymbol(out var symbol, out reports))
                    {
                        statement = default;

                        return false;
                    }

                    arguments.Add(symbol);

                    if (_lexer.Current.Type == LexemType.Comma)
                        _lexer.NextBlock();
                }

                _lexer.NextBlock();
            }
            else
                arguments = null;

            // Early exit if no body, render nor value is defined
            if (_lexer.Current.Type != LexemType.Symbol)
            {
                _lexer.NextRaw();

                // Arguments were defined, build function assignment
                if (arguments != null)
                {
                    statement = Statement.CreateAssignFunction(name, arguments, mode, Statement.NoOp);
                    reports = Array.Empty<DocumentReport>();

                    return true;
                }

                // Arguments where not defined, build value assignment
                statement = Statement.CreateAssignValue(name, mode, Expression.Void);
                reports = Array.Empty<DocumentReport>();

                return true;
            }

            // Parse 'as' or 'to' keyword
            if (mode == StoreMode.Global)
            {
                // FIXME: raise "notice" event, then remove legacy keyword handling
                if (_lexer.Current.Value == "as")
                {
                    _lexer.NextBlock();

                    mode = StoreMode.Local;
                }
                else
                {
                    if (!TryParseExpected(LexemType.Symbol, "to", "'to' keyword", out reports))
                    {
                        statement = default;

                        return false;
                    }
                }
            }
            else if (!TryParseExpected(LexemType.Symbol, "as", "'as' keyword", out reports))
            {
                statement = default;

                return false;
            }

            // Arguments were defined, build function assignment
            if (arguments != null)
            {
                if (!TryParseStatementBody(out var body, out reports))
                {
                    statement = default;

                    return false;
                }

                statement = Statement.CreateAssignFunction(name, arguments, mode, body);

                return true;
            }

            // No arguments provided and literal body follows, build render assignment
            if (_lexer.Current.Type == LexemType.Colon)
            {
                if (!TryParseStatementBody(out var body, out reports))
                {
                    statement = default;

                    return false;
                }

                statement = Statement.CreateAssignRender(name, mode, body);

                return true;
            }

            // No arguments and no literal body, build value assignment
            if (!TryParseExpression(out var operand, out reports))
            {
                statement = default;

                return false;
            }

            _lexer.NextRaw();

            statement = Statement.CreateAssignValue(name, mode, operand);

            return true;
        }

        private bool TryParseExpected(LexemType type, string value, string message,
            out IEnumerable<DocumentReport> reports)
        {
            if (_lexer.Current.Type != type || _lexer.Current.Value != value)
            {
                reports = CreateReportsExpected(message);

                return false;
            }

            _lexer.NextBlock();

            reports = Array.Empty<DocumentReport>();

            return true;
        }

        private bool TryParseExpression(out Expression expression, out IEnumerable<DocumentReport> reports)
        {
            if (TryParseValue(out var operand, out reports))
                return TryParseExpressionOperand(operand, out expression, out reports);

            expression = default;

            return false;
        }

        private bool TryParseExpressionOperand(Expression head, out Expression expression,
            out IEnumerable<DocumentReport> reports)
        {
            var operands = new Stack<Expression>();
            var operators = new Stack<Operator>();

            operands.Push(head);

            while (true)
            {
                Operator current;

                switch (_lexer.Current.Type)
                {
                    case LexemType.DoubleAmpersand:
                        current = new Operator(BuiltinOperators.OperatorAnd, 0);

                        break;

                    case LexemType.DoublePipe:
                        current = new Operator(BuiltinOperators.OperatorOr, 0);

                        break;

                    case LexemType.Equal:
                        current = new Operator(BuiltinOperators.OperatorEqual, 1);

                        break;

                    case LexemType.GreaterEqual:
                        current = new Operator(BuiltinOperators.OperatorGreaterEqual, 1);

                        break;

                    case LexemType.GreaterThan:
                        current = new Operator(BuiltinOperators.OperatorGreaterThan, 1);

                        break;

                    case LexemType.LowerEqual:
                        current = new Operator(BuiltinOperators.OperatorLowerEqual, 1);

                        break;

                    case LexemType.LowerThan:
                        current = new Operator(BuiltinOperators.OperatorLowerThan, 1);

                        break;

                    case LexemType.Minus:
                        current = new Operator(BuiltinOperators.OperatorSub, 2);

                        break;

                    case LexemType.NotEqual:
                        current = new Operator(BuiltinOperators.OperatorNotEqual, 1);

                        break;

                    case LexemType.Percent:
                        current = new Operator(BuiltinOperators.OperatorMod, 3);

                        break;

                    case LexemType.Plus:
                        current = new Operator(BuiltinOperators.OperatorAdd, 2);

                        break;

                    case LexemType.Slash:
                        current = new Operator(BuiltinOperators.OperatorDiv, 3);

                        break;

                    case LexemType.Star:
                        current = new Operator(BuiltinOperators.OperatorMul, 3);

                        break;

                    default:
                        while (operators.Count > 0)
                        {
                            var remaining = operators.Pop();
                            var value = operands.Pop();

                            operands.Push(ForwardParser.BuildInvoke(remaining.Function, operands.Pop(), value));
                        }

                        expression = operands.Pop();
                        reports = Array.Empty<DocumentReport>();

                        return true;
                }

                _lexer.NextBlock();

                while (operators.Count > 0 && operators.Peek().Precedence >= current.Precedence)
                {
                    var other = operators.Pop();
                    var value = operands.Pop();

                    operands.Push(ForwardParser.BuildInvoke(other.Function, operands.Pop(), value));
                }

                operators.Push(current);

                if (!TryParseValue(out var operand, out reports))
                {
                    expression = default;

                    return false;
                }

                operands.Push(operand);
            }
        }

        private bool TryParseKeyword(out Keyword keyword)
        {
            if (_lexer.Current.Type == LexemType.Symbol &&
                ForwardParser.Keywords.TryGetValue(_lexer.Current.Value, out keyword))
            {
                _lexer.NextBlock();

                return true;
            }

            keyword = default;

            return false;
        }

        private bool TryParseStatement(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            var statements = new List<Statement>();

            while
            (
                _lexer.Current.Type != LexemType.BlockContinue &&
                _lexer.Current.Type != LexemType.BlockEnd &&
                _lexer.Current.Type != LexemType.EndOfFile
            )
            {
                // Parse next statement or exit loop
                switch (_lexer.Current.Type)
                {
                    case LexemType.BlockBegin:
                        _lexer.NextBlock();

                        var blockLexem = _lexer.Current;
                        KeywordParser blockParse;

                        // Case 1: first block lexem is not a keyword, consider it as an implicit "echo" command
                        if (!TryParseKeyword(out var keyword))
                        {
                            blockParse = (ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                                p.TryCreateEcho(out c, out f);
                        }

                        // Case 2: first block lexem is a keyword but is missing mandatory operand, consider it as a
                        // symbol and parse as an implicit "echo" command
                        else if (keyword.HasMandatoryOperand && _lexer.Current.Type == LexemType.None)
                        {
                            var symbol = Expression.CreateSymbol(blockLexem.Value);

                            blockParse = (ForwardParser p, out Statement c, out IEnumerable<DocumentReport> f) =>
                            {
                                if (!TryParseExpressionOperand(symbol, out var operand, out f))
                                {
                                    c = default;

                                    return false;
                                }

                                _lexer.NextRaw();

                                c = Statement.CreateEcho(operand);

                                return true;
                            };
                        }

                        // Case 3: first block lexem is a keyword with acceptable syntax, parse command accordingly
                        else
                            blockParse = keyword.Parse;

                        // Use delegate defined above to parse recognized command
                        if (!blockParse(this, out var blockStatement, out reports))
                        {
                            statement = default;

                            return false;
                        }

                        if (_lexer.Current.Type != LexemType.BlockEnd)
                        {
                            statement = default;
                            reports = CreateReportsExpected("end of block");

                            return false;
                        }

                        // Ignore empty statements
                        if (blockStatement != null)
                            statements.Add(blockStatement);

                        _lexer.NextRaw();

                        break;

                    case LexemType.Text:
                        statements.Add(Statement.CreateLiteral(_trimmer(_lexer.Current.Value)));

                        _lexer.NextRaw();

                        break;

                    default:
                        statement = default;
                        reports = CreateReportsExpected("text or block begin ('{')");

                        return false;
                }
            }

            if (statements.Count < 1)
                statement = Statement.NoOp;
            else if (statements.Count == 1)
                statement = statements[0];
            else
            {
                var composite = statements[statements.Count - 1];

                for (var i = statements.Count - 2; i >= 0; --i)
                    composite = Statement.CreateComposite(statements[i], composite);

                statement = composite;
            }

            reports = Array.Empty<DocumentReport>();

            return true;
        }

        private bool TryParseStatementBody(out Statement statement, out IEnumerable<DocumentReport> reports)
        {
            if (_lexer.Current.Type != LexemType.Colon)
            {
                statement = default;
                reports = CreateReportsExpected("body separator (':')");

                return false;
            }

            _lexer.NextRaw();

            return TryParseStatement(out statement, out reports);
        }

        private bool TryParseStatementOperand(Func<Expression, Statement> constructor, out Statement statement,
            out IEnumerable<DocumentReport> reports)
        {
            if (!TryParseExpression(out var operand, out reports))
            {
                statement = default;

                return false;
            }

            _lexer.NextRaw();

            statement = constructor(operand);

            return true;
        }

        private bool TryParseSymbol(out string name, out IEnumerable<DocumentReport> reports)
        {
            if (_lexer.Current.Type != LexemType.Symbol)
            {
                reports = CreateReportsExpected("symbol (variable name)");
                name = default;

                return false;
            }

            reports = Array.Empty<DocumentReport>();
            name = _lexer.Current.Value;

            _lexer.NextBlock();

            return true;
        }

        private bool TryParseValue(out Expression expression, out IEnumerable<DocumentReport> reports)
        {
            switch (_lexer.Current.Type)
            {
                case LexemType.Bang:
                    _lexer.NextBlock();

                    if (!TryParseValue(out var notExpression, out reports))
                    {
                        expression = default;

                        return false;
                    }

                    expression = ForwardParser.BuildInvoke(BuiltinOperators.OperatorNot, notExpression);
                    reports = Array.Empty<DocumentReport>();

                    return true;

                case LexemType.BracketBegin:
                    var elements = new List<ExpressionElement>();
                    var index = 0;

                    for (_lexer.NextBlock(); _lexer.Current.Type != LexemType.BracketEnd;)
                    {
                        if (!TryParseExpression(out var element, out reports))
                        {
                            expression = default;

                            return false;
                        }

                        Expression key;
                        Expression value;

                        if (_lexer.Current.Type == LexemType.Colon)
                        {
                            _lexer.NextBlock();

                            if (!TryParseExpression(out value, out reports))
                            {
                                expression = default;

                                return false;
                            }

                            if (element.Type == ExpressionType.Constant && element.Value.Type == ValueContent.Number &&
                                Math.Abs(element.Value.AsNumber - index) < double.Epsilon)
                            {
                                ++index;
                            }

                            key = element;
                        }
                        else
                        {
                            key = Expression.CreateConstant(index++);
                            value = element;
                        }

                        elements.Add(new ExpressionElement(key, value));

                        if (_lexer.Current.Type == LexemType.Comma)
                            _lexer.NextBlock();
                    }

                    expression = Expression.CreateMap(elements);

                    _lexer.NextBlock();

                    break;

                case LexemType.Minus:
                    _lexer.NextBlock();

                    if (!TryParseValue(out var minusRhs, out reports))
                    {
                        expression = default;

                        return false;
                    }

                    var minusLhs = Expression.CreateConstant(0);

                    expression = ForwardParser.BuildInvoke(BuiltinOperators.OperatorSub, minusLhs, minusRhs);
                    reports = Array.Empty<DocumentReport>();

                    return true;

                case LexemType.Number:
                    if (!double.TryParse(_lexer.Current.Value, NumberStyles.Number, CultureInfo.InvariantCulture,
                        out var number))
                        number = 0;

                    expression = Expression.CreateConstant(number);

                    _lexer.NextBlock();

                    break;

                case LexemType.ParenthesisBegin:
                    _lexer.NextBlock();

                    if (!TryParseExpression(out expression, out reports))
                        return false;

                    if (_lexer.Current.Type != LexemType.ParenthesisEnd)
                    {
                        reports = CreateReportsExpected("parenthesis end (')')");

                        return false;
                    }

                    _lexer.NextBlock();

                    reports = Array.Empty<DocumentReport>();

                    return true;

                case LexemType.Plus:
                    _lexer.NextBlock();

                    return TryParseValue(out expression, out reports);

                case LexemType.String:
                    expression = Expression.CreateConstant(_lexer.Current.Value);

                    _lexer.NextBlock();

                    break;

                case LexemType.Symbol:
                    expression = Expression.CreateSymbol(_lexer.Current.Value);

                    _lexer.NextBlock();

                    break;

                default:
                    expression = default;
                    reports = CreateReportsExpected("expression");

                    return false;
            }

            while (true)
            {
                switch (_lexer.Current.Type)
                {
                    case LexemType.BracketBegin:
                        _lexer.NextBlock();

                        if (!TryParseExpression(out var subscript, out reports))
                            return false;

                        if (_lexer.Current.Type != LexemType.BracketEnd)
                        {
                            reports = CreateReportsExpected("array index end (']')");

                            return false;
                        }

                        _lexer.NextBlock();

                        expression = Expression.CreateAccess(expression, subscript);

                        break;

                    case LexemType.Dot:
                        _lexer.NextBlock();

                        if (_lexer.Current.Type != LexemType.Symbol)
                        {
                            reports = CreateReportsExpected("field name");

                            return false;
                        }

                        expression =
                            Expression.CreateAccess(expression, Expression.CreateConstant(_lexer.Current.Value));

                        _lexer.NextBlock();

                        break;

                    case LexemType.ParenthesisBegin:
                        var arguments = new List<Expression>();

                        for (_lexer.NextBlock(); _lexer.Current.Type != LexemType.ParenthesisEnd;)
                        {
                            if (!TryParseExpression(out var argument, out reports))
                                return false;

                            arguments.Add(argument);

                            if (_lexer.Current.Type == LexemType.Comma)
                                _lexer.NextBlock();
                        }

                        _lexer.NextBlock();

                        expression = Expression.CreateInvoke(expression, arguments);

                        break;

                    default:
                        reports = Array.Empty<DocumentReport>();

                        return true;
                }
            }
        }

        private IEnumerable<DocumentReport> CreateReportsExpected(string expected)
        {
            var current = _lexer.Current;
            var message = $"expected {expected}, found {current.Value}";

            return new[] { new DocumentReport(DocumentSeverity.Error, current.Offset, current.Length, message) };
        }

        private IEnumerable<DocumentReport> CreateReportsObsolete(string obsolete, string replacement)
        {
            var current = _lexer.Current;
            var message = $"{obsolete} is obsolete, please replace with {replacement}";

            return new[] { new DocumentReport(DocumentSeverity.Notice, current.Offset, current.Length, message) };
        }
    }
}