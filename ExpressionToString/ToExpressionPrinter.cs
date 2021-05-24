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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Runtime.CompilerServices;
using static System.Environment;
using PE = System.Linq.Expressions.ParameterExpression;

namespace FastExpressionCompiler {


// Helpers targeting the performance. Extensions method names may be a bit funny (non standard), 
// in order to prevent conflicts with YOUR helpers with standard names
internal static class Tools {
    internal static bool IsUnsigned(this Type type) =>
        type == typeof(byte) ||
        type == typeof(ushort) ||
        type == typeof(uint) ||
        type == typeof(ulong);

    internal static bool IsNullable(this Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);

    internal static MethodInfo FindMethod(this Type type, string methodName) {
        var methods = type.GetMethods();
        for (var i = 0; i < methods.Length; i++)
            if (methods[i].Name == methodName)
                return methods[i];
        return type.BaseType?.FindMethod(methodName);
    }

    internal static MethodInfo DelegateTargetGetterMethod =
        typeof(Delegate).FindPropertyGetMethod("Target");

    internal static MethodInfo FindDelegateInvokeMethod(this Type type) => type.GetMethod("Invoke");

    internal static MethodInfo FindNullableGetValueOrDefaultMethod(this Type type) {
        var methods = type.GetMethods();
        for (var i = 0; i < methods.Length; i++) {
            var m = methods[i];
            if (m.GetParameters().Length == 0 && m.Name == "GetValueOrDefault")
                return m;
        }

        return null;
    }

    internal static MethodInfo FindValueGetterMethod(this Type type) =>
        type.FindPropertyGetMethod("Value");

    internal static MethodInfo FindNullableHasValueGetterMethod(this Type type) =>
        type.FindPropertyGetMethod("HasValue");

    internal static MethodInfo FindPropertyGetMethod(this Type propHolderType, string propName) {
        var methods = propHolderType.GetMethods();
        for (var i = 0; i < methods.Length; i++) {
            var method = methods[i];
            if (method.IsSpecialName) {
                var methodName = method.Name;
                if (methodName.Length == propName.Length + 4 && methodName[0] == 'g' && methodName[3] == '_') {
                    var j = propName.Length - 1;
                    while (j != -1 && propName[j] == methodName[j + 4]) --j;
                    if (j == -1)
                        return method;
                }
            }
        }

        return propHolderType.BaseType?.FindPropertyGetMethod(propName);
    }

    internal static MethodInfo FindPropertySetMethod(this Type propHolderType, string propName) {
        var methods = propHolderType.GetMethods();
        for (var i = 0; i < methods.Length; i++) {
            var method = methods[i];
            if (method.IsSpecialName) {
                var methodName = method.Name;
                if (methodName.Length == propName.Length + 4 && methodName[0] == 's' && methodName[3] == '_') {
                    var j = propName.Length - 1;
                    while (j != -1 && propName[j] == methodName[j + 4]) --j;
                    if (j == -1)
                        return method;
                }
            }
        }

        return propHolderType.BaseType?.FindPropertySetMethod(propName);
    }

    internal static MethodInfo FindConvertOperator(this Type type, Type sourceType, Type targetType) {
        var methods = type.GetMethods();
        for (var i = 0; i < methods.Length; i++) {
            var m = methods[i];
            if (m.IsStatic && m.IsSpecialName && m.ReturnType == targetType) {
                var n = m.Name;
                // n == "op_Implicit" || n == "op_Explicit"
                if (n.Length == 11 &&
                    n[2] == '_' && n[5] == 'p' && n[6] == 'l' && n[7] == 'i' && n[8] == 'c' && n[9] == 'i' &&
                    n[10] == 't' &&
                    m.GetParameters()[0].ParameterType == sourceType)
                    return m;
            }
        }

        return null;
    }

    internal static ConstructorInfo FindSingleParamConstructor(this Type type, Type paramType) {
        var ctors = type.GetConstructors();
        for (var i = 0; i < ctors.Length; i++) {
            var ctor = ctors[i];
            var parameters = ctor.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == paramType)
                return ctor;
        }

        return null;
    }

    public static T[] AsArray<T>(this IEnumerable<T> xs) {
        if (xs is T[] array)
            return array;
        return xs == null ? null : xs.ToArray();
    }

    private static class EmptyArray<T> {
        public static readonly T[] Value = new T[0];
    }

    public static T[] Empty<T>() => EmptyArray<T>.Value;

    public static T[] WithLast<T>(this T[] source, T value) {
        if (source == null || source.Length == 0)
            return new[] {value};
        if (source.Length == 1)
            return new[] {source[0], value};
        if (source.Length == 2)
            return new[] {source[0], source[1], value};
        var sourceLength = source.Length;
        var result = new T[sourceLength + 1];
        Array.Copy(source, 0, result, 0, sourceLength);
        result[sourceLength] = value;
        return result;
    }

    public static Type[] GetParamTypes(IReadOnlyList<PE> paramExprs) {
        if (paramExprs == null)
            return Empty<Type>();

        var count = paramExprs.Count;
        if (count == 0)
            return Empty<Type>();

        if (count == 1)
            return new[] {paramExprs[0].IsByRef ? paramExprs[0].Type.MakeByRefType() : paramExprs[0].Type};

        var paramTypes = new Type[count];
        for (var i = 0; i < paramTypes.Length; i++) {
            var parameterExpr = paramExprs[i];
            paramTypes[i] = parameterExpr.IsByRef ? parameterExpr.Type.MakeByRefType() : parameterExpr.Type;
        }

        return paramTypes;
    }

    public static Type GetFuncOrActionType(Type returnType) =>
        returnType == typeof(void) ? typeof(Action) : typeof(Func<>).MakeGenericType(returnType);

    public static Type GetFuncOrActionType(Type p, Type returnType) =>
        returnType == typeof(void) ?
            typeof(Action<>).MakeGenericType(p) :
            typeof(Func<,>).MakeGenericType(p, returnType);

    public static Type GetFuncOrActionType(Type p0, Type p1, Type returnType) =>
        returnType == typeof(void) ?
            typeof(Action<,>).MakeGenericType(p0, p1) :
            typeof(Func<,,>).MakeGenericType(p0, p1, returnType);

    public static Type GetFuncOrActionType(Type p0, Type p1, Type p2, Type returnType) =>
        returnType == typeof(void) ?
            typeof(Action<,,>).MakeGenericType(p0, p1, p2) :
            typeof(Func<,,,>).MakeGenericType(p0, p1, p2, returnType);

    public static Type GetFuncOrActionType(Type p0, Type p1, Type p2, Type p3, Type returnType) =>
        returnType == typeof(void) ?
            typeof(Action<,,,>).MakeGenericType(p0, p1, p2, p3) :
            typeof(Func<,,,,>).MakeGenericType(p0, p1, p2, p3, returnType);

    public static Type GetFuncOrActionType(Type p0, Type p1, Type p2, Type p3, Type p4, Type returnType) =>
        returnType == typeof(void) ?
            typeof(Action<,,,,>).MakeGenericType(p0, p1, p2, p3, p4) :
            typeof(Func<,,,,,>).MakeGenericType(p0, p1, p2, p3, p4, returnType);

    public static Type GetFuncOrActionType(Type p0, Type p1, Type p2, Type p3, Type p4, Type p5, Type returnType) =>
        returnType == typeof(void) ?
            typeof(Action<,,,,,>).MakeGenericType(p0, p1, p2, p3, p4, p5) :
            typeof(Func<,,,,,,>).MakeGenericType(p0, p1, p2, p3, p4, p5, returnType);

    public static Type GetFuncOrActionType(Type[] paramTypes, Type returnType) {
        if (returnType == typeof(void)) {
            switch (paramTypes.Length) {
                case 0: return typeof(Action);
                case 1: return typeof(Action<>).MakeGenericType(paramTypes);
                case 2: return typeof(Action<,>).MakeGenericType(paramTypes);
                case 3: return typeof(Action<,,>).MakeGenericType(paramTypes);
                case 4: return typeof(Action<,,,>).MakeGenericType(paramTypes);
                case 5: return typeof(Action<,,,,>).MakeGenericType(paramTypes);
                case 6: return typeof(Action<,,,,,>).MakeGenericType(paramTypes);
                case 7: return typeof(Action<,,,,,,>).MakeGenericType(paramTypes);
                default:
                    throw new NotSupportedException(
                        $"Action with so many ({paramTypes.Length}) parameters is not supported!");
            }
        }

        switch (paramTypes.Length) {
            case 0: return typeof(Func<>).MakeGenericType(returnType);
            case 1: return typeof(Func<,>).MakeGenericType(paramTypes[0], returnType);
            case 2: return typeof(Func<,,>).MakeGenericType(paramTypes[0], paramTypes[1], returnType);
            case 3: return typeof(Func<,,,>).MakeGenericType(paramTypes[0], paramTypes[1], paramTypes[2], returnType);
            case 4:
                return typeof(Func<,,,,>).MakeGenericType(paramTypes[0], paramTypes[1], paramTypes[2], paramTypes[3],
                    returnType);
            case 5:
                return typeof(Func<,,,,,>).MakeGenericType(paramTypes[0], paramTypes[1], paramTypes[2], paramTypes[3],
                    paramTypes[4], returnType);
            case 6:
                return typeof(Func<,,,,,,>).MakeGenericType(paramTypes[0], paramTypes[1], paramTypes[2], paramTypes[3],
                    paramTypes[4], paramTypes[5], returnType);
            case 7:
                return typeof(Func<,,,,,,,>).MakeGenericType(paramTypes[0], paramTypes[1], paramTypes[2], paramTypes[3],
                    paramTypes[4], paramTypes[5], paramTypes[6], returnType);
            default:
                throw new NotSupportedException(
                    $"Func with so many ({paramTypes.Length}) parameters is not supported!");
        }
    }

    public static T GetFirst<T>(this IEnumerable<T> source) {
        // This is pretty much Linq.FirstOrDefault except it does not need to check
        // if source is IPartition<T> (but should it?)

        if (source is IList<T> list)
            return list.Count == 0 ? default : list[0];
        using (var items = source.GetEnumerator())
            return items.MoveNext() ? items.Current : default;
    }

    public static T GetFirst<T>(this T[] source) => source.Length == 0 ? default : source[0];
}


public static class ToExpressionPrinter {
    /// Helps to identify constants as the one to be put into the Closure
    public static bool IsClosureBoundConstant(object value, Type type) =>
        value is Delegate || type.IsArray ||
        !type.IsPrimitive && !type.IsEnum && value is string == false && value is Type == false &&
        value is decimal == false;


    /// <summary>
    /// Prints the expression in its constructing syntax - 
    /// helpful to get the expression from the debug session and put into it the code for the test.
    /// </summary>
    public static string ToExpressionString(this Expression expr) =>
        expr.ToExpressionString(out var _, out var _, out var _);

    /// <summary>
    /// Prints the expression in its constructing syntax - 
    /// helpful to get the expression from the debug session and put into it the code for the test.
    /// In addition, returns the gathered expressions, parameters ad labels. 
    /// </summary>
    public static string ToExpressionString(this Expression expr,
        out List<ParameterExpression> paramsExprs, out List<Expression> uniqueExprs, out List<LabelTarget> lts,
        bool stripNamespace = false, Func<Type, string, string> printType = null) {
        var sb = new StringBuilder(1024);
        sb.Append("var expr = ");
        paramsExprs = new List<ParameterExpression>();
        uniqueExprs = new List<Expression>();
        lts = new List<LabelTarget>();
        sb = expr.CreateExpressionString(sb, paramsExprs, uniqueExprs, lts, 2, stripNamespace, printType).Append(';');

        sb.Insert(0, $"var l = new LabelTarget[{lts.Count}]; // the labels {NewLine}");
        sb.Insert(0, $"var e = new Expression[{uniqueExprs.Count}]; // the unique expressions {NewLine}");
        sb.Insert(0, $"var p = new ParameterExpression[{paramsExprs.Count}]; // the parameter expressions {NewLine}");

        return sb.ToString();
    }

    // Searches first for the expression reference in the `uniqueExprs` and adds the reference to expression by index, 
    // otherwise delegates to `CreateExpressionCodeString`
    internal static StringBuilder ToExpressionString(this Expression expr, StringBuilder sb,
        List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent = 0, bool stripNamespace = false, Func<Type, string, string> printType = null,
        int identSpaces = 2) {
        if (expr is ParameterExpression p)
            return p.ToExpressionString(sb, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                identSpaces);

        var i = uniqueExprs.Count - 1;
        while (i != -1 && !ReferenceEquals(uniqueExprs[i], expr)) --i;
        if (i != -1)
            return sb.Append("e[").Append(i)
                // output expression type and kind to help to understand what is it
                .Append(" // ").Append(expr.NodeType.ToString()).Append(" of ")
                .Append(expr.Type.ToCode(stripNamespace, printType))
                .NewLineIdent(lineIdent).Append("]");

        uniqueExprs.Add(expr);
        sb.Append("e[").Append(uniqueExprs.Count - 1).Append("]=");
        return expr.CreateExpressionString(sb, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
            identSpaces);
    }

