using NVS.Core.Enums;

namespace NVS.Services.Editor;

public static class SyntaxHighlighting
{
    public static readonly Dictionary<Language, string[]> Keywords = new()
    {
        [Language.CSharp] = [
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while", "async", "await", "record", "init", "required", "partial"
        ],
        [Language.TypeScript] = [
            "abstract", "any", "as", "boolean", "break", "case", "catch", "class", "const", "constructor",
            "continue", "debugger", "declare", "default", "delete", "do", "else", "enum", "export", "extends",
            "false", "finally", "for", "from", "function", "get", "if", "implements", "import", "in",
            "instanceof", "interface", "let", "module", "namespace", "new", "null", "number", "object",
            "package", "private", "protected", "public", "readonly", "require", "return", "set", "static",
            "string", "super", "switch", "this", "throw", "true", "try", "type", "typeof", "undefined",
            "var", "void", "while", "with", "yield", "async", "await"
        ],
        [Language.JavaScript] = [
            "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger",
            "default", "delete", "do", "else", "export", "extends", "false", "finally", "for",
            "function", "if", "import", "in", "instanceof", "let", "new", "null", "return",
            "static", "super", "switch", "this", "throw", "true", "try", "typeof", "undefined",
            "var", "void", "while", "with", "yield"
        ],
        [Language.Python] = [
            "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class",
            "continue", "def", "del", "elif", "else", "except", "finally", "for", "from", "global",
            "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise",
            "return", "try", "while", "with", "yield"
        ],
        [Language.Rust] = [
            "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum",
            "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod",
            "move", "mut", "pub", "ref", "return", "self", "Self", "static", "struct", "super",
            "trait", "true", "type", "unsafe", "use", "where", "while"
        ],
        [Language.Go] = [
            "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough",
            "for", "func", "go", "goto", "if", "import", "interface", "map", "package", "range",
            "return", "select", "struct", "switch", "type", "var"
        ],
        [Language.Cpp] = [
            "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break",
            "case", "catch", "char", "char8_t", "char16_t", "char32_t", "class", "compl", "concept",
            "const", "consteval", "constexpr", "constinit", "const_cast", "continue", "co_await",
            "co_return", "co_yield", "decltype", "default", "delete", "do", "double", "dynamic_cast",
            "else", "enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto",
            "if", "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq",
            "nullptr", "operator", "or", "or_eq", "private", "protected", "public", "register",
            "reinterpret_cast", "requires", "return", "short", "signed", "sizeof", "static",
            "static_assert", "static_cast", "struct", "switch", "template", "this", "thread_local",
            "throw", "true", "try", "typedef", "typeid", "typename", "union", "unsigned", "using",
            "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq"
        ],
        [Language.C] = [
            "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else",
            "enum", "extern", "float", "for", "goto", "if", "inline", "int", "long", "register",
            "restrict", "return", "short", "signed", "sizeof", "static", "struct", "switch", "typedef",
            "union", "unsigned", "void", "volatile", "while", "_Alignas", "_Alignof", "_Atomic",
            "_Bool", "_Complex", "_Generic", "_Imaginary", "_Noreturn", "_Static_assert", "_Thread_local"
        ]
    };

    public static readonly Dictionary<Language, string[]> StringDelimiters = new()
    {
        [Language.CSharp] = ["\"", "$\"", "@\"", "'"],
        [Language.TypeScript] = ["\"", "'", "`"],
        [Language.JavaScript] = ["\"", "'", "`"],
        [Language.Python] = ["\"", "'", "\"\"\"", "'''"],
        [Language.Rust] = ["\"", "'"],
        [Language.Go] = ["\"", "'", "`"],
        [Language.Cpp] = ["\"", "'"],
        [Language.C] = ["\"", "'"],
        [Language.Json] = ["\""],
    };

    public static readonly Dictionary<Language, string[]> CommentDelimiters = new()
    {
        [Language.CSharp] = ["//", "/*", "*/"],
        [Language.TypeScript] = ["//", "/*", "*/"],
        [Language.JavaScript] = ["//", "/*", "*/"],
        [Language.Python] = ["#", "\"\"\"", "'''"],
        [Language.Rust] = ["//", "/*", "*/", "///", "//!"],
        [Language.Go] = ["//", "/*", "*/"],
        [Language.Cpp] = ["//", "/*", "*/"],
        [Language.C] = ["//", "/*", "*/"],
        [Language.Json] = [],
        [Language.Yaml] = ["#"],
        [Language.Toml] = ["#"],
        [Language.Markdown] = [],
    };
}
