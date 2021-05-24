# About

This is derived from https://github.com/dadhi/FastExpressionCompiler . It only contains the expression printing code.

The `ToCSharpString` interface is, with the changes in this repository, sufficiently advanced to be exported as source code and run. In fact, that is exactly how I use it in my bullet hell engine project Danmokou: https://github.com/Bagoum/danmokou . This said, there are a few caveats:

- Since C# source code can't handle inline block expressions, they need to be "linearized" using a process similar to the one here: https://github.com/Bagoum/danmokou/blob/master/Assets/Danmokou/Plugins/Self/Danmaku/Expressions/LinearizeVisitor.cs
- Nontrivial runtime instance variables can't be correctly printed. This is a pretty hard limitation, since if the process is closed all the runtime instance variables are gone and can't be referenced again, so an exported string can't refer to anything persistent. (Static variables are OK, as are data variables that have trivial representations such as `new Vector2(3, 4)` (Vector2) or `"hello world"` (string)).



Licensed under MIT. See COPYING for details.