    internal static StringBuilder ToExpressionString(this ParameterExpression pe, StringBuilder sb,
        List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent = 0, bool stripNamespace = false, Func<Type, string, string> printType = null,
        int identSpaces = 2) {
        var i = paramsExprs.Count - 1;
        while (i != -1 && !ReferenceEquals(paramsExprs[i], pe)) --i;
        if (i != -1)
            return sb.Append("p[").Append(i)
                .Append(" // (")
                .Append(!pe.Type.IsPrimitive && pe.Type.IsValueType ? "[struct] " : string.Empty)
                .Append(pe.Type.ToCode(stripNamespace, printType))
                .Append(' ').AppendName(pe.Name, pe.Type, pe).Append(')')
                .NewLineIdent(lineIdent).Append(']');

        paramsExprs.Add(pe);
        sb.Append("p[").Append(paramsExprs.Count - 1).Append("]=");
        return pe.CreateExpressionString(sb, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
            identSpaces);
    }

    internal static StringBuilder ToExpressionString(this LabelTarget lt, StringBuilder sb,
        List<LabelTarget> labelTargets,
        int lineIdent = 0, bool stripNamespace = false, Func<Type, string, string> printType = null) {
        var i = labelTargets.Count - 1;
        while (i != -1 && !ReferenceEquals(labelTargets[i], lt)) --i;
        if (i != -1)
            return sb.Append("l[").Append(i)
                .Append(" // (").AppendName(lt.Name, lt.Type, lt).Append(')')
                .NewLineIdent(lineIdent).Append(']');

        labelTargets.Add(lt);
        sb.Append("l[").Append(labelTargets.Count - 1).Append("]=Label(");
        sb.AppendTypeof(lt.Type, stripNamespace, printType);

        return (lt.Name != null ? sb.Append(", \"").Append(lt.Name).Append("\"") : sb).Append(")");
    }

    private static StringBuilder ToExpressionString(this IReadOnlyList<CatchBlock> bs, StringBuilder sb,
        List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent, bool stripNamespace, Func<Type, string, string> printType, int identSpaces) {
        if (bs.Count == 0)
            return sb.Append("new CatchBlock[0]");
        for (var i = 0; i < bs.Count; i++)
            bs[i].ToExpressionString((i > 0 ? sb.Append(',') : sb).NewLineIdent(lineIdent),
                paramsExprs, uniqueExprs, lts, lineIdent + identSpaces, stripNamespace, printType, identSpaces);
        return sb;
    }

    private static StringBuilder ToExpressionString(this CatchBlock b, StringBuilder sb,
        List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent, bool stripNamespace, Func<Type, string, string> printType, int identSpaces) {
        sb.Append("MakeCatchBlock(");
        sb.NewLineIdent(lineIdent).AppendTypeof(b.Test, stripNamespace, printType).Append(',');
        sb.NewLineIdentExpr(b.Variable, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
            identSpaces).Append(',');
        sb.NewLineIdentExpr(b.Body, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType, identSpaces)
            .Append(',');
        sb.NewLineIdentExpr(b.Filter, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType, identSpaces);
        return sb.Append(')');
    }

    private static StringBuilder ToExpressionString(this IReadOnlyList<SwitchCase> items, StringBuilder sb,
        List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent, bool stripNamespace, Func<Type, string, string> printType, int identSpaces) {
        if (items.Count == 0)
            return sb.Append("new SwitchCase[0]");
        for (var i = 0; i < items.Count; i++)
            items[i].ToExpressionString((i > 0 ? sb.Append(',') : sb).NewLineIdent(lineIdent),
                paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType, identSpaces);
        return sb;
    }

    private static StringBuilder ToExpressionString(this SwitchCase s, StringBuilder sb,
        List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent, bool stripNamespace, Func<Type, string, string> printType, int identSpaces) {
        sb.Append("SwitchCase(");
        sb.NewLineIdentExpr(s.Body, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType, identSpaces)
            .Append(',');
        sb.NewLineIdentArgumentExprs(s.TestValues, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
            identSpaces);
        return sb.Append(')');
    }

    private static StringBuilder ToExpressionString(this MemberBinding mb, StringBuilder sb,
        List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent = 0, bool stripNamespace = false, Func<Type, string, string> printType = null,
        int identSpaces = 2) {
        if (mb is MemberAssignment ma) {
            sb.Append("Bind(");
            sb.NewLineIdent(lineIdent).AppendMember(mb.Member, stripNamespace, printType).Append(", ");
            sb.NewLineIdentExpr(ma.Expression, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                identSpaces);
            return sb.Append(")");
        }

        if (mb is MemberMemberBinding mmb) {
            sb.NewLineIdent(lineIdent).Append(NotSupportedExpression).Append(nameof(MemberMemberBinding))
                .NewLineIdent(lineIdent);
            sb.Append("MemberBind(");
            sb.NewLineIdent(lineIdent).AppendMember(mb.Member, stripNamespace, printType);

            for (int i = 0; i < mmb.Bindings.Count; i++)
                mmb.Bindings[i].ToExpressionString(sb.Append(", ").NewLineIdent(lineIdent),
                    paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType, identSpaces);
            return sb.Append(")");
        }

        if (mb is MemberListBinding mlb) {
            sb.NewLineIdent(lineIdent).Append(NotSupportedExpression).Append(nameof(MemberListBinding))
                .NewLineIdent(lineIdent);
            sb.Append("ListBind(");
            sb.NewLineIdent(lineIdent).AppendMember(mb.Member, stripNamespace, printType);

            for (int i = 0; i < mlb.Initializers.Count; i++)
                mlb.Initializers[i].ToExpressionString(sb.Append(", ").NewLineIdent(lineIdent),
                    paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType, identSpaces);

            return sb.Append(")");
        }

        return sb;
    }

    private static StringBuilder ToExpressionString(this ElementInit ei, StringBuilder sb,
        List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent = 0, bool stripNamespace = false, Func<Type, string, string> printType = null,
        int identSpaces = 2) {
        sb.Append("ElementInit(");
        sb.NewLineIdent(lineIdent).AppendMethod(ei.AddMethod, stripNamespace, printType).Append(", ");
        sb.NewLineIdentArgumentExprs(ei.Arguments, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
            identSpaces);
        return sb.Append(")");
    }

    private const string NotSupportedExpression = "// NOT_SUPPORTED_EXPRESSION: ";

