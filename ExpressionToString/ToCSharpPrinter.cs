/*
The MIT License (MIT)

Copyright (c) 2016-2020 Maksim Volkau

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace FastExpressionCompiler {
/// <summary>Converts the expression into the valid C# code representation</summary>
public static class ToCSharpPrinter {
    /// <summary>Tries hard to convert the expression into the correct C# code</summary>
    public static string ToCSharpString(this Expression expr, CodePrinter.IObjectToCode cPrinter = null) =>
        expr.ToCSharpString(new StringBuilder(1024), cPrinter ?? CodePrinter.DefaultConstantValueToCode, 4, true)
            .Append(';').ToString();

    /// <summary>Tries hard to convert the expression into the correct C# code</summary>
    public static StringBuilder ToCSharpString(this Expression e, StringBuilder sb, CodePrinter.IObjectToCode cPrinter,
        int lineIdent = 0, bool stripNamespace = false, Func<Type, string, string> printType = null,
        int identSpaces = 4) {
        switch (e.NodeType) {
            case ExpressionType.Constant: {
                var x = (ConstantExpression) e;
                if (x.Value == null)
                    return sb.Append("null");

                if (x.Value is Type t)
                    return sb.AppendTypeof(t, stripNamespace, printType);

                if (x.Value.GetType() != x.Type) // add the cast
                    sb.Append('(').Append(x.Type.ToCode(stripNamespace, printType)).Append(')');

                return sb.Append(x.Value.ToCode(cPrinter, stripNamespace, printType));
            }
            case ExpressionType.Parameter: {
                return sb.AppendName(((ParameterExpression) e).Name, e.Type, e);
            }
            case ExpressionType.New: {
                var x = (NewExpression) e;
                sb.Append("new ").Append(e.Type.ToCode(stripNamespace, printType)).Append('(');
                var args = x.Arguments;
                if (args.Count == 1)
                    args[0].ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                else if (args.Count > 1)
                    for (var i = 0; i < args.Count; i++) {
                        if (i > 0) sb.Append(", ");
                        args[i].ToCSharpString(sb, cPrinter, lineIdent + identSpaces, stripNamespace, printType,
                            identSpaces);
                    }
                return sb.Append(')');
            }
            case ExpressionType.Call: {
                var x = (MethodCallExpression) e;
                if (x.Object != null)
                    x.Object.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                else // for the static method or the static extension method we need to qualify with the class
                    sb.Append(x.Method.DeclaringType.ToCode(stripNamespace, printType));

                var name = x.Method.Name;
                // check for the special methods, e.g. property access `get_` or `set_` and output them as properties
                if (x.Method.IsSpecialName) {
                    if (name.StartsWith("get_") || name.StartsWith("set_"))
                        return sb.Append('.').Append(name.Substring(4));
                }

                sb.Append('.').Append(name);
                if (x.Method.IsGenericMethod) {
                    sb.Append('<');
                    var typeArgs = x.Method.GetGenericArguments();
                    for (var i = 0; i < typeArgs.Length; i++)
                        (i == 0 ? sb : sb.Append(", ")).Append(typeArgs[i].ToCode(stripNamespace, printType));
                    sb.Append('>');
                }

                sb.Append('(');
                var pars = x.Method.GetParameters();
                var args = x.Arguments;
                if (args.Count == 1) {
                    var p = pars[0];
                    if (p.ParameterType.IsByRef)
                        sb.Append(p.IsOut ? "out " : p.IsIn ? "in" : "ref ");
                    args[0].ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                } else if (args.Count > 1) {
                    for (var i = 0; i < args.Count; i++) {
                        if (i > 0) sb.Append(", ");
                        var p = pars[i];
                        if (p.ParameterType.IsByRef)
                            sb.Append(p.IsOut ? "out " : p.IsIn ? "in " : "ref ");

                        args[i].ToCSharpString(sb, cPrinter, lineIdent + identSpaces, stripNamespace, printType,
                            identSpaces);
                    }
                }
                return sb.Append(')');
            }
            case ExpressionType.MemberAccess: {
                var x = (MemberExpression) e;
                if (x.Expression != null)
                    x.Expression.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                else
                    sb.NewLineIdent(lineIdent).Append(x.Member.DeclaringType.ToCode(stripNamespace, printType));
                return sb.Append('.').Append(x.Member.GetCSharpName());
            }
            case ExpressionType.NewArrayBounds:
            case ExpressionType.NewArrayInit: {
                var x = (NewArrayExpression) e;
                sb.Append("new ").Append(e.Type.GetElementType().ToCode(stripNamespace, printType));
                sb.Append(e.NodeType == ExpressionType.NewArrayInit ? "[] {" : "[");

                var exprs = x.Expressions;
                if (exprs.Count == 1)
                    exprs[0].ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                else if (exprs.Count > 1)
                    for (var i = 0; i < exprs.Count; i++)
                        exprs[i].ToCSharpString(
                            (i > 0 ? sb.Append(',') : sb).NewLineIdent(lineIdent), cPrinter,
                            lineIdent + identSpaces, stripNamespace, printType, identSpaces);

                return sb.Append(e.NodeType == ExpressionType.NewArrayInit ? "}" : "]");
            }
            case ExpressionType.MemberInit: {
                var x = (MemberInitExpression) e;
                x.NewExpression.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                sb.NewLine(lineIdent, identSpaces).Append(" { ");
                x.Bindings.ToCSharpString(sb, cPrinter, lineIdent + identSpaces, stripNamespace, printType,
                    identSpaces);
                return sb.NewLine(lineIdent, identSpaces).Append('}');
            }
            case ExpressionType.ListInit: {
                var x = (ListInitExpression) e;
                x.NewExpression.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                sb.NewLine(lineIdent, identSpaces).Append(" { ");

                var inits = x.Initializers;
                for (var i = 0; i < inits.Count; ++i) {
                    (i == 0 ? sb : sb.Append(", ")).NewLineIdent(lineIdent);
                    var elemInit = inits[i];
                    var args = elemInit.Arguments;
                    if (args.Count == 1) {
                        args.GetArgument(0).ToCSharpString(sb, cPrinter, lineIdent + identSpaces, stripNamespace,
                            printType, identSpaces);
                    } else {
                        sb.Append('(');
                        for (var j = 0; j < args.Count; ++j)
                            args.GetArgument(j).ToCSharpString(j == 0 ? sb : sb.Append(", "), cPrinter,
                                lineIdent + identSpaces, stripNamespace, printType, identSpaces);
                        sb.Append(')');
                    }
                }
                return sb.NewLine(lineIdent, identSpaces).Append('}');
            }
            case ExpressionType.Lambda: {
                var x = (LambdaExpression) e;
                // The result should be something like this (taken from the #237)
                //
                // `(DeserializerDlg<Word>)(ref ReadOnlySequence<Byte> input, Word value, out Int64 bytesRead) => {...})`
                // 
                sb.Append('(').Append(e.Type.ToCode(stripNamespace, printType)).Append(")((");
                var count = x.Parameters.Count;
                if (count > 0) {
                    var pars = x.Type.FindDelegateInvokeMethod().GetParameters();

                    for (var i = 0; i < count; i++) {
                        if (i > 0)
                            sb.Append(", ");

                        var pe = x.Parameters[i];
                        var p = pars[i];
                        if (pe.IsByRef)
                            sb.Append(p.IsOut ? "out " : p.IsIn ? "in " : "ref ");
                        sb.Append(pe.Type.ToCode(stripNamespace, printType)).Append(' ');
                        sb.AppendName(pe.Name, pe.Type, pe);
                    }
                }

                sb.Append(") => ");
                if (x.ReturnType != typeof(void) && x.Body is BlockExpression == false)
                    x.Body.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType).Append(')');
                else {
                    sb.Append('{');

                    // Body handles ident and `;` itself
                    if (x.Body is BlockExpression blockBody)
                        blockBody.BlockToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces,
                            inTheLastBlock: true);
                    else
                        sb.NewLineIdentCs(cPrinter, x.Body, lineIdent, stripNamespace, printType).Append(';');

                    sb.NewLine(lineIdent, identSpaces).Append("})");
                }
                return sb;
            }
            case ExpressionType.Invoke: {
                var x = (InvocationExpression) e;
                sb.Append("new ").Append(x.Expression.Type.ToCode(stripNamespace, printType)).Append("(");
                sb.NewLineIdentCs(cPrinter, x.Expression, lineIdent, stripNamespace, printType, identSpaces);
                sb.Append(").Invoke(");
                for (var i = 0; i < x.Arguments.Count; i++)
                    (i > 0 ? sb.Append(',') : sb)
                        .NewLineIdentCs(cPrinter, x.Arguments[i], lineIdent, stripNamespace, printType, identSpaces);
                return sb.Append(")");
            }
            case ExpressionType.Conditional: {
                var x = (ConditionalExpression) e;
                if (e.Type == typeof(void)) // otherwise output as ternary expression
                {
                    sb.Append("if (");
                    x.Test.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                    sb.Append(") {");

                    if (x.IfTrue is BlockExpression)
                        x.IfTrue.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                    else
                        sb.NewLineIdentCs(cPrinter, x.IfTrue, lineIdent, stripNamespace, printType, identSpaces)
                            .Append(';');

                    sb.NewLine(lineIdent, identSpaces).Append('}');
                    if (x.IfFalse.NodeType != ExpressionType.Default || x.IfFalse.Type != typeof(void)) {
                        sb.Append(" else {");

                        if (x.IfFalse is BlockExpression bl)
                            bl.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                        else
                            sb.NewLineIdentCs(cPrinter, x.IfFalse, lineIdent, stripNamespace, printType, identSpaces)
                                .Append(';');
                        sb.NewLine(lineIdent, identSpaces).Append('}');
                    }
                } else {
                    x.Test.ToCSharpString(sb.Append('('), cPrinter, lineIdent, stripNamespace, printType, identSpaces)
                        .Append(" ?");
                    sb.NewLineIdentCs(cPrinter, x.IfTrue, lineIdent, stripNamespace, printType, identSpaces)
                        .Append(" :");
                    sb.NewLineIdentCs(cPrinter, x.IfFalse, lineIdent, stripNamespace, printType, identSpaces)
                        .Append(')');
                }
                return sb;
            }
            case ExpressionType.Block: {
                return BlockToCSharpString((BlockExpression) e, sb, cPrinter, lineIdent, stripNamespace, printType,
                    identSpaces);
            }
            case ExpressionType.Loop: {
                var x = (LoopExpression) e;
                sb.NewLine(lineIdent, identSpaces).Append("while (true)");
                sb.NewLine(lineIdent, identSpaces).Append("{");

                if (x.ContinueLabel != null) {
                    sb.NewLine(lineIdent, identSpaces);
                    x.ContinueLabel.ToCSharpString(sb).Append(": ");
                }

                x.Body.ToCSharpString(sb, cPrinter, lineIdent + identSpaces, stripNamespace, printType, identSpaces);

                sb.NewLine(lineIdent, identSpaces).Append("}");

                if (x.BreakLabel != null) {
                    sb.NewLine(lineIdent, identSpaces);
                    x.BreakLabel.ToCSharpString(sb).Append(": ");
                }
                return sb;
            }
            case ExpressionType.Index: {
                var x = (IndexExpression) e;
                x.Object.ToCSharpString(sb, cPrinter, lineIdent + identSpaces, stripNamespace, printType, identSpaces);

                var isStandardIndexer = x.Indexer == null || x.Indexer.Name == "Item";
                if (isStandardIndexer)
                    sb.Append('[');
                else
                    sb.Append('.').Append(x.Indexer.Name).Append('(');

                for (var i = 0; i < x.Arguments.Count; i++)
                    x.Arguments[i].ToCSharpString(i > 0 ? sb.Append(", ") : sb, cPrinter,
                        lineIdent + identSpaces, stripNamespace, printType, identSpaces);

                return sb.Append(isStandardIndexer ? ']' : ')');
            }
            case ExpressionType.Try: {
                var x = (TryExpression) e;
                sb.Append("try");
                sb.NewLine(lineIdent, identSpaces).Append('{');
                sb.NewLineIdentCs(cPrinter, x.Body, lineIdent, stripNamespace, printType, identSpaces);
                sb.NewLine(lineIdent, identSpaces).Append('}');

                var handlers = x.Handlers;
                if (handlers != null && handlers.Count > 0) {
                    for (var i = 0; i < handlers.Count; i++) {
                        var h = handlers[i];
                        sb.NewLine(lineIdent, identSpaces).Append("catch (");
                        var exTypeName = h.Test.ToCode(stripNamespace, printType);
                        sb.Append(exTypeName);

                        if (h.Variable != null)
                            sb.AppendName(h.Variable.Name, h.Variable.Type, h.Variable);

                        sb.Append(')');
                        if (h.Filter != null) {
                            sb.Append("when (");
                            sb.NewLineIdentCs(cPrinter, h.Filter, lineIdent, stripNamespace, printType, identSpaces);
                            sb.NewLine(lineIdent, identSpaces).Append(')');
                        }
                        sb.NewLine(lineIdent, identSpaces).Append('{');
                        sb.NewLineIdentCs(cPrinter, h.Body, lineIdent, stripNamespace, printType, identSpaces);
                        sb.NewLine(lineIdent, identSpaces).Append('}');
                    }
                }

                if (x.Finally != null) {
                    sb.Append("finally");
                    sb.NewLine(lineIdent, identSpaces).Append('{');
                    sb.NewLineIdentCs(cPrinter, x.Finally, lineIdent, stripNamespace, printType, identSpaces);
                    sb.NewLine(lineIdent, identSpaces).Append('}');
                }
                return sb;
            }
            case ExpressionType.Label: {
                var x = (LabelExpression) e;
                sb.NewLineIdent(lineIdent);
                x.Target.ToCSharpString(sb).Append(':');
                if (x.DefaultValue == null)
                    return sb;

                sb.NewLineIdent(lineIdent).Append("return ");
                x.DefaultValue.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                return sb.Append(';');
            }
            case ExpressionType.Goto: {
                return ((GotoExpression) e).Target.ToCSharpString(sb.Append("goto "));
            }
            case ExpressionType.Switch: {
                var x = (SwitchExpression) e;
                sb.Append("switch (");
                x.SwitchValue.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                sb.Append(") {");

                foreach (var cs in x.Cases) {
                    sb.NewLineIdent(lineIdent);
                    foreach (var tv in cs.TestValues) {
                        sb.Append("case ");
                        tv.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                        sb.Append(':');
                        sb.NewLineIdent(lineIdent);
                    }

                    cs.Body.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                }

                if (x.DefaultBody != null) {
                    sb.NewLineIdent(lineIdent).Append("default:");
                    x.DefaultBody.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                }

                return sb.NewLineIdent(lineIdent).Append("}");
            }
            case ExpressionType.Default: {
                return e.Type == typeof(void) ?
                    sb // `default(void)` does not make sense in the C#
                    :
                    sb.Append("default(").Append(e.Type.ToCode(stripNamespace, printType)).Append(')');
            }
            case ExpressionType.TypeIs:
            case ExpressionType.TypeEqual: {
                var x = (TypeBinaryExpression) e;
                sb.Append('(');
                x.Expression.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                sb.Append(" is ").Append(x.TypeOperand.ToCode(stripNamespace, printType));
                return sb.Append(')');
            }
            case ExpressionType.Coalesce: {
                var x = (BinaryExpression) e;
                x.Left.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                sb.Append(" ?? ").NewLineIdent(lineIdent);
                return x.Right.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
            }
            case ExpressionType.Extension: {
                var reduced = e.Reduce(); // proceed with the reduced expression
                return reduced.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
            }
            case ExpressionType.Dynamic:
            case ExpressionType.RuntimeVariables:
            case ExpressionType.DebugInfo:
            case ExpressionType.Quote: {
                return sb.NewLineIdent(lineIdent).Append(NotSupportedExpression).Append(e.NodeType)
                    .NewLineIdent(lineIdent);
            }
            default: {
                var name = Enum.GetName(typeof(ExpressionType), e.NodeType);
                if (e is UnaryExpression u) {
                    var op = u.Operand;
                    switch (e.NodeType) {
                        case ExpressionType.ArrayLength:
                            return op.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces)
                                .Append(".Length");

                        case ExpressionType.Not: // either the bool not or the binary not
                            return op.ToCSharpString(
                                e.Type == typeof(bool) ? sb.Append("!(") : sb.Append("~("), cPrinter,
                                lineIdent, stripNamespace, printType, identSpaces).Append(')');

                        case ExpressionType.Convert:
                        case ExpressionType.ConvertChecked:
                            sb.Append("((").Append(e.Type.ToCode(stripNamespace, printType)).Append(")(");
                            return op.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces)
                                .Append("))");

                        case ExpressionType.Decrement:
                            return op.ToCSharpString(sb.Append('('), cPrinter, lineIdent, stripNamespace, printType,
                                identSpaces).Append(" - 1)");

                        case ExpressionType.Increment:
                            return op.ToCSharpString(sb.Append('('), cPrinter, lineIdent, stripNamespace, printType,
                                identSpaces).Append(" + 1)");

                        case ExpressionType.Negate:
                        case ExpressionType.NegateChecked:
                            return op.ToCSharpString(sb.Append("(-"), cPrinter, lineIdent, stripNamespace, printType,
                                identSpaces).Append(')');

                        case ExpressionType.PostIncrementAssign:
                            return op.ToCSharpString(sb.Append('('), cPrinter, lineIdent, stripNamespace, printType,
                                identSpaces).Append("++)");

                        case ExpressionType.PreIncrementAssign:
                            return op.ToCSharpString(sb.Append("(++"), cPrinter, lineIdent, stripNamespace, printType,
                                identSpaces).Append(')');

                        case ExpressionType.PostDecrementAssign:
                            return op.ToCSharpString(sb.Append('('), cPrinter, lineIdent, stripNamespace, printType,
                                identSpaces).Append("--)");

                        case ExpressionType.PreDecrementAssign:
                            return op.ToCSharpString(sb.Append("(--"), cPrinter, lineIdent, stripNamespace, printType,
                                identSpaces).Append(')');

                        case ExpressionType.IsTrue:
                            return op.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces)
                                .Append("==true");

                        case ExpressionType.IsFalse:
                            return op.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces)
                                .Append("==false");

                        case ExpressionType.TypeAs:
                            op.ToCSharpString(sb.Append('('), cPrinter, lineIdent, stripNamespace, printType,
                                identSpaces);
                            return sb.Append(" as ").Append(e.Type.ToCode(stripNamespace, printType)).Append(')');

                        case ExpressionType.TypeIs:
                            op.ToCSharpString(sb.Append('('), cPrinter, lineIdent, stripNamespace, printType,
                                identSpaces);
                            return sb.Append(" is ").Append(e.Type.ToCode(stripNamespace, printType)).Append(')');

                        case ExpressionType.Throw:
                            sb.Append("throw ");
                            return op.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces)
                                .Append(';');

                        case ExpressionType.Unbox: // output it as the cast 
                            sb.Append("((").Append(e.Type.ToCode(stripNamespace, printType)).Append(')');
                            return op.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces)
                                .Append(')');

                        default:
                            return sb.Append(e.ToString()); // falling back ro ToString as a closest to C# code output 
                    }
                }

                if (e is BinaryExpression b) {
                    if (e.NodeType == ExpressionType.ArrayIndex) {
                        b.Left.ToCSharpString(sb.Append('('), cPrinter, lineIdent, stripNamespace, printType,
                            identSpaces).Append(')');
                        return b.Right.ToCSharpString(sb.Append("["), cPrinter, lineIdent, stripNamespace, printType,
                            identSpaces).Append("]");
                    }

                    if (e.NodeType == ExpressionType.Assign ||
                        e.NodeType == ExpressionType.PowerAssign ||
                        e.NodeType == ExpressionType.AndAssign ||
                        e.NodeType == ExpressionType.OrAssign ||
                        e.NodeType == ExpressionType.AddAssign ||
                        e.NodeType == ExpressionType.ExclusiveOrAssign ||
                        e.NodeType == ExpressionType.AddAssignChecked ||
                        e.NodeType == ExpressionType.SubtractAssign ||
                        e.NodeType == ExpressionType.SubtractAssignChecked ||
                        e.NodeType == ExpressionType.MultiplyAssign ||
                        e.NodeType == ExpressionType.MultiplyAssignChecked ||
                        e.NodeType == ExpressionType.DivideAssign ||
                        e.NodeType == ExpressionType.LeftShiftAssign ||
                        e.NodeType == ExpressionType.RightShiftAssign ||
                        e.NodeType == ExpressionType.ModuloAssign
                    ) {
                        // todo: @incomplete handle the right part is condition with the blocks for If and/or Else, e.g. see #261 test `Serialize_the_nullable_struct_array` 
                        if (b.Right is BlockExpression rightBlock
                        ) // it is valid to assign the block and it is used to my surprise
                        {
                            sb.Append("// { The block result will be assigned to `")
                                .Append(b.Left.ToCSharpString(new StringBuilder(), cPrinter, lineIdent, stripNamespace,
                                    printType, identSpaces))
                                .Append('`');
                            rightBlock.BlockToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType,
                                identSpaces, false, blockResultAssignment: b);
                            return sb.NewLineIdent(lineIdent).Append("// } end of block assignment");
                        }

                        b.Left.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                        if (e.NodeType == ExpressionType.PowerAssign) {
                            sb.Append(" = System.Math.Pow(");
                            b.Left.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces)
                                .Append(", ");
                            return b.Right
                                .ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces)
                                .Append(")");
                        }

                        sb.Append(OperatorToCSharpString(e.NodeType));

                        return b.Right.ToCSharpString(sb.Append('('), cPrinter, lineIdent, stripNamespace, printType,
                            identSpaces).Append(')');
                    }


                    sb.Append('(');
                    b.Left.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces).Append(")");

                    if (e.NodeType == ExpressionType.Equal) {
                        if (b.Right is ConstantExpression r && r.Value is bool rb && rb)
                            return sb;
                        sb.Append(" == ");
                    } else if (e.NodeType == ExpressionType.NotEqual) {
                        if (b.Right is ConstantExpression r && r.Value is bool rb)
                            return rb ? sb.Append(" == false") : sb;
                        sb.Append(" != ");
                    } else {
                        sb.Append(OperatorToCSharpString(e.NodeType));
                    }

                    return b.Right.ToCSharpString(sb.Append('('), cPrinter, lineIdent, stripNamespace, printType,
                            identSpaces)
                        .Append(")");
                }

                return sb.Append(e.ToString()); // falling back ToString and hoping for the best 
            }
        }
    }

    private static string GetCSharpName(this MemberInfo m) {
        var name = m.Name;
        if (m is FieldInfo fi && m.DeclaringType.IsValueType) {
            // btw, `fi.IsSpecialName` returns `false` :/
            if (name[0] == '<') // a backing field for the properties in struct, e.g. <Key>k__BackingField
            {
                var end = name.IndexOf('>');
                if (end > 1)
                    name = name.Substring(1, end - 1);
            }
        }
        return name;
    }

    private const string NotSupportedExpression = "// NOT_SUPPORTED_EXPRESSION: ";

    internal static StringBuilder ToCSharpString(this LabelTarget lt, StringBuilder sb) =>
        (lt.Name != null ? sb.Append(lt.Name) : sb.Append(lt.Type.ToCode(true, null)))
        .Append("__")
        .Append((long) lt.GetHashCode() +
                (long) int.MaxValue); // append the hash because often the label names in the block and sub-blocks are selected to be the same

    private static StringBuilder ToCSharpString(this IReadOnlyList<MemberBinding> bindings, StringBuilder sb,
        CodePrinter.IObjectToCode cPrinter, int lineIdent = 0, bool stripNamespace = false,
        Func<Type, string, string> printType = null, int identSpaces = 4) {
        foreach (var b in bindings) {
            sb.NewLineIdent(lineIdent);
            sb.Append(b.Member.Name).Append(" = ");

            if (b is MemberAssignment ma) {
                ma.Expression.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
            } else if (b is MemberMemberBinding mmb) {
                sb.Append("{");
                ToCSharpString(mmb.Bindings, sb, cPrinter, lineIdent + identSpaces, stripNamespace, printType,
                    identSpaces);
                sb.NewLineIdent(lineIdent + identSpaces).Append("}");
            } else if (b is MemberListBinding mlb) {
                sb.Append("{");
                foreach (var i in mlb.Initializers) {
                    sb.NewLineIdent(lineIdent + identSpaces);
                    if (i.Arguments.Count > 1)
                        sb.Append("(");

                    var n = 0;
                    foreach (var a in i.Arguments)
                        a.ToCSharpString((++n > 1 ? sb.Append(", ") : sb), cPrinter, lineIdent + identSpaces,
                            stripNamespace, printType, identSpaces);

                    if (i.Arguments.Count > 1)
                        sb.Append(")");

                    sb.Append(",");
                }
                sb.NewLineIdent(lineIdent + identSpaces).Append("}");
            }
            sb.Append(",");
        }
        return sb;
    }

    private static StringBuilder BlockToCSharpString(this BlockExpression b, StringBuilder sb,
        CodePrinter.IObjectToCode cPrinter,
        int lineIdent = 0, bool stripNamespace = false, Func<Type, string, string> printType = null,
        int identSpaces = 4,
        bool inTheLastBlock = false, BinaryExpression blockResultAssignment = null) {
        var vars = b.Variables;
        if (vars.Count != 0) {
            for (var i = 0; i < vars.Count; i++) {
                var v = vars[i];
                sb.NewLineIdent(lineIdent);
                sb.Append(v.Type.ToCode(stripNamespace, printType)).Append(' ');
                sb.AppendName(v.Name, v.Type, v).Append(';');
            }
        }

        var exprs = b.Expressions;

        // we don't inline as single expression case because it can always go crazy with assignment, e.g. `var a; a = 1 + (a = 2) + a * 2`

        for (var i = 0; i < exprs.Count - 1; i++) {
            var expr = exprs[i];

            // this is basically the return pattern (see #237) so we don't care for the rest of the expressions
            if (expr is GotoExpression gt && gt.Kind == GotoExpressionKind.Return &&
                exprs[i + 1] is LabelExpression label && label.Target == gt.Target) {
                sb.NewLineIdent(lineIdent);
                if (gt.Value == null)
                    return b.Type == typeof(void) ? sb : sb.Append("return;");

                sb.Append("return ");
                return gt.Value.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces)
                    .Append(";");
            }

            if (expr is BlockExpression bl) {
                // Unrolling the block on the same vertical line
                bl.BlockToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces,
                    inTheLastBlock: false);
            } else {
                sb.NewLineIdent(lineIdent);

                if (expr is LabelExpression) // keep the label on the same vertical line
                    expr.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
                else
                    expr.ToCSharpString(sb, cPrinter, lineIdent + identSpaces, stripNamespace, printType, identSpaces);

                // Preventing the `};` kind of situation and separating the conditional block with empty line
                if (expr is BlockExpression ||
                    expr is ConditionalExpression ||
                    expr is TryExpression ||
                    expr is LoopExpression ||
                    expr is SwitchExpression)
                    sb.NewLineIdent(lineIdent);
                else if (!(
                    expr is LabelExpression ||
                    expr is DefaultExpression))
                    sb.Append(';');
            }
        }

        var lastExpr = exprs[exprs.Count - 1];
        if (lastExpr.NodeType == ExpressionType.Default && lastExpr.Type == typeof(void))
            return sb;

        if (lastExpr is BlockExpression lastBlock)
            return lastBlock.BlockToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces,
                inTheLastBlock, // the last block is marked so if only it is itself in the last block
                blockResultAssignment);

        if (lastExpr is LabelExpression) // keep the last label on the same vertical line
        {
            lastExpr.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
            if (inTheLastBlock)
                sb.Append(';'); // the last label forms the invalid C#, so we need at least ';' at the end
            return sb;
        }

        sb.NewLineIdent(lineIdent);

        if (blockResultAssignment != null) {
            blockResultAssignment.Left.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
            if (blockResultAssignment.NodeType != ExpressionType.PowerAssign)
                sb.Append(OperatorToCSharpString(blockResultAssignment.NodeType));
            else {
                sb.Append(" = System.Math.Pow(");
                blockResultAssignment.Left
                    .ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces).Append(", ");
            }
        } else if (inTheLastBlock && b.Type != typeof(void))
            sb.Append("return ");

        if ((lastExpr is ConditionalExpression c && c.Type == typeof(void)) ||
            lastExpr is TryExpression ||
            lastExpr is LoopExpression ||
            lastExpr is SwitchExpression ||
            lastExpr is DefaultExpression d && d.Type == typeof(void)) {
            lastExpr.ToCSharpString(sb, cPrinter, lineIdent + identSpaces, stripNamespace, printType, identSpaces);
        } else if (lastExpr.NodeType == ExpressionType.Assign &&
                   ((BinaryExpression) lastExpr).Right is BlockExpression) {
            lastExpr.ToCSharpString(sb, cPrinter, lineIdent, stripNamespace, printType, identSpaces);
        } else {
            lastExpr.ToCSharpString(sb, cPrinter, lineIdent + identSpaces, stripNamespace, printType, identSpaces);

            if (blockResultAssignment?.NodeType == ExpressionType.PowerAssign)
                sb.Append(')');
            sb.Append(';');
        }
        return sb;
    }

    private static string OperatorToCSharpString(ExpressionType nodeType) =>
        nodeType switch {
            ExpressionType.And => " & ",
            ExpressionType.AndAssign => " &= ",
            ExpressionType.AndAlso => " && ",
            ExpressionType.Or => " | ",
            ExpressionType.OrAssign => " |= ",
            ExpressionType.OrElse => " || ",
            ExpressionType.GreaterThan => " > ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThan => " < ",
            ExpressionType.LessThanOrEqual => " <= ",
            ExpressionType.Equal => " == ",
            ExpressionType.NotEqual => " != ",
            ExpressionType.Add => " + ",
            ExpressionType.AddChecked => " + ",
            ExpressionType.AddAssign => " += ",
            ExpressionType.AddAssignChecked => " += ",
            ExpressionType.Subtract => " - ",
            ExpressionType.SubtractChecked => " - ",
            ExpressionType.SubtractAssign => " -= ",
            ExpressionType.SubtractAssignChecked => " -= ",
            ExpressionType.Assign => " = ",
            ExpressionType.ExclusiveOr => " ^ ",
            ExpressionType.ExclusiveOrAssign => " ^= ",
            ExpressionType.LeftShift => " << ",
            ExpressionType.LeftShiftAssign => " <<= ",
            ExpressionType.RightShift => " >> ",
            ExpressionType.RightShiftAssign => " >>= ",
            ExpressionType.Modulo => " % ",
            ExpressionType.ModuloAssign => " %= ",
            ExpressionType.Multiply => " * ",
            ExpressionType.MultiplyChecked => " * ",
            ExpressionType.MultiplyAssign => " *= ",
            ExpressionType.MultiplyAssignChecked => " *= ",
            ExpressionType.Divide => " / ",
            ExpressionType.DivideAssign => " /= ",
            _ => "???" // todo: @unclear wanna be good
        };
}

}