    internal static StringBuilder CreateExpressionString(this Expression e, StringBuilder sb,
        List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent = 0, bool stripNamespace = false, Func<Type, string, string> printType = null,
        int identSpaces = 2) {
        switch (e.NodeType) {
            case ExpressionType.Constant: {
                var x = (ConstantExpression) e;
                sb.Append("Constant(");
                if (x.Value == null) {
                    sb.Append("null");
                    if (x.Type != typeof(object))
                        sb.Append(", ").AppendTypeof(x.Type, stripNamespace, printType);
                } else if (x.Value is Type t)
                    sb.AppendTypeof(t, stripNamespace, printType);
                else {
                    // For the closure bound constant let's output `null` or default value with the comment for user to provide the actual value
                    if (IsClosureBoundConstant(x.Value, x.Type)) {
                        if (x.Type.IsValueType)
                            sb.Append("default(").Append(x.Type.ToCode(stripNamespace, printType)).Append(')');
                        else // specifying the type for the Constant, otherwise we will lost it with the `Constant(default(MyClass))` which is equivalent to `Constant(null)`
                            sb.Append("null, ").AppendTypeof(x.Type, stripNamespace, printType);
                        sb.NewLineIdent(lineIdent).Append("// !!! Please provide the non-default value")
                            .NewLineIdent(lineIdent);
                    } else {
                        sb.Append(x.Value.ToCode(CodePrinter.DefaultConstantValueToCode, stripNamespace, printType));
                        if (x.Value.GetType() != x.Type)
                            sb.Append(", ").AppendTypeof(x.Type, stripNamespace, printType);
                    }
                }
                return sb.Append(')');
            }
            case ExpressionType.Parameter: {
                var x = (ParameterExpression) e;
                sb.Append("Parameter(").AppendTypeof(x.Type, stripNamespace, printType);
                if (x.IsByRef)
                    sb.Append(".MakeByRefType()");
                if (x.Name != null)
                    sb.Append(", \"").Append(x.Name).Append('"');
                return sb.Append(')');
            }
            case ExpressionType.New: {
                var x = (NewExpression) e;
                var args = x.Arguments;

                if (args.Count == 0 && e.Type.IsValueType)
                    return sb.Append("New(").AppendTypeof(e.Type, stripNamespace, printType).Append(')');

                sb.Append("New( // ").Append(args.Count).Append(" args");
                var ctorIndex = x.Constructor.DeclaringType.GetTypeInfo().DeclaredConstructors.ToArray()
                    .GetFirstIndex(x.Constructor);
                sb.NewLineIdent(lineIdent).AppendTypeof(x.Type, stripNamespace, printType)
                    .Append(".GetTypeInfo().DeclaredConstructors.ToArray()[").Append(ctorIndex).Append("],");
                sb.NewLineIdentArgumentExprs(args, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces);
                return sb.Append(')');
            }
            case ExpressionType.Call: {
                var x = (MethodCallExpression) e;
                sb.Append("Call(");
                sb.NewLineIdentExpr(x.Object, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(", ");
                sb.NewLineIdent(lineIdent).AppendMethod(x.Method, stripNamespace, printType);
                if (x.Arguments.Count > 0)
                    sb.Append(',').NewLineIdentArgumentExprs(x.Arguments, paramsExprs, uniqueExprs, lts, lineIdent,
                        stripNamespace, printType, identSpaces);
                return sb.Append(')');
            }
            case ExpressionType.MemberAccess: {
                var x = (MemberExpression) e;
                if (x.Member is PropertyInfo p) {
                    sb.Append("Property(");
                    sb.NewLineIdentExpr(x.Expression, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                        printType, identSpaces).Append(',');
                    sb.NewLineIdent(lineIdent).AppendProperty(p, stripNamespace, printType);
                } else {
                    sb.Append("Field(");
                    sb.NewLineIdentExpr(x.Expression, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                        printType, identSpaces).Append(',');
                    sb.NewLineIdent(lineIdent).AppendField((FieldInfo) x.Member, stripNamespace, printType);
                }
                return sb.Append(')');
            }

            case ExpressionType.NewArrayBounds:
            case ExpressionType.NewArrayInit: {
                var x = (NewArrayExpression) e;
                if (e.NodeType == ExpressionType.NewArrayInit) {
                    // todo: @feature multi-dimensional array initializers are not supported yet, they also are not supported by the hoisted expression
                    if (e.Type.GetArrayRank() > 1)
                        sb.NewLineIdent(lineIdent).Append(NotSupportedExpression).Append(e.NodeType)
                            .NewLineIdent(lineIdent);
                    sb.Append("NewArrayInit(");
                } else {
                    sb.Append("NewArrayBounds(");
                }
                sb.NewLineIdent(lineIdent).AppendTypeof(x.Type.GetElementType(), stripNamespace, printType)
                    .Append(", ");
                sb.NewLineIdentArgumentExprs(x.Expressions, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                    printType, identSpaces);
                return sb.Append(')');
            }
            case ExpressionType.MemberInit: {
                var x = (MemberInitExpression) e;
                sb.Append("MemberInit((NewExpression)(");
                sb.NewLineIdentExpr(x.NewExpression, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                        printType, identSpaces)
                    .Append(')');
                for (var i = 0; i < x.Bindings.Count; i++)
                    x.Bindings[i].ToExpressionString(sb.Append(", ").NewLineIdent(lineIdent),
                        paramsExprs, uniqueExprs, lts, lineIdent + identSpaces, stripNamespace, printType, identSpaces);
                return sb.Append(')');
            }
            case ExpressionType.Lambda: {
                var x = (LambdaExpression) e;
                sb.Append("Lambda( // $"); // bookmark for the lambdas - $ means the cost of the lambda, specifically nested lambda
                sb.NewLineIdent(lineIdent).AppendTypeof(x.Type, stripNamespace, printType).Append(',');
                sb.NewLineIdentExpr(x.Body, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                sb.NewLineIdentArgumentExprs(x.Parameters, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                    printType, identSpaces);
                return sb.Append(')');
            }
            case ExpressionType.Invoke: {
                var x = (InvocationExpression) e;
                sb.Append("Invoke(");
                sb.NewLineIdentExpr(x.Expression, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                sb.NewLineIdentArgumentExprs(x.Arguments, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                    printType, identSpaces);
                return sb.Append(")");
            }
            case ExpressionType.Conditional: {
                var x = (ConditionalExpression) e;
                sb.Append("Condition(");
                sb.NewLineIdentExpr(x.Test, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                sb.NewLineIdentExpr(x.IfTrue, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                sb.NewLineIdentExpr(x.IfFalse, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                sb.NewLineIdent(lineIdent).AppendTypeof(x.Type, stripNamespace, printType);
                return sb.Append(')');
            }
            case ExpressionType.Block: {
                var x = (BlockExpression) e;
                sb.Append("Block(");
                sb.NewLineIdent(lineIdent).AppendTypeof(x.Type, stripNamespace, printType).Append(',');

                if (x.Variables.Count == 0)
                    sb.NewLineIdent(lineIdent).Append("new ParameterExpression[0], ");
                else {
                    sb.NewLineIdent(lineIdent).Append("new[] {");
                    for (var i = 0; i < x.Variables.Count; i++)
                        x.Variables[i].ToExpressionString((i > 0 ? sb.Append(',') : sb).NewLineIdent(lineIdent),
                            paramsExprs, uniqueExprs, lts, lineIdent + identSpaces, stripNamespace, printType,
                            identSpaces);
                    sb.NewLineIdent(lineIdent).Append("},");
                }

                sb.NewLineIdentArgumentExprs(x.Expressions, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                    printType, identSpaces);
                return sb.Append(')');
            }
            case ExpressionType.Loop: {
                var x = (LoopExpression) e;
                sb.Append("Loop(");
                sb.NewLineIdentExpr(x.Body, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces);

                if (x.BreakLabel != null)
                    x.BreakLabel.ToExpressionString(sb.Append(',').NewLineIdent(lineIdent), lts, lineIdent,
                        stripNamespace, printType);

                if (x.ContinueLabel != null)
                    x.ContinueLabel.ToExpressionString(sb.Append(',').NewLineIdent(lineIdent), lts, lineIdent,
                        stripNamespace, printType);

                return sb.Append(')');
            }
            case ExpressionType.Index: {
                var x = (IndexExpression) e;
                sb.Append(x.Indexer != null ? "MakeIndex(" : "ArrayAccess(");
                sb.NewLineIdentExpr(x.Object, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(", ");

                if (x.Indexer != null)
                    sb.NewLineIdent(lineIdent).AppendProperty(x.Indexer, stripNamespace, printType).Append(", ");

                sb.Append("new Expression[] {");
                for (var i = 0; i < x.Arguments.Count; i++)
                    (i > 0 ? sb.Append(',') : sb)
                        .NewLineIdentExpr(x.Arguments[i], paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                            printType, identSpaces);
                return sb.Append("})");
            }
            case ExpressionType.Try: {
                var x = (TryExpression) e;
                if (x.Finally == null) {
                    sb.Append("TryCatch(");
                    sb.NewLineIdentExpr(x.Body, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                        identSpaces).Append(',');
                    x.Handlers.ToExpressionString(sb, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                        printType, identSpaces);
                } else if (x.Handlers == null) {
                    sb.Append("TryFinally(");
                    sb.NewLineIdentExpr(x.Body, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                        identSpaces).Append(',');
                    sb.NewLineIdentExpr(x.Finally, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                        identSpaces);
                } else {
                    sb.Append("TryCatchFinally(");
                    sb.NewLineIdentExpr(x.Body, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                        identSpaces).Append(',');
                    x.Handlers.ToExpressionString(sb, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                        printType, identSpaces).Append(',');
                    sb.NewLineIdentExpr(x.Finally, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                        identSpaces);
                }

                return sb.Append(')');
            }
            case ExpressionType.Label: {
                var x = (LabelExpression) e;
                sb.Append("Label(");
                x.Target.ToExpressionString(sb, lts, lineIdent, stripNamespace, printType);
                if (x.DefaultValue != null)
                    sb.Append(',').NewLineIdentExpr(x.DefaultValue, paramsExprs, uniqueExprs, lts, lineIdent,
                        stripNamespace, printType, identSpaces);
                return sb.Append(')');
            }
            case ExpressionType.Goto: {
                var x = (GotoExpression) e;
                sb.Append("MakeGoto(").AppendEnum(x.Kind, stripNamespace, printType).Append(',');

                sb.NewLineIdent(lineIdent);
                x.Target.ToExpressionString(sb, lts, lineIdent, stripNamespace, printType).Append(',');

                sb.NewLineIdentExpr(x.Value, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                sb.NewLineIdent(lineIdent).AppendTypeof(x.Type, stripNamespace, printType);
                return sb.Append(')');
            }
            case ExpressionType.Switch: {
                var x = (SwitchExpression) e;
                sb.Append("Switch(");
                sb.NewLineIdentExpr(x.SwitchValue, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                sb.NewLineIdentExpr(x.DefaultBody, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                sb.NewLineIdent(lineIdent).AppendMethod(x.Comparison, stripNamespace, printType);
                ToExpressionString(x.Cases, sb, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces);
                return sb.Append(')');
            }
            case ExpressionType.Default: {
                return e.Type == typeof(void) ?
                    sb.Append("Empty()") :
                    sb.Append("Default(").AppendTypeof(e.Type, stripNamespace, printType).Append(')');
            }
            case ExpressionType.TypeIs:
            case ExpressionType.TypeEqual: {
                var x = (TypeBinaryExpression) e;
                sb.Append(e.NodeType == ExpressionType.TypeIs ? "TypeIs(" : "TypeEqual(");
                sb.NewLineIdentExpr(x.Expression, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                sb.NewLineIdent(lineIdent).AppendTypeof(x.TypeOperand, stripNamespace, printType);
                return sb.Append(')');
            }
            case ExpressionType.Coalesce: {
                var x = (BinaryExpression) e;
                sb.Append("Coalesce(");
                sb.NewLineIdentExpr(x.Left, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                sb.NewLineIdentExpr(x.Right, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                    identSpaces).Append(',');
                if (x.Conversion != null)
                    sb.NewLineIdentExpr(x.Conversion, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                        printType, identSpaces);
                return sb.Append(')');
            }
            case ExpressionType.ListInit: {
                var x = (ListInitExpression) e;
                sb.Append("ListInit((NewExpression)(");
                sb.NewLineIdentExpr(x.NewExpression, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                        printType, identSpaces)
                    .Append(')');
                for (var i = 0; i < x.Initializers.Count; i++)
                    x.Initializers[i].ToExpressionString(sb.Append(", ").NewLineIdent(lineIdent),
                        paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType, identSpaces);
                return sb.Append(")");
            }
            case ExpressionType.Extension: {
                var reduced = e.Reduce(); // proceed with the reduced expression
                return reduced.CreateExpressionString(sb, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace,
                    printType, identSpaces);
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
                    sb.Append(name).Append('(');
                    sb.NewLineIdentExpr(u.Operand, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                        identSpaces);

                    if (e.NodeType == ExpressionType.Convert ||
                        e.NodeType == ExpressionType.ConvertChecked ||
                        e.NodeType == ExpressionType.Unbox ||
                        e.NodeType == ExpressionType.Throw ||
                        e.NodeType == ExpressionType.TypeAs)
                        sb.Append(',').NewLineIdent(lineIdent).AppendTypeof(e.Type, stripNamespace, printType);

                    if ((e.NodeType == ExpressionType.Convert || e.NodeType == ExpressionType.ConvertChecked)
                        && u.Method != null)
                        sb.Append(',').NewLineIdent(lineIdent).AppendMethod(u.Method, stripNamespace, printType);
                }

                if (e is BinaryExpression b) {
                    sb.Append("MakeBinary(").Append(typeof(ExpressionType).Name).Append('.').Append(name).Append(',');
                    sb.NewLineIdentExpr(b.Left, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                        identSpaces).Append(',');
                    sb.NewLineIdentExpr(b.Right, paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType,
                        identSpaces);
                }
                return sb.Append(')');
            }
        }
    }
}


public static class CodePrinter {
    public static StringBuilder AppendTypeof(this StringBuilder sb, Type type,
        bool stripNamespace = false, Func<Type, string, string> printType = null, bool printGenericTypeArgs = false) =>
        type == null ?
            sb.Append("null") :
            sb.Append("typeof(").Append(type.ToCode(stripNamespace, printType, printGenericTypeArgs)).Append(')');

    public static StringBuilder AppendTypeofList(this StringBuilder sb, Type[] types,
        bool stripNamespace = false, Func<Type, string, string> printType = null, bool printGenericTypeArgs = false) {
        for (var i = 0; i < types.Length; i++)
            (i > 0 ? sb.Append(", ") : sb).AppendTypeof(types[i], stripNamespace, printType, printGenericTypeArgs);
        return sb;
    }

    internal static StringBuilder AppendMember(this StringBuilder sb, MemberInfo member,
        bool stripNamespace = false, Func<Type, string, string> printType = null) =>
        member is FieldInfo f ?
            sb.AppendField(f, stripNamespace, printType) :
            sb.AppendProperty((PropertyInfo) member, stripNamespace, printType);

    internal static StringBuilder AppendField(this StringBuilder sb, FieldInfo field,
        bool stripNamespace = false, Func<Type, string, string> printType = null) =>
        sb.AppendTypeof(field.DeclaringType, stripNamespace, printType)
            .Append(".GetTypeInfo().GetDeclaredField(\"").Append(field.Name).Append("\")");

    internal static StringBuilder AppendProperty(this StringBuilder sb, PropertyInfo property,
        bool stripNamespace = false, Func<Type, string, string> printType = null) =>
        sb.AppendTypeof(property.DeclaringType, stripNamespace, printType)
            .Append(".GetTypeInfo().GetDeclaredProperty(\"").Append(property.Name).Append("\")");

    internal static StringBuilder AppendEnum<TEnum>(this StringBuilder sb, TEnum value,
        bool stripNamespace = false, Func<Type, string, string> printType = null) =>
        sb.Append(typeof(TEnum).ToCode(stripNamespace, printType)).Append('.')
            .Append(Enum.GetName(typeof(TEnum), value));

    private const string _nonPubStatMethods = "BindingFlags.NonPublic|BindingFlags.Static";
    private const string _nonPubInstMethods = "BindingFlags.NonPublic|BindingFlags.Instance";

    public static StringBuilder AppendMethod(this StringBuilder sb, MethodInfo method,
        bool stripNamespace = false, Func<Type, string, string> printType = null) {
        if (method == null)
            return sb.Append("null");

        sb.AppendTypeof(method.DeclaringType, stripNamespace, printType);
        sb.Append(".GetMethods(");

        if (!method.IsPublic)
            sb.Append(method.IsStatic ? _nonPubStatMethods : _nonPubInstMethods);

        var mp = method.GetParameters();
        if (!method.IsGenericMethod) {
            sb.Append(").Single(x => !x.IsGenericMethod && x.Name == \"").Append(method.Name).Append("\" && ");
            return mp.Length == 0 ?
                sb.Append("x.GetParameters().Length == 0)") :
                sb.Append("x.GetParameters().Select(y => y.ParameterType).SequenceEqual(new[] { ")
                    .AppendTypeofList(mp.Select(x => x.ParameterType).ToArray(), stripNamespace, printType)
                    .Append(" }))");
        }

        var tp = method.GetGenericArguments();
        sb.Append(").Where(x => x.IsGenericMethod && x.Name == \"").Append(method.Name).Append("\" && ");
        if (mp.Length == 0) {
            sb.Append("x.GetParameters().Length == 0 && x.GetGenericArguments().Length == ").Append(tp.Length);
            sb.Append(").Select(x => x.IsGenericMethodDefinition ? x.MakeGenericMethod(")
                .AppendTypeofList(tp, stripNamespace, printType);
            return sb.Append(") : x).Single()");
        }

        sb.Append("x.GetGenericArguments().Length == ").Append(tp.Length);
        sb.Append(").Select(x => x.IsGenericMethodDefinition ? x.MakeGenericMethod(")
            .AppendTypeofList(tp, stripNamespace, printType);
        sb.Append(") : x).Single(x => x.GetParameters().Select(y => y.ParameterType).SequenceEqual(new[] { ");
        sb.AppendTypeofList(mp.Select(x => x.ParameterType).ToArray(), stripNamespace, printType);
        return sb.Append(" }))");
    }

    private static string GetParameterOrVariableNameFromTheType(Type t, string s) {
        var dotIndex = s.LastIndexOf('.');
        if (dotIndex != -1)
            s = s.Substring(dotIndex + 1);
        return (t.IsArray ? s.Replace("[]", "_arr") :
            t.IsGenericType ? s.Replace('<', '_').Replace('>', '_') :
            s).ToLowerInvariant();
    }

    internal static StringBuilder AppendName<T>(this StringBuilder sb, string name, Type type, T identity) =>
        name != null ?
            sb.Append(name) :
            sb.Append(type.ToCode(true, (t, s) => GetParameterOrVariableNameFromTheType(t, s))).Append("__")
                .Append((long) identity.GetHashCode() + (long) int.MaxValue);

    /// <summary>Converts the <paramref name="type"/> into the proper C# representation.</summary>
    public static string ToCode(this Type type,
        bool stripNamespace = false, Func<Type, string, string> printType = null, bool printGenericTypeArgs = false) {
        if (type.IsGenericParameter)
            return !printGenericTypeArgs ? string.Empty : (printType?.Invoke(type, type.Name) ?? type.Name);

        Type arrayType = null;
        if (type.IsArray) {
            // store the original type for the later and process its element type further here
            arrayType = type;
            type = type.GetElementType();
        }

        // the default handling of the built-in types
        string buildInTypeString = null;
        if (type == typeof(void))
            buildInTypeString = "void";
        if (type == typeof(object))
            buildInTypeString = "object";
        if (type == typeof(bool))
            buildInTypeString = "bool";
        if (type == typeof(int))
            buildInTypeString = "int";
        if (type == typeof(short))
            buildInTypeString = "short";
        if (type == typeof(byte))
            buildInTypeString = "byte";
        if (type == typeof(double))
            buildInTypeString = "double";
        if (type == typeof(float))
            buildInTypeString = "float";
        if (type == typeof(char))
            buildInTypeString = "char";
        if (type == typeof(string))
            buildInTypeString = "string";

        if (buildInTypeString != null) {
            if (arrayType != null)
                buildInTypeString += "[]";
            return printType?.Invoke(arrayType ?? type, buildInTypeString) ?? buildInTypeString;
        }

        var parentCount = 0;
        for (var ti = type.GetTypeInfo(); ti.IsNested; ti = ti.DeclaringType.GetTypeInfo())
            ++parentCount;

        Type[] parentTypes = null;
        if (parentCount > 0) {
            parentTypes = new Type[parentCount];
            var pt = type.DeclaringType;
            for (var i = 0; i < parentTypes.Length; i++, pt = pt.DeclaringType)
                parentTypes[i] = pt;
        }

        var typeInfo = type.GetTypeInfo();
        Type[] typeArgs = null;
        var isTypeClosedGeneric = false;
        if (type.IsGenericType) {
            isTypeClosedGeneric = !typeInfo.IsGenericTypeDefinition;
            typeArgs = isTypeClosedGeneric ? typeInfo.GenericTypeArguments : typeInfo.GenericTypeParameters;
        }

        var typeArgsConsumedByParentsCount = 0;
        var s = new StringBuilder();
        if (!stripNamespace && !string.IsNullOrEmpty(type.Namespace)
        ) // for the auto-generated classes Namespace may be empty and in general it may be empty
            s.Append(type.Namespace).Append('.');

        if (parentTypes != null) {
            for (var p = parentTypes.Length - 1; p >= 0; --p) {
                var parentType = parentTypes[p];
                if (!parentType.IsGenericType) {
                    s.Append(parentType.Name).Append('.');
                } else {
                    var parentTypeInfo = parentType.GetTypeInfo();
                    Type[] parentTypeArgs = null;
                    if (parentTypeInfo.IsGenericTypeDefinition) {
                        parentTypeArgs = parentTypeInfo.GenericTypeParameters;

                        // replace the open parent args with the closed child args,
                        // and close the parent
                        if (isTypeClosedGeneric)
                            for (var t = 0; t < parentTypeArgs.Length; ++t)
                                parentTypeArgs[t] = typeArgs[t];

                        var parentTypeArgCount = parentTypeArgs.Length;
                        if (typeArgsConsumedByParentsCount > 0) {
                            int ownArgCount = parentTypeArgCount - typeArgsConsumedByParentsCount;
                            if (ownArgCount == 0)
                                parentTypeArgs = null;
                            else {
                                var ownArgs = new Type[ownArgCount];
                                for (var a = 0; a < ownArgs.Length; ++a)
                                    ownArgs[a] = parentTypeArgs[a + typeArgsConsumedByParentsCount];
                                parentTypeArgs = ownArgs;
                            }
                        }
                        typeArgsConsumedByParentsCount = parentTypeArgCount;
                    } else {
                        parentTypeArgs = parentTypeInfo.GenericTypeArguments;
                    }

                    var parentTickIndex = parentType.Name.IndexOf('`');
                    s.Append(parentType.Name.Substring(0, parentTickIndex));

                    // The owned parentTypeArgs maybe empty because all args are defined in the parent's parents
                    if (parentTypeArgs?.Length > 0) {
                        s.Append('<');
                        for (var t = 0; t < parentTypeArgs.Length; ++t)
                            (t == 0 ? s : s.Append(", "))
                                .Append(parentTypeArgs[t].ToCode(stripNamespace, printType, printGenericTypeArgs));
                        s.Append('>');
                    }
                    s.Append('.');
                }
            }
        }

        var name = type.Name;
        if (name.StartsWith("<>"))
            name = name.Substring(2); // strip the "<>" from the `AnonymousType`

        if (typeArgs != null && typeArgsConsumedByParentsCount < typeArgs.Length) {
            var tickIndex = name.IndexOf('`');
            s.Append(name.Substring(0, tickIndex)).Append('<');
            for (var i = 0; i < typeArgs.Length - typeArgsConsumedByParentsCount; ++i)
                (i == 0 ? s : s.Append(", "))
                    .Append(typeArgs[i + typeArgsConsumedByParentsCount]
                        .ToCode(stripNamespace, printType, printGenericTypeArgs));
            s.Append('>');
        } else {
            s.Append(name);
        }

        if (arrayType != null)
            s.Append("[]");

        return printType?.Invoke(arrayType ?? type, s.ToString()) ?? s.ToString();
    }

    /// <summary>Prints valid C# Boolean</summary>
    public static string ToCode(this bool x) => x ? "true" : "false";

    /// <summary>Prints valid C# String escaping the things</summary>
    public static string ToCode(this string x) =>
        x == null ? "null" : $"\"{x.Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n")}\"";

    /// <summary>Prints valid C# Enum literal</summary>
    public static string ToEnumValueCode(this Type enumType, object x,
        bool stripNamespace = false, Func<Type, string, string> printType = null) =>
        $"{enumType.ToCode(stripNamespace, printType)}.{Enum.GetName(enumType, x)}";

    private static Type[] GetGenericTypeParametersOrArguments(this TypeInfo typeInfo) =>
        typeInfo.IsGenericTypeDefinition ? typeInfo.GenericTypeParameters : typeInfo.GenericTypeArguments;

    public interface IObjectToCode {
        string ToCode(object x, bool stripNamespace = false, Func<Type, string, string> printType = null);
    }

    private class ConstantValueToCode : CodePrinter.IObjectToCode {
        public string ToCode(object x, bool stripNamespace = false, Func<Type, string, string> printType = null) =>
            "default(" + x.GetType().ToCode(stripNamespace, printType) + ")";
    }

    internal static readonly CodePrinter.IObjectToCode DefaultConstantValueToCode = new ConstantValueToCode();

    /// <summary>Prints many code items as the array initializer.</summary>
    public static string ToCommaSeparatedCode(this IEnumerable items, IObjectToCode notRecognizedToCode,
        bool stripNamespace = false, Func<Type, string, string> printType = null) {
        var s = new StringBuilder();
        var first = true;
        foreach (var item in items) {
            if (first)
                first = false;
            else
                s.Append(", ");
            s.Append(item.ToCode(notRecognizedToCode, stripNamespace, printType));
        }
        return s.ToString();
    }

    /// <summary>Prints many code items as array initializer.</summary>
    public static string ToArrayInitializerCode(this IEnumerable items, Type itemType,
        IObjectToCode notRecognizedToCode,
        bool stripNamespace = false, Func<Type, string, string> printType = null) =>
        $"new {itemType.ToCode(stripNamespace, printType)}[]{{{items.ToCommaSeparatedCode(notRecognizedToCode, stripNamespace, printType)}}}";

    private static readonly Type[] TypesImplementedByArray =
        typeof(object[]).GetInterfaces().Where(t => t.GetTypeInfo().IsGenericType)
            .Select(t => t.GetGenericTypeDefinition()).ToArray();

    /// <summary>
    /// Prints a valid C# for known <paramref name="x"/>,
    /// otherwise uses passed <paramref name="notRecognizedToCode"/> or falls back to `ToString()`.
    /// </summary>
    public static string ToCode(this object x, IObjectToCode notRecognizedToCode,
        bool stripNamespace = false, Func<Type, string, string> printType = null) {
        if (x == null)
            return "null";

        if (x is bool b)
            return b.ToCode();

        if (x is string s)
            return s.ToCode();

        if (x is char c)
            return "'" + c + "'";

        if (x is Type t)
            return t.ToCode(stripNamespace, printType);

        var xType = x.GetType();
        var xTypeInfo = xType.GetTypeInfo();

        // check if item is implemented by array and then use the array initializer only for these types, 
        // otherwise we may produce the array initializer but it will be incompatible with e.g. `List<T>`
        if (xTypeInfo.IsArray ||
            xTypeInfo.IsGenericType && TypesImplementedByArray.Contains(xType.GetGenericTypeDefinition())) {
            var elemType = xTypeInfo.IsArray ?
                xTypeInfo.GetElementType() :
                xTypeInfo.GetGenericTypeParametersOrArguments().GetFirst();
            if (elemType != null)
                return ((IEnumerable) x).ToArrayInitializerCode(elemType, notRecognizedToCode);
        }

        // unwrap the Nullable struct
        if (xTypeInfo.IsGenericType && xTypeInfo.GetGenericTypeDefinition() == typeof(Nullable<>)) {
            xType = xTypeInfo.GetElementType();
            xTypeInfo = xType.GetTypeInfo();
        }

        if (xTypeInfo.IsEnum)
            return x.GetType().ToEnumValueCode(x, stripNamespace, printType);

        if (x is float f)
            return f.ToString(CultureInfo.InvariantCulture) + "f";

        if (x is double d)
            return d.ToString(CultureInfo.InvariantCulture);

        if (x is int i)
            return i.ToString(CultureInfo.InvariantCulture);

        if (xTypeInfo.IsPrimitive) // output the primitive casted to the type
            return "(" + x.GetType().ToCode(true, null) + ")" + x.ToString();

        return notRecognizedToCode?.ToCode(x, stripNamespace, printType) ?? x.ToString();
    }

    internal static StringBuilder NewLineIdent(this StringBuilder sb, int lineIdent) =>
        sb.AppendLine().Append(' ', lineIdent);

    internal static StringBuilder NewLine(this StringBuilder sb, int lineIdent, int identSpaces) =>
        sb.AppendLine().Append(' ', Math.Max(lineIdent - identSpaces, 0));

    internal static StringBuilder NewLineIdentExpr(this StringBuilder sb,
        Expression expr, List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent, bool stripNamespace = false, Func<Type, string, string> printType = null, int identSpaces = 2) {
        sb.NewLineIdent(lineIdent);
        return expr?.ToExpressionString(sb, paramsExprs, uniqueExprs, lts,
                   lineIdent + identSpaces, stripNamespace, printType, identSpaces)
               ?? sb.Append("null");
    }

    internal static StringBuilder NewLineIdentArgumentExprs<T>(this StringBuilder sb, IReadOnlyList<T> exprs,
        List<ParameterExpression> paramsExprs, List<Expression> uniqueExprs, List<LabelTarget> lts,
        int lineIdent, bool stripNamespace = false, Func<Type, string, string> printType = null, int identSpaces = 2)
        where T : Expression {
        if (exprs.Count == 0)
            return sb.Append(" new ").Append(typeof(T).ToCode(true)).Append("[0]");
        for (var i = 0; i < exprs.Count; i++)
            (i > 0 ? sb.Append(", ") : sb).NewLineIdentExpr(exprs[i],
                paramsExprs, uniqueExprs, lts, lineIdent, stripNamespace, printType, identSpaces);
        return sb;
    }

    internal static StringBuilder NewLineIdentCs(this StringBuilder sb, IObjectToCode cPrinter, Expression expr,
        int lineIdent, bool stripNamespace = false, Func<Type, string, string> printType = null, int identSpaces = 4) {
        sb.NewLineIdent(lineIdent);
        return expr?.ToCSharpString(sb, cPrinter, lineIdent + identSpaces, stripNamespace, printType, identSpaces)
               ?? sb.Append("null");
    }
}

internal static class FecHelpers {
    public static int GetFirstIndex<T>(this IReadOnlyList<T> source, T item) {
        if (source.Count != 0)
            for (var i = 0; i < source.Count; ++i)
                if (ReferenceEquals(source[i], item))
                    return i;
        return -1;
    }

    [MethodImpl((MethodImplOptions) 256)]
    public static T GetArgument<T>(this IReadOnlyList<T> source, int index) => source[index];

    [MethodImpl((MethodImplOptions) 256)]
    public static ParameterExpression GetParameter(this IReadOnlyList<PE> source, int index) => source[index];

#if LIGHT_EXPRESSION
        public static IReadOnlyList<PE> ToReadOnlyList(this IParameterProvider source) 
        {
            var count = source.ParameterCount;
            var ps = new ParameterExpression[count];
            for (var i = 0; i < count; ++i)
                ps[i] = source.GetParameter(i);
            return ps;
        }
#else
    public static IReadOnlyList<PE> ToReadOnlyList(this IReadOnlyList<PE> source) => source;
#endif
}
}
//#